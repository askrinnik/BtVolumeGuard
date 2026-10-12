namespace BtVolumeGuard;

internal static class Program
{
    /// <param name="args">
    /// Optional: <c>--mask "&lt;pattern&gt;"</c> overrides the device mask from the settings file
    /// (e.g. for testing on another endpoint).
    /// </param>
    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(initiallyOwned: true, @"Local\BtVolumeGuard", out bool createdNew);
        if (!createdNew)
        {
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext(ParseMask(args)));
    }

    private static string? ParseMask(string[] args)
    {
        int index = Array.FindIndex(args, a => a.Equals("--mask", StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
