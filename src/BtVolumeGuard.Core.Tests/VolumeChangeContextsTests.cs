using BtVolumeGuard.Core;

namespace BtVolumeGuard.Core.Tests;

public class VolumeChangeContextsTests
{
    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000", VolumeChangeSource.User)]
    [InlineData("355c8163-187b-45d8-9882-61afb3537f61", VolumeChangeSource.User)]
    [InlineData("03016827-7b05-566a-ada3-14d84347d041", VolumeChangeSource.ProfileSwitch)]
    [InlineData("0de6c1af-c506-4e60-bd8c-f3b00097de2a", VolumeChangeSource.HeadsetDriver)]
    [InlineData("7d3f8a52-6c1e-4b8e-9a0d-2f5b1c9e4a17", VolumeChangeSource.Own)]
    [InlineData("12345678-1234-1234-1234-123456789abc", VolumeChangeSource.Unknown)]
    public void Context_is_classified(string context, VolumeChangeSource expected) =>
        Assert.Equal(expected, VolumeChangeContexts.Classify(Guid.Parse(context)));
}
