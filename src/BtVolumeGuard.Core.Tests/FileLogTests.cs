using BtVolumeGuard.Core.Logging;

namespace BtVolumeGuard.Core.Tests;

public sealed class FileLogTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("BtVolumeGuardTests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string[] WriteBoth(LogLevel level)
    {
        string path = Path.Combine(_dir, $"{level}.log");
        var log = new FileLog(path, level);

        log.Write("normal line");
        log.Verbose("verbose line");

        return File.Exists(path) ? File.ReadAllLines(path) : [];
    }

    [Fact]
    public void Off_writes_nothing_and_creates_no_file() =>
        Assert.Empty(WriteBoth(LogLevel.Off));

    [Fact]
    public void Normal_skips_verbose_lines()
    {
        string[] lines = WriteBoth(LogLevel.Normal);

        Assert.Single(lines);
        Assert.EndsWith("normal line", lines[0]);
    }

    [Fact]
    public void Verbose_writes_everything() =>
        Assert.Equal(2, WriteBoth(LogLevel.Verbose).Length);

    [Fact]
    public void Clear_removes_old_lines_and_logging_continues()
    {
        string path = Path.Combine(_dir, "clear.log");
        var log = new FileLog(path, LogLevel.Normal);

        log.Write("old");
        log.Clear();
        Assert.False(File.Exists(path));

        log.Write("new");
        Assert.EndsWith("new", Assert.Single(File.ReadAllLines(path)));
    }

    [Fact]
    public void Level_can_change_at_runtime()
    {
        string path = Path.Combine(_dir, "runtime.log");
        var log = new FileLog(path, LogLevel.Normal);

        log.Verbose("dropped");
        log.Level = LogLevel.Verbose;
        log.Verbose("kept");

        Assert.EndsWith("kept", Assert.Single(File.ReadAllLines(path)));
    }
}
