namespace BtVolumeGuard.Core;

/// <summary>
/// The app version for display, parsed from the assembly's informational version
/// (<c>0.2.1+&lt;commit hash&gt;</c>: the SDK appends the full source commit hash).
/// </summary>
public sealed record AppVersion(string Version, string? Commit)
{
    public const int ShortCommitLength = 7;

    public static AppVersion Parse(string? informationalVersion)
    {
        string text = informationalVersion?.Trim() ?? "";
        int plus = text.IndexOf('+');
        string version = plus >= 0 ? text[..plus] : text;
        string metadata = plus >= 0 ? text[(plus + 1)..] : "";
        return new AppVersion(
            version.Length > 0 ? version : "unknown",
            metadata.Length > 0 ? metadata[..Math.Min(ShortCommitLength, metadata.Length)] : null);
    }
}
