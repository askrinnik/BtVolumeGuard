using System.Globalization;

namespace BtVolumeGuard.Core.Logging;

/// <summary>Appends timestamped lines to a UTF-8 text file.</summary>
public sealed class FileLog : ILog
{
    private readonly Lock _lock = new();
    private readonly TimeProvider _time;
    private int _level;

    public FileLog(string path, LogLevel level = LogLevel.Normal, TimeProvider? time = null)
    {
        Path = path;
        Level = level;
        _time = time ?? TimeProvider.System;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
    }

    public string Path { get; }

    /// <summary>
    /// Default log location: BtVolumeGuard.log next to the executable. The app is meant to run
    /// from a user-writable folder (not Program Files), so the log lives with it.
    /// </summary>
    public static string DefaultPath { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "BtVolumeGuard.log");

    /// <summary>Messages above this level are dropped. Can be changed from any thread.</summary>
    public LogLevel Level
    {
        get => (LogLevel)Volatile.Read(ref _level);
        set => Volatile.Write(ref _level, (int)value);
    }

    /// <summary>Deletes the log file. Lines written afterwards start a new file.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            try
            {
                File.Delete(Path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    public void Write(LogLevel level, string message)
    {
        if (level == LogLevel.Off || level > Level)
        {
            return;
        }

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
