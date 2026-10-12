using System.Text.Json;

namespace BtVolumeGuard.Core.Settings;

/// <summary>
/// Last known volume (%) below maximum per endpoint ID, persisted as JSON next to the executable
/// so that it survives restarts. Not thread-safe: used on the guard's worker thread only.
/// </summary>
public sealed class VolumeMemory
{
    public const string FileName = "BtVolumeGuard.state.json";

    private static readonly JsonSerializerOptions s_json = new() { WriteIndented = true };

    private readonly Dictionary<string, int> _volumes;
    private readonly string? _path;

    private VolumeMemory(Dictionary<string, int> volumes, string? path)
    {
        _volumes = volumes;
        _path = path;
    }

    public static string DefaultPath { get; } = Path.Combine(AppContext.BaseDirectory, FileName);

    /// <summary>Memory that is not persisted.</summary>
    public static VolumeMemory InMemory() => new([], null);

    /// <summary>
    /// Loads remembered volumes from <paramref name="path"/>. A missing file means nothing is
    /// remembered yet; an unreadable file is reported and replaced on the next save.
    /// </summary>
    public static (VolumeMemory Memory, string? Problem) Load(string path)
    {
        if (!File.Exists(path))
        {
            return (new VolumeMemory([], path), null);
        }

        try
        {
            Dictionary<string, int> volumes =
                JsonSerializer.Deserialize<StateFile>(File.ReadAllText(path), s_json)?.RememberedVolumes ?? [];
            return (new VolumeMemory(volumes, path), null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return (new VolumeMemory([], path), $"Cannot read {path}: {ex.Message} Starting with no remembered volumes.");
        }
    }

    public int? Get(string endpointId) => _volumes.TryGetValue(endpointId, out int volume) ? volume : null;

    /// <summary>Remembers the volume and saves the file if it changed. Returns a problem description if saving failed.</summary>
    public string? Set(string endpointId, int volume)
    {
        if (_volumes.TryGetValue(endpointId, out int existing) && existing == volume)
        {
            return null;
        }

        _volumes[endpointId] = volume;
        if (_path is null)
        {
            return null;
        }

        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(new StateFile(_volumes), s_json));
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Cannot save {_path}: {ex.Message}";
        }
    }

    private sealed record StateFile(Dictionary<string, int> RememberedVolumes);
}
