using BtVolumeGuard.Core;
using BtVolumeGuard.Core.Logging;
using BtVolumeGuard.Core.Settings;

namespace BtVolumeGuard.Core.Tests;

public sealed class SettingsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("BtVolumeGuardTests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string PathOf(string name) => Path.Combine(_dir, name);

    [Fact]
    public void Missing_settings_file_is_created_with_defaults()
    {
        string path = PathOf(AppSettings.FileName);

        (AppSettings settings, string? problem) = AppSettings.Load(path);

        Assert.Null(problem);
        Assert.Equal(new AppSettings(), settings);
        Assert.True(File.Exists(path));
        Assert.Equal(new AppSettings(), AppSettings.Load(path).Settings);
    }

    [Fact]
    public void Settings_are_read_with_comments_and_missing_properties_defaulted()
    {
        string path = PathOf(AppSettings.FileName);
        File.WriteAllText(path, """
            {
              // the speaker is shared with the family, leave it alone
              "ExcludedDevices": [ "Speakers (JBL Flip 4)" ],
              "HeadsetDriverContexts": [ "0de6c1af-c506-4e60-bd8c-f3b00097de2a", "11111111-2222-3333-4444-555555555555" ],
              "DefaultVolume": 30,
              "LogLevel": "Verbose",
            }
            """);

        (AppSettings settings, string? problem) = AppSettings.Load(path);

        Assert.Null(problem);
        Assert.Equal(["Speakers (JBL Flip 4)"], settings.ExcludedDevices);
        Assert.Equal(2, settings.HeadsetDriverContexts.Count);
        Assert.Equal([VolumeChangeContexts.DefaultProfileSwitch], settings.ProfileSwitchContexts);
        Assert.Equal(30, settings.DefaultVolume);
        Assert.Equal(LogLevel.Verbose, settings.LogLevel);
        Assert.Equal(95, settings.MaxThreshold);
        Assert.Equal(TimeSpan.FromSeconds(2), settings.ToPolicy().ProfileSwitchWindow);
        Assert.Equal(
            VolumeChangeSource.HeadsetDriver,
            settings.ToContexts().Classify(new Guid("11111111-2222-3333-4444-555555555555")));
        Assert.False(settings.ToSelection().IsGuarded("Speakers (JBL Flip 4)", isBluetooth: true));
    }

    [Fact]
    public void Settings_from_version_0_1_0_with_a_device_mask_still_load()
    {
        string path = PathOf(AppSettings.FileName);
        File.WriteAllText(path, """{ "DeviceMask": "*LK12*", "NotifyOnRevert": true }""");

        (AppSettings settings, string? problem) = AppSettings.Load(path);

        Assert.Null(problem);
        Assert.True(settings.NotifyOnRevert);
        Assert.Empty(settings.ExcludedDevices);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("""{ "DefaultVolume": 99 }""")]
    [InlineData("""{ "ExcludedDevices": null }""")]
    [InlineData("""{ "UserContexts": [ "not a guid" ] }""")]
    [InlineData("""{ "LogLevel": "Loud" }""")]
    public void Invalid_settings_file_gives_defaults_and_a_problem_and_is_kept(string content)
    {
        string path = PathOf(AppSettings.FileName);
        File.WriteAllText(path, content);

        (AppSettings settings, string? problem) = AppSettings.Load(path);

        Assert.Equal(new AppSettings(), settings);
        Assert.NotNull(problem);
        Assert.Equal(content, File.ReadAllText(path));
    }

    [Fact]
    public void Remembered_volumes_survive_reload()
    {
        string path = PathOf(VolumeMemory.FileName);
        VolumeMemory memory = VolumeMemory.Load(path).Memory;

        Assert.Null(memory.Set("{endpoint-a}", 35));
        Assert.Null(memory.Set("{endpoint-b}", 20));

        VolumeMemory reloaded = VolumeMemory.Load(path).Memory;
        Assert.Equal(35, reloaded.Get("{endpoint-a}"));
        Assert.Equal(20, reloaded.Get("{endpoint-b}"));
        Assert.Null(reloaded.Get("{endpoint-c}"));
    }

    [Fact]
    public void Missing_state_file_means_nothing_remembered()
    {
        (VolumeMemory memory, string? problem) = VolumeMemory.Load(PathOf(VolumeMemory.FileName));

        Assert.Null(problem);
        Assert.Null(memory.Get("{endpoint-a}"));
    }

    [Fact]
    public void Corrupt_state_file_is_reported_and_replaced_on_next_save()
    {
        string path = PathOf(VolumeMemory.FileName);
        File.WriteAllText(path, "garbage");

        (VolumeMemory memory, string? problem) = VolumeMemory.Load(path);
        Assert.NotNull(problem);

        memory.Set("{endpoint-a}", 40);
        Assert.Equal(40, VolumeMemory.Load(path).Memory.Get("{endpoint-a}"));
    }
}
