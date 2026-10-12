namespace BtVolumeGuard.Core;

/// <summary>
/// Which playback endpoints are guarded: every Bluetooth endpoint except the excluded names
/// (chosen in the tray menu), or, in test mode, any endpoint whose name matches a mask.
/// Endpoints are identified by friendly name, so one headset paired several times (several
/// endpoint IDs with the same name) is one device.
/// </summary>
public sealed class DeviceSelection
{
    private readonly HashSet<string> _excluded;

    public DeviceSelection(IEnumerable<string>? excludedNames = null, DeviceNameMask? testMask = null)
    {
        _excluded = new HashSet<string>(excludedNames ?? [], StringComparer.OrdinalIgnoreCase);
        TestMask = testMask;
    }

    /// <summary>Set from the command line (<c>--mask</c>): guard matching endpoints of any kind.</summary>
    public DeviceNameMask? TestMask { get; }

    public IReadOnlyCollection<string> ExcludedNames => _excluded;

    /// <summary>Whether the endpoint is offered as a device at all (listed in the menu).</summary>
    public bool IsCandidate(string name, bool isBluetooth) =>
        TestMask is { } mask ? mask.IsMatch(name) : isBluetooth;

    public bool IsGuarded(string name, bool isBluetooth) =>
        IsCandidate(name, isBluetooth) && !_excluded.Contains(name);

    public DeviceSelection WithGuarded(string name, bool guarded)
    {
        var excluded = new HashSet<string>(_excluded, StringComparer.OrdinalIgnoreCase);
        if (guarded)
        {
            excluded.Remove(name);
        }
        else
        {
            excluded.Add(name);
        }

        return new DeviceSelection(excluded, TestMask);
    }

    public override string ToString() =>
        TestMask is { } mask
            ? $"test mask '{mask}'"
            : _excluded.Count == 0
                ? "all Bluetooth devices"
                : $"all Bluetooth devices except {string.Join(", ", _excluded.Order(StringComparer.OrdinalIgnoreCase))}";
}

/// <summary>A device offered in the tray menu: one friendly name, possibly several endpoints.</summary>
public sealed record KnownDevice(string Name, bool Connected, bool Guarded);
