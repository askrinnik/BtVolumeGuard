namespace BtVolumeGuard.Core;

/// <summary>
/// Tells Bluetooth playback endpoints apart from built-in, HDMI and USB ones, from two endpoint
/// properties read by the Core Audio layer.
/// </summary>
/// <remarks>
/// Observed on Windows 11 (docs/design.md): a classic Bluetooth endpoint has the device
/// enumerator <c>BTHENUM</c> (or <c>BTHHFENUM</c> for Hands-Free). When Bluetooth audio is
/// routed through Intel Smart Sound Technology, the enumerator is <c>INTELAUDIO</c>, but the
/// endpoint's association path still points at the Bluetooth node (<c>BTHENUM\{0000110B-…}</c>).
/// </remarks>
public static class BluetoothEndpoint
{
    public static bool IsBluetooth(string? enumeratorName, string? associationPath) =>
        IsBluetoothEnumerator(enumeratorName) || ContainsBluetoothEnumerator(associationPath);

    private static bool IsBluetoothEnumerator(string? value) =>
        value is not null
        && (value.Equals("BTHENUM", StringComparison.OrdinalIgnoreCase)
            || value.Equals("BTHHFENUM", StringComparison.OrdinalIgnoreCase));

    private static bool ContainsBluetoothEnumerator(string? value) =>
        value is not null
        && (value.Contains(@"BTHENUM\", StringComparison.OrdinalIgnoreCase)
            || value.Contains(@"BTHHFENUM\", StringComparison.OrdinalIgnoreCase));
}
