using BtVolumeGuard.Core;

namespace BtVolumeGuard;

internal static class Program
{
    private const string DefaultMask = "*LK12*";

    /// <param name="args">Optional: <c>--mask "&lt;pattern&gt;"</c> to watch other endpoints (e.g. for testing).</param>
    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(initiallyOwned: true, @"Local\BtVolumeGuard", out bool createdNew);
        if (!createdNew)
        {
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext(new DeviceNameMask(ParseMask(args))));
    }

    private static string ParseMask(string[] args)
    {
        int index = Array.FindIndex(args, a => a.Equals("--mask", StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : DefaultMask;
    }
}
