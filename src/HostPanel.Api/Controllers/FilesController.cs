using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HostPanel.Core.DTOs;
using HostPanel.Core.Enums;
using HostPanel.Core.Interfaces;
using HostPanel.Infrastructure.Data;
using HostPanel.Modules.FileManager;

namespace HostPanel.Api.Controllers;

[ApiController]
[Route("api/v1/domains/{domainId}/files")]
[Authorize]
public class FilesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly FileManagerService _fileManager;
    private readonly IBillingMeter _meter;

    public FilesController(AppDbContext db, FileManagerService fileManager, IBillingMeter meter)
    {
        _db = db;
        _fileManager = fileManager;
        _meter = meter;
    }

    [HttpGet]
    public async Task<ActionResult<FileListResponse>> List(int domainId, [FromQuery] string path = "/")
    {
        var basePath = await GetDomainBasePathAsync(domainId);
        if (basePath is null) return NotFound();

        try
        {
            return Ok(_fileManager.ListDirectory(basePath, path));
        }
        catch (DirectoryNotFoundException)
        {
            return NotFound(new { message = "Directory not found" });
        }
    }

    [HttpGet("read")]
    public async Task<ActionResult<FileContentResponse>> ReadFile(int domainId, [FromQuery] string path)
    {
        var basePath = await GetDomainBasePathAsync(domainId);
        if (basePath is null) return NotFound();

        try
        {
            return Ok(_fileManager.ReadFile(basePath, path));
        }
        catch (FileNotFoundException)
        {
            return NotFound(new { message = "File not found" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut]
    public async Task<IActionResult> SaveFile(int domainId, [FromBody] SaveFileRequest request)
    {
        var basePath = await GetDomainBasePathAsync(domainId);
        if (basePath is null) return NotFound();

        try
        {
            _fileManager.SaveFile(basePath, request.Path, request.Content);
            return Ok(new { success = true });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpPost("upload")]
    [RequestSizeLimit(100 * 1024 * 1024)] // 100MB max
    public async Task<IActionResult> Upload(int domainId, [FromQuery] string path, IFormFile file)
    {
        var basePath = await GetDomainBasePathAsync(domainId);
        if (basePath is null) return NotFound();

        // Quota enforcement (#55): block uploads that would push disk usage past the plan limit.
        var sub = await _db.Domains
            .Where(d => d.Id == domainId)
            .Select(d => new { d.SubscriptionId, d.Subscription!.ServicePlan!.DiskQuotaMb })
            .FirstOrDefaultAsync();
        if (sub is not null && sub.DiskQuotaMb > 0)
        {
            var currentBytes = await _meter.GetDiskUsageBytesAsync(sub.SubscriptionId);
            var limitBytes = sub.DiskQuotaMb * 1024L * 1024L;
            if (currentBytes + file.Length > limitBytes)
            {
                return StatusCode(StatusCodes.Status413PayloadTooLarge, new
                {
                    message = "Disk quota exceeded.",
                    diskQuotaMb = sub.DiskQuotaMb,
                    currentUsageMb = currentBytes / (1024.0 * 1024.0),
                    fileSizeMb = file.Length / (1024.0 * 1024.0),
                });
            }
        }

        var filePath = string.IsNullOrEmpty(path) || path == "/"
            ? file.FileName
            : $"{path.TrimEnd('/')}/{file.FileName}";

        using var stream = file.OpenReadStream();
        await _fileManager.UploadFileAsync(basePath, filePath, stream, HttpContext.RequestAborted);

        return Ok(new { success = true, path = filePath });
    }

    [HttpGet("download")]
    public async Task<IActionResult> Download(int domainId, [FromQuery] string path)
    {
        var basePath = await GetDomainBasePathAsync(domainId);
        if (basePath is null) return NotFound();

        try
        {
            var (stream, fileName) = _fileManager.DownloadFile(basePath, path);
            return File(stream, "application/octet-stream", fileName);
        }
        catch (FileNotFoundException)
        {
            return NotFound(new { message = "File not found" });
        }
    }

    [HttpPost("mkdir")]
    public async Task<IActionResult> CreateDirectory(int domainId, [FromBody] CreateDirectoryRequest request)
    {
        var basePath = await GetDomainBasePathAsync(domainId);
        if (basePath is null) return NotFound();

        _fileManager.CreateDirectory(basePath, request.Path);
        return Ok(new { success = true });
    }

    [HttpDelete]
    public async Task<IActionResult> Delete(int domainId, [FromQuery] string path)
    {
        var basePath = await GetDomainBasePathAsync(domainId);
        if (basePath is null) return NotFound();

        try
        {
            _fileManager.Delete(basePath, path);
            return NoContent();
        }
        catch (FileNotFoundException)
        {
            return NotFound(new { message = "Not found" });
        }
    }

    [HttpPost("rename")]
    public async Task<IActionResult> Rename(int domainId, [FromBody] RenameRequest request)
    {
        var basePath = await GetDomainBasePathAsync(domainId);
        if (basePath is null) return NotFound();

        try
        {
            _fileManager.Rename(basePath, request.OldPath, request.NewPath);
            return Ok(new { success = true });
        }
        catch (FileNotFoundException)
        {
            return NotFound(new { message = "Not found" });
        }
    }

    private async Task<string?> GetDomainBasePathAsync(int domainId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var role = User.FindFirstValue(ClaimTypes.Role);

        var domain = await _db.Domains
            .Include(d => d.Subscription)
            .ThenInclude(s => s.User)
            .FirstOrDefaultAsync(d => d.Id == domainId);

        if (domain is null) return null;

        // Access check
        if (role == UserRole.Customer.ToString() && domain.Subscription.UserId != userId)
            return null;
        if (role == UserRole.Reseller.ToString() &&
            domain.Subscription.UserId != userId &&
            domain.Subscription.User.ParentUserId != userId)
            return null;

        // Bug #45: main-domain rows can have a null/empty DocumentRoot if they were
        // created via the subscription create flow (which set it on the Subscription,
        // not the Domain row). Fall back to the canonical layout if so.
        if (!string.IsNullOrEmpty(domain.DocumentRoot) && System.IO.Directory.Exists(domain.DocumentRoot))
            return domain.DocumentRoot;

        var username = domain.Subscription.SystemUsername;
        if (!string.IsNullOrEmpty(username))
        {
            var canonical = $"/var/www/{username}/httpdocs";
            if (System.IO.Directory.Exists(canonical)) return canonical;
            var legacy = $"/var/www/{username}";
            if (System.IO.Directory.Exists(legacy)) return legacy;
        }

        // Last resort — let the caller see the original (possibly broken) path so the
        // 404 message is informative.
        return domain.DocumentRoot;
    }
}
