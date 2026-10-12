namespace BtVolumeGuard.Core.Audio;

/// <summary>A watched endpoint and its current volume (%).</summary>
public sealed record EndpointStatus(string Name, int Volume, bool Muted);

/// <summary>A jump the guard reverted: <paramref name="From"/>% was set back to <paramref name="To"/>%.</summary>
public sealed record RevertInfo(string EndpointName, int From, int To);
