using BtVolumeGuard.Core;

namespace BtVolumeGuard.Core.Tests;

public class DeviceSelectionTests
{
    [Fact]
    public void By_default_every_Bluetooth_device_is_guarded_and_nothing_else()
    {
        var selection = new DeviceSelection();

        Assert.True(selection.IsGuarded("Headphones (LK12)", isBluetooth: true));
        Assert.True(selection.IsGuarded("Speakers (JBL Flip 4)", isBluetooth: true));
        Assert.False(selection.IsGuarded("Speakers (Realtek(R) Audio)", isBluetooth: false));
        Assert.False(selection.IsCandidate("Speakers (Realtek(R) Audio)", isBluetooth: false));
    }

    [Fact]
    public void Excluded_device_is_listed_but_not_guarded_case_insensitively()
    {
        var selection = new DeviceSelection(["speakers (jbl flip 4)"]);

        Assert.True(selection.IsCandidate("Speakers (JBL Flip 4)", isBluetooth: true));
        Assert.False(selection.IsGuarded("Speakers (JBL Flip 4)", isBluetooth: true));
        Assert.True(selection.IsGuarded("Headphones (LK12)", isBluetooth: true));
    }

    [Fact]
    public void Toggling_a_device_returns_a_new_selection()
    {
        var original = new DeviceSelection();

        DeviceSelection excluded = original.WithGuarded("Speakers (JBL Flip 4)", guarded: false);
        DeviceSelection back = excluded.WithGuarded("Speakers (JBL Flip 4)", guarded: true);

        Assert.True(original.IsGuarded("Speakers (JBL Flip 4)", isBluetooth: true));
        Assert.Equal(["Speakers (JBL Flip 4)"], excluded.ExcludedNames);
        Assert.Empty(back.ExcludedNames);
    }

    [Fact]
    public void Test_mask_guards_matching_devices_of_any_kind()
    {
        var selection = new DeviceSelection(testMask: new DeviceNameMask("Speakers (Realtek*"));

        Assert.True(selection.IsGuarded("Speakers (Realtek(R) Audio)", isBluetooth: false));
        Assert.False(selection.IsGuarded("Headphones (LK12)", isBluetooth: true));
    }

    [Fact]
    public void Description_names_the_exclusions() =>
        Assert.Equal(
            "all Bluetooth devices except Speakers (JBL Flip 4)",
            new DeviceSelection(["Speakers (JBL Flip 4)"]).ToString());
}
