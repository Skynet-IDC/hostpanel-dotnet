using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HostPanel.Core.DTOs;
using HostPanel.Core.Enums;
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

    public FilesController(AppDbContext db, FileManagerService fileManager)
    {
        _db = db;
        _fileManager = fileManager;
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

        return domain.DocumentRoot;
    }
}
