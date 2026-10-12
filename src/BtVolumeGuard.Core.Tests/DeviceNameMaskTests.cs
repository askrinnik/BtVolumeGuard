using BtVolumeGuard.Core;

namespace BtVolumeGuard.Core.Tests;

public class DeviceNameMaskTests
{
    private readonly DeviceNameMask _mask = new("*LK12*");

    [Theory]
    [InlineData("Headphones (LK12)")]
    [InlineData("Headset (LK12 Hands-Free)")]
    [InlineData("headset (lk12 hands-free)")]
    public void Headset_endpoints_match(string name) =>
        Assert.True(_mask.IsMatch(name));

    [Theory]
    [InlineData("Speakers (Realtek(R) Audio)")]
    [InlineData("LK1")]
    [InlineData("")]
    public void Other_endpoints_do_not_match(string name) =>
        Assert.False(_mask.IsMatch(name));

    [Fact]
    public void Question_mark_matches_one_character()
    {
        var mask = new DeviceNameMask("Headset (LK1?)");

        Assert.True(mask.IsMatch("Headset (LK12)"));
        Assert.False(mask.IsMatch("Headset (LK123)"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Empty_pattern_is_rejected(string pattern) =>
        Assert.ThrowsAny<ArgumentException>(() => new DeviceNameMask(pattern));
}
