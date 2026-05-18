using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.Filters;
using HostPanel.Core.Models;
using HostPanel.Infrastructure.Data;

namespace HostPanel.Api.Middleware;

/// <summary>
/// Global action filter that automatically records audit log entries for every
/// non-GET controller action. Captures user, action (Controller.Method), entity type,
/// entity ID (if present as route param), IP, trace ID, and result status.
/// </summary>
public class AuditLogFilter : IAsyncActionFilter
{
    private readonly AppDbContext _db;
    private readonly ILogger<AuditLogFilter> _logger;

    public AuditLogFilter(AppDbContext db, ILogger<AuditLogFilter> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var method = context.HttpContext.Request.Method;
        // Only log mutating actions
        if (method == "GET" || method == "HEAD" || method == "OPTIONS")
        {
            await next();
            return;
        }

        var executed = await next();

        // Don't log unauthenticated requests (login attempts etc. — would be noisy)
        var userId = context.HttpContext.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return;

        // Don't log failed model binding or unauthorized
        var status = executed.HttpContext.Response.StatusCode;
        if (status == 400 && executed.Exception is null) return;
        if (status == 401 || status == 403) return;

        try
        {
            var controllerName = (context.RouteData.Values["controller"] as string) ?? "?";
            var actionName = (context.RouteData.Values["action"] as string) ?? "?";
            int? entityId = null;
            if (context.RouteData.Values.TryGetValue("id", out var idObj) &&
                int.TryParse(idObj?.ToString(), out var parsed)) entityId = parsed;

            _db.AuditLogs.Add(new AuditLog
            {
                UserId = userId,
                Action = $"{method} {controllerName}.{actionName}",
                EntityType = controllerName,
                EntityId = entityId,
                Details = $"status={status}",
                IpAddress = context.HttpContext.Connection.RemoteIpAddress?.ToString(),
                TraceId = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier,
                CreatedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Never fail the request just because audit logging failed
            _logger.LogWarning(ex, "Audit log write failed for {Path}", context.HttpContext.Request.Path);
        }
    }
}
