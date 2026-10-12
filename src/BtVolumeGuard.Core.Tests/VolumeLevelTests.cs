using BtVolumeGuard.Core;

namespace BtVolumeGuard.Core.Tests;

public class VolumeLevelTests
{
    [Theory]
    [InlineData(0f, 0)]
    [InlineData(0.2f, 20)]
    [InlineData(0.256f, 26)]
    [InlineData(0.254f, 25)]
    [InlineData(0.999f, 100)]
    [InlineData(1f, 100)]
    [InlineData(-0.1f, 0)]
    [InlineData(1.1f, 100)]
    public void Scalar_is_rounded_to_percent(float scalar, int expected) =>
        Assert.Equal(expected, VolumeLevel.ToPercent(scalar));
}
