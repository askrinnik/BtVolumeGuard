using System.Globalization;

namespace BtVolumeGuard.Core.Logging;

/// <summary>Appends timestamped lines to a UTF-8 text file.</summary>
public sealed class FileLog : ILog
{
    private readonly Lock _lock = new();
    private readonly TimeProvider _time;

    public FileLog(string path, TimeProvider? time = null)
    {
        Path = path;
        _time = time ?? TimeProvider.System;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
    }

    public string Path { get; }

    /// <summary>Default log location: %LOCALAPPDATA%\BtVolumeGuard\BtVolumeGuard.log.</summary>
    public static string DefaultPath { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BtVolumeGuard",
        "BtVolumeGuard.log");

    public void Write(string message)
    {
        string line = string.Create(
            CultureInfo.InvariantCulture,
            $"{_time.GetLocalNow():yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}");

        lock (_lock)
        {
            try
            {
                File.AppendAllText(Path, line);
            }
            catch (IOException)
            {
                // The log is diagnostic only; never let it break the guard.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
