using BtVolumeGuard.Core;

namespace BtVolumeGuard.Core.Tests;

public class AppVersionTests
{
    [Theory]
    [InlineData("0.2.1+96eec58a1b2c3d4e5f60718293a4b5c6d7e8f901", "0.2.1", "96eec58")]
    [InlineData("0.2.1+abc", "0.2.1", "abc")]
    [InlineData("0.2.1", "0.2.1", null)]
    [InlineData("0.2.1+", "0.2.1", null)]
    [InlineData(" 1.0.0-beta+1234567890 ", "1.0.0-beta", "1234567")]
    [InlineData("", "unknown", null)]
    [InlineData(null, "unknown", null)]
    public void Informational_version_is_split_into_version_and_short_commit(
        string? informational, string version, string? commit) =>
        Assert.Equal(new AppVersion(version, commit), AppVersion.Parse(informational));
}
