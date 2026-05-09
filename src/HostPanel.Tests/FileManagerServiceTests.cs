using Microsoft.Extensions.Logging.Abstractions;
using HostPanel.Modules.FileManager;

namespace HostPanel.Tests;

public class FileManagerServiceTests : IDisposable
{
    private readonly FileManagerService _service;
    private readonly string _testDir;

    public FileManagerServiceTests()
    {
        _service = new FileManagerService(NullLogger<FileManagerService>.Instance);
        _testDir = Path.Combine(Path.GetTempPath(), $"hostpanel_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        File.WriteAllText(Path.Combine(_testDir, "test.txt"), "hello world");
        Directory.CreateDirectory(Path.Combine(_testDir, "subdir"));
    }

    [Fact]
    public void ListDirectory_ReturnsFilesAndDirs()
    {
        var result = _service.ListDirectory(_testDir, ".");
        Assert.NotEmpty(result.Entries);
        Assert.Contains(result.Entries, e => e.Name == "test.txt" && !e.IsDirectory);
        Assert.Contains(result.Entries, e => e.Name == "subdir" && e.IsDirectory);
    }

    [Fact]
    public void ReadFile_ReturnsContent()
    {
        var result = _service.ReadFile(_testDir, "test.txt");
        Assert.Equal("hello world", result.Content);
    }

    [Fact]
    public void SaveFile_WritesContent()
    {
        _service.SaveFile(_testDir, "new.txt", "new content");
        Assert.True(File.Exists(Path.Combine(_testDir, "new.txt")));
        Assert.Equal("new content", File.ReadAllText(Path.Combine(_testDir, "new.txt")));
    }

    [Fact]
    public void ResolveSafePath_BlocksTraversal()
    {
        Assert.Throws<UnauthorizedAccessException>(() => _service.ReadFile(_testDir, "../../etc/passwd"));
    }

    [Fact]
    public void Delete_RemovesFile()
    {
        _service.SaveFile(_testDir, "todelete.txt", "x");
        _service.Delete(_testDir, "todelete.txt");
        Assert.False(File.Exists(Path.Combine(_testDir, "todelete.txt")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir)) Directory.Delete(_testDir, true);
    }
}
