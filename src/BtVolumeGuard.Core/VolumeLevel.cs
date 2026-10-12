namespace BtVolumeGuard.Core;

/// <summary>Conversion between Core Audio scalar volume (0.0..1.0) and percent (0..100).</summary>
public static class VolumeLevel
{
    public static int ToPercent(float scalar) =>
        (int)Math.Round(Math.Clamp(scalar, 0f, 1f) * 100, MidpointRounding.AwayFromZero);
}
