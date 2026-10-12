using System.IO.Enumeration;

namespace BtVolumeGuard.Core;

/// <summary>
/// Case-insensitive wildcard match of endpoint friendly names (<c>*</c> = any run of characters,
/// <c>?</c> = one character), e.g. <c>*LK12*</c>.
/// </summary>
public sealed class DeviceNameMask
{
    public DeviceNameMask(string pattern)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        Pattern = pattern;
    }

    public string Pattern { get; }

    public bool IsMatch(string name) =>
        FileSystemName.MatchesSimpleExpression(Pattern, name, ignoreCase: true);

    public override string ToString() => Pattern;
}
