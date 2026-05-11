using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using HostPanel.Core.DTOs;
using HostPanel.Core.Enums;
using HostPanel.Core.Models;
using HostPanel.Infrastructure.Services;

namespace HostPanel.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class AuthController : ControllerBase
{
    private readonly UserManager<AppUser> _userManager;
    private readonly SignInManager<AppUser> _signInManager;
    private readonly JwtService _jwtService;

    public AuthController(
        UserManager<AppUser> userManager,
        SignInManager<AppUser> signInManager,
        JwtService jwtService)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _jwtService = jwtService;
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest request)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user is null || !user.IsActive)
            return Unauthorized(new { message = "Invalid credentials" });

        var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!result.Succeeded)
            return Unauthorized(new { message = "Invalid credentials" });

        var token = _jwtService.GenerateToken(user);
        var refreshToken = _jwtService.GenerateRefreshToken();

        return Ok(new AuthResponse(
            Token: token,
            RefreshToken: refreshToken,
            ExpiresAt: DateTime.UtcNow.AddHours(24),
            User: MapToDto(user)
        ));
    }

    [HttpPost("register")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin,Reseller")]
    public async Task<ActionResult<AuthResponse>> Register([FromBody] RegisterRequest request)
    {
        var existingUser = await _userManager.FindByEmailAsync(request.Email);
        if (existingUser is not null)
            return Conflict(new { message = "Email already registered" });

        var user = new AppUser
        {
            UserName = request.Email,
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName,
            CompanyName = request.CompanyName,
            Role = request.Role,
            EmailConfirmed = true
        };

        // Resellers can only create customers
        var currentUserRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        if (currentUserRole == UserRole.Reseller.ToString() && request.Role != UserRole.Customer)
            return Forbid();

        // Set parent user for reseller-created customers
        if (currentUserRole == UserRole.Reseller.ToString())
        {
            var currentUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            user.ParentUserId = currentUserId;
        }

        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
            return BadRequest(new { errors = result.Errors.Select(e => e.Description) });

        await _userManager.AddToRoleAsync(user, request.Role.ToString());

        var token = _jwtService.GenerateToken(user);
        var refreshToken = _jwtService.GenerateRefreshToken();

        return CreatedAtAction(nameof(Login), new AuthResponse(
            Token: token,
            RefreshToken: refreshToken,
            ExpiresAt: DateTime.UtcNow.AddHours(24),
            User: MapToDto(user)
        ));
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<UserDto>> Me()
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId is null)
            return Unauthorized();

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
            return NotFound();

        return Ok(MapToDto(user));
    }

    private static UserDto MapToDto(AppUser user) => new(
        Id: user.Id,
        Email: user.Email ?? "",
        FirstName: user.FirstName,
        LastName: user.LastName,
        CompanyName: user.CompanyName,
        Role: user.Role,
        IsActive: user.IsActive
    );
}
