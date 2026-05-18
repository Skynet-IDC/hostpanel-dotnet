using Microsoft.Extensions.Logging;
using HostPanel.Core.DTOs;

namespace HostPanel.Modules.FileManager;

public class FileManagerService
{
    private readonly ILogger<FileManagerService> _logger;

    public FileManagerService(ILogger<FileManagerService> logger)
    {
        _logger = logger;
    }

    public FileListResponse ListDirectory(string basePath, string relativePath)
    {
        var fullPath = ResolveSafePath(basePath, relativePath);
        if (!Directory.Exists(fullPath))
            throw new DirectoryNotFoundException($"Directory not found: {relativePath}");

        var dirInfo = new DirectoryInfo(fullPath);
        var entries = new List<FileEntry>();

        foreach (var dir in dirInfo.GetDirectories().OrderBy(d => d.Name))
        {
            entries.Add(new FileEntry(
                dir.Name,
                Path.Combine(relativePath, dir.Name).Replace('\\', '/'),
                true,
                0,
                dir.LastWriteTimeUtc,
                null
            ));
        }

        foreach (var file in dirInfo.GetFiles().OrderBy(f => f.Name))
        {
            entries.Add(new FileEntry(
                file.Name,
                Path.Combine(relativePath, file.Name).Replace('\\', '/'),
                false,
                file.Length,
                file.LastWriteTimeUtc,
                null
            ));
        }

        return new FileListResponse(relativePath, entries);
    }

    public FileContentResponse ReadFile(string basePath, string relativePath)
    {
        var fullPath = ResolveSafePath(basePath, relativePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"File not found: {relativePath}");

        var info = new FileInfo(fullPath);
        if (info.Length > 5 * 1024 * 1024)
            throw new InvalidOperationException("File too large to read in editor (max 5MB)");

        var content = File.ReadAllText(fullPath);
        return new FileContentResponse(relativePath, content, "utf-8");
    }

    public void SaveFile(string basePath, string relativePath, string content)
    {
        var fullPath = ResolveSafePath(basePath, relativePath);
        var dir = Path.GetDirectoryName(fullPath);
        if (dir is not null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(fullPath, content);
        _logger.LogInformation("File saved: {Path}", relativePath);
    }

    public void CreateDirectory(string basePath, string relativePath)
    {
        var fullPath = ResolveSafePath(basePath, relativePath);
        Directory.CreateDirectory(fullPath);
        _logger.LogInformation("Directory created: {Path}", relativePath);
    }

    public void Delete(string basePath, string relativePath)
    {
        var fullPath = ResolveSafePath(basePath, relativePath);

        if (Directory.Exists(fullPath))
        {
            Directory.Delete(fullPath, recursive: true);
            _logger.LogInformation("Directory deleted: {Path}", relativePath);
        }
        else if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
            _logger.LogInformation("File deleted: {Path}", relativePath);
        }
        else
        {
            throw new FileNotFoundException($"Not found: {relativePath}");
        }
    }

    public void Rename(string basePath, string oldRelativePath, string newRelativePath)
    {
        var oldFull = ResolveSafePath(basePath, oldRelativePath);
        var newFull = ResolveSafePath(basePath, newRelativePath);

        if (Directory.Exists(oldFull))
            Directory.Move(oldFull, newFull);
        else if (File.Exists(oldFull))
            File.Move(oldFull, newFull);
        else
            throw new FileNotFoundException($"Not found: {oldRelativePath}");

        _logger.LogInformation("Renamed {Old} -> {New}", oldRelativePath, newRelativePath);
    }

    public (Stream Stream, string FileName) DownloadFile(string basePath, string relativePath)
    {
        var fullPath = ResolveSafePath(basePath, relativePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"File not found: {relativePath}");

        var stream = File.OpenRead(fullPath);
        return (stream, Path.GetFileName(fullPath));
    }

    public async Task UploadFileAsync(string basePath, string relativePath, Stream fileStream, CancellationToken cancellationToken)
    {
        var fullPath = ResolveSafePath(basePath, relativePath);
        var dir = Path.GetDirectoryName(fullPath);
        if (dir is not null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        using var output = File.Create(fullPath);
        await fileStream.CopyToAsync(output, cancellationToken);
        _logger.LogInformation("File uploaded: {Path}", relativePath);
    }

    /// <summary>
    /// Resolves and validates a path to prevent directory traversal attacks.
    /// </summary>
    private static string ResolveSafePath(string basePath, string relativePath)
    {
        var normalizedBase = Path.GetFullPath(basePath);
        // Strip leading slashes so "/" and absolute-looking paths are treated as
        // relative to the domain root, not as filesystem root (which bypasses the check).
        var sanitized = relativePath.TrimStart('/');
        var combined = Path.GetFullPath(
            string.IsNullOrEmpty(sanitized)
                ? normalizedBase
                : Path.Combine(normalizedBase, sanitized));

        if (!combined.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Path traversal detected");

        return combined;
    }
}
