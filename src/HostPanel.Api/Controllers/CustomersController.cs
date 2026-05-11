using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HostPanel.Core.DTOs;
using HostPanel.Core.Enums;
using HostPanel.Core.Models;
using HostPanel.Infrastructure.Data;
using HostPanel.Infrastructure.Services;

namespace HostPanel.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin,Reseller")]
public class CustomersController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly UserManager<AppUser> _userManager;
    private readonly JwtService _jwtService;

    public CustomersController(AppDbContext db, UserManager<AppUser> userManager, JwtService jwtService)
    {
        _db = db;
        _userManager = userManager;
        _jwtService = jwtService;
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var role = User.FindFirstValue(ClaimTypes.Role);

        IQueryable<AppUser> query = _db.Users;

        if (role == UserRole.Reseller.ToString())
        {
            // Resellers see only their own customers
            query = query.Where(u => u.ParentUserId == currentUserId && u.Role == UserRole.Customer);
        }
        else
        {
            // Admins see all non-admin users
            query = query.Where(u => u.Role != UserRole.Admin);
        }

        var users = await query
            .Select(u => new
            {
                u.Id, u.Email, u.FirstName, u.LastName, u.CompanyName,
                u.Role, u.IsActive, u.CreatedAt, u.ParentUserId,
                subscriptionCount = u.Subscriptions.Count,
                totalDomains = u.Subscriptions.SelectMany(s => s.Domains).Count()
            })
            .OrderByDescending(u => u.CreatedAt)
            .ToListAsync();

        return Ok(users);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id)
    {
        var user = await _db.Users
            .Include(u => u.Subscriptions).ThenInclude(s => s.ServicePlan)
            .Include(u => u.Subscriptions).ThenInclude(s => s.Domains)
            .FirstOrDefaultAsync(u => u.Id == id);

        if (user is null) return NotFound();
        if (!CanAccessUser(user)) return Forbid();

        return Ok(new
        {
            user.Id, user.Email, user.FirstName, user.LastName, user.CompanyName,
            user.Role, user.IsActive, user.CreatedAt,
            subscriptions = user.Subscriptions.Select(s => new
            {
                s.Id, s.Status, planName = s.ServicePlan.Name,
                domains = s.Domains.Select(d => d.Name).ToList()
            })
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] RegisterRequest request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var currentRole = User.FindFirstValue(ClaimTypes.Role);

        // Resellers can only create customers
        if (currentRole == UserRole.Reseller.ToString() && request.Role != UserRole.Customer)
            return Forbid();

        // Only admins can create resellers
        if (request.Role == UserRole.Reseller && currentRole != UserRole.Admin.ToString())
            return Forbid();

        var existing = await _userManager.FindByEmailAsync(request.Email);
        if (existing is not null) return Conflict(new { message = "Email already exists" });

        var user = new AppUser
        {
            UserName = request.Email,
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName,
            CompanyName = request.CompanyName,
            Role = request.Role,
            ParentUserId = currentRole == UserRole.Reseller.ToString() ? currentUserId : null,
            EmailConfirmed = true
        };

        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
            return BadRequest(new { errors = result.Errors.Select(e => e.Description) });

        await _userManager.AddToRoleAsync(user, request.Role.ToString());

        return CreatedAtAction(nameof(Get), new { id = user.Id }, new
        {
            user.Id, user.Email, user.FirstName, user.LastName, user.Role
        });
    }

    [HttpPut("{id}/toggle-active")]
    public async Task<IActionResult> ToggleActive(string id)
    {
        var user = await _db.Users.FindAsync(id);
        if (user is null) return NotFound();
        if (!CanAccessUser(user)) return Forbid();

        user.IsActive = !user.IsActive;
        await _db.SaveChangesAsync();

        return Ok(new { user.Id, user.IsActive });
    }

    [HttpDelete("{id}")]
    [Authorize(AuthenticationSchemes = "MultiAuth", Roles = "Admin")]
    public async Task<IActionResult> Delete(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null) return NotFound();

        if (user.Role == UserRole.Admin)
            return BadRequest(new { message = "Cannot delete admin users" });

        await _userManager.DeleteAsync(user);
        return NoContent();
    }

    private bool CanAccessUser(AppUser user)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var role = User.FindFirstValue(ClaimTypes.Role);

        if (role == UserRole.Admin.ToString()) return true;
        if (role == UserRole.Reseller.ToString()) return user.ParentUserId == currentUserId;
        return false;
    }
}
