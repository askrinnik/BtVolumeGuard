using BtVolumeGuard.Core;

namespace BtVolumeGuard.Core.Tests;

// Property values as observed on the development machine (docs/design.md).
public class BluetoothEndpointTests
{
    [Theory]
    [InlineData("BTHENUM", null)]
    [InlineData("BTHHFENUM", null)]
    [InlineData("INTELAUDIO", @"{1}.BTHENUM\{0000110B-0000-1000-8000-00805F9B34FB}_LOCALMFG&0002\7&105BD535&0&4142E2284CFF_C00000000")]
    [InlineData("INTELAUDIO", @"{1}.BTHENUM\{0000111E-0000-1000-8000-00805F9B34FB}_HCIBYPASS")]
    public void Bluetooth_endpoints_are_recognised(string? enumerator, string? association) =>
        Assert.True(BluetoothEndpoint.IsBluetooth(enumerator, association));

    [Theory]
    [InlineData("INTELAUDIO", null)]
    [InlineData("HDAUDIO", null)]
    [InlineData("USB", null)]
    [InlineData(null, null)]
    [InlineData("INTELAUDIO", "Intel Smart Sound Technology for Bluetooth Audio")]
    public void Other_endpoints_are_not(string? enumerator, string? association) =>
        Assert.False(BluetoothEndpoint.IsBluetooth(enumerator, association));
}
