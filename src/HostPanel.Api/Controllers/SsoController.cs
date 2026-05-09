using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using HostPanel.Core.Enums;
using HostPanel.Core.Models;
using HostPanel.Infrastructure.Services;

namespace HostPanel.Api.Controllers;

[ApiController]
[Route("api/v1/sso")]
public class SsoController : ControllerBase
{
    private readonly UserManager<AppUser> _userManager;
    private readonly JwtService _jwtService;
    private readonly IConfiguration _config;

    public SsoController(UserManager<AppUser> userManager, JwtService jwtService, IConfiguration config)
    {
        _userManager = userManager;
        _jwtService = jwtService;
        _config = config;
    }

    /// <summary>
    /// Initiate OIDC login flow with Authentik.
    /// Frontend redirects the browser to this endpoint.
    /// </summary>
    [HttpGet("login")]
    public IActionResult Login([FromQuery] string? returnUrl = null)
    {
        var safeReturn = SanitizeReturnUrl(returnUrl);
        var props = new AuthenticationProperties
        {
            RedirectUri = $"/api/v1/sso/callback?returnUrl={Uri.EscapeDataString(safeReturn)}"
        };
        return Challenge(props, OpenIdConnectDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// OIDC callback - Authentik redirects here after authentication.
    /// Creates or finds the local user and issues a JWT token.
    /// </summary>
    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string? returnUrl = null)
    {
        var frontendUrl = _config["Cors:Origins:0"] ?? "http://localhost:5173";

        var authResult = await HttpContext.AuthenticateAsync(OpenIdConnectDefaults.AuthenticationScheme);
        if (!authResult.Succeeded)
            return Redirect($"{frontendUrl}/login?error=sso_failed");

        var claims = authResult.Principal?.Claims.ToList() ?? [];
        var email = claims.FirstOrDefault(c => c.Type == "email")?.Value;
        var firstName = claims.FirstOrDefault(c => c.Type == "given_name")?.Value ?? "";
        var lastName = claims.FirstOrDefault(c => c.Type == "family_name")?.Value ?? "";

        if (string.IsNullOrEmpty(email))
            return Redirect($"{frontendUrl}/login?error=no_email");

        // Find or create user
        var user = await _userManager.FindByEmailAsync(email);
        if (user is null)
        {
            var defaultRole = _config["Sso:DefaultRole"] ?? "Customer";
            Enum.TryParse<UserRole>(defaultRole, out var role);

            user = new AppUser
            {
                UserName = email,
                Email = email,
                FirstName = firstName,
                LastName = lastName,
                Role = role,
                EmailConfirmed = true,
                IsActive = true
            };

            var result = await _userManager.CreateAsync(user);
            if (!result.Succeeded)
                return Redirect($"{frontendUrl}/login?error=provision_failed");

            await _userManager.AddToRoleAsync(user, role.ToString());
        }

        if (!user.IsActive)
            return Redirect($"{frontendUrl}/login?error=account_disabled");

        // Issue short-lived SSO code, exchange for JWT on frontend POST
        // Using a temporary signed token via fragment (not query string, avoids referrer leakage)
        var token = _jwtService.GenerateToken(user);
        return Redirect($"{frontendUrl}/sso-callback#token={token}");
    }

    /// <summary>
    /// Validates returnUrl is a safe relative path (prevents open redirect).
    /// </summary>
    private static string SanitizeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrEmpty(returnUrl)) return "/";
        // Only allow relative paths starting with /
        if (!returnUrl.StartsWith('/') || returnUrl.StartsWith("//") || returnUrl.StartsWith("/\\"))
            return "/";
        return returnUrl;
    }

    /// <summary>
    /// Get SSO configuration status.
    /// </summary>
    [HttpGet("config")]
    public IActionResult GetConfig()
    {
        var enabled = !string.IsNullOrEmpty(_config["Sso:Authentik:Authority"]);
        return Ok(new
        {
            enabled,
            provider = "Authentik",
            loginUrl = enabled ? "/api/v1/sso/login" : null
        });
    }
}
