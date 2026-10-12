namespace BtVolumeGuard.Core.Logging;

/// <summary>Timestamped diagnostic log. Implementations must be thread-safe.</summary>
public interface ILog
{
    void Write(string message);
}
