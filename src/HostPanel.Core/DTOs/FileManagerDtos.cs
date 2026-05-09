namespace HostPanel.Core.DTOs;

public record FileEntry(
    string Name,
    string Path,
    bool IsDirectory,
    long SizeBytes,
    DateTime ModifiedAt,
    string? Permissions
);

public record FileListResponse(
    string CurrentPath,
    List<FileEntry> Entries
);

public record CreateDirectoryRequest(string Path);
public record RenameRequest(string OldPath, string NewPath);
public record FileContentResponse(string Path, string Content, string Encoding);
public record SaveFileRequest(string Path, string Content);
