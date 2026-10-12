namespace BtVolumeGuard.Core.Logging;

/// <summary>How much is written to the log.</summary>
public enum LogLevel
{
    /// <summary>Nothing is written.</summary>
    Off,

    /// <summary>Start and stop, endpoints, reverted jumps, settings changes, errors. A few lines per call.</summary>
    Normal,

    /// <summary>Everything, including every volume change with its event context (for diagnostics).</summary>
    Verbose,
}

/// <summary>Timestamped diagnostic log. Implementations must be thread-safe.</summary>
public interface ILog
{
    /// <summary>Writes the message if <paramref name="level"/> is enabled.</summary>
    void Write(LogLevel level, string message);
}

public static class LogExtensions
{
    public static void Write(this ILog log, string message) => log.Write(LogLevel.Normal, message);

    public static void Verbose(this ILog log, string message) => log.Write(LogLevel.Verbose, message);
}
