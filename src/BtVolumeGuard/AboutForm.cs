using System.Diagnostics;
using System.Reflection;

using BtVolumeGuard.Core;

namespace BtVolumeGuard;

/// <summary>"About BtVolumeGuard": name, version, author, license and a link to the repository.</summary>
internal sealed class AboutForm : Form
{
    public const string RepositoryUrl = "https://github.com/askrinnik/BtVolumeGuard";

    public AboutForm()
    {
        Assembly assembly = typeof(AboutForm).Assembly;
        AppVersion version = Current;
        string copyright = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "";
        string description = assembly.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description ?? "";

        Text = "About BtVolumeGuard";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        AutoScaleMode = AutoScaleMode.Dpi;
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);

        var link = new LinkLabel { Text = RepositoryUrl, AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        link.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo(RepositoryUrl) { UseShellExecute = true });
        var ok = new Button { Text = "OK", Anchor = AnchorStyles.Right, Margin = new Padding(0, 12, 0, 0) };
        ok.Click += (_, _) => Close();
        AcceptButton = ok;
        CancelButton = ok;

        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Padding = new Padding(16),
        };
        layout.Controls.Add(new Label
        {
            Text = "BtVolumeGuard",
            AutoSize = true,
            Font = new Font(Font.FontFamily, Font.Size * 1.5f, FontStyle.Bold),
            Margin = Padding.Empty,
        });
        layout.Controls.Add(new Label { Text = $"Version {version.Version}", AutoSize = true, Margin = new Padding(0, 4, 0, 0) });
        if (version.Commit is { } commit)
        {
            layout.Controls.Add(new Label { Text = $"Commit {commit}", AutoSize = true, ForeColor = SystemColors.GrayText, Margin = Padding.Empty });
        }

        layout.Controls.Add(new Label { Text = description, AutoSize = true, Margin = new Padding(0, 8, 0, 0) });
        layout.Controls.Add(new Label { Text = copyright, AutoSize = true, Margin = new Padding(0, 8, 0, 0) });
        layout.Controls.Add(link);
        layout.Controls.Add(ok);
        Controls.Add(layout);
    }

    /// <summary>This build's version (in CI the release tag, locally the project's <c>Version</c>).</summary>
    public static AppVersion Current { get; } = AppVersion.Parse(
        typeof(AboutForm).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
}
