using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

using Microsoft.Win32;

namespace BtVolumeGuard;

internal enum GlyphStyle
{
    /// <summary>White glyph, for a dark taskbar.</summary>
    Light,

    /// <summary>Near-black glyph, for a light taskbar.</summary>
    Dark,

    /// <summary>Near-black glyph with a white outline, for any background (the executable's icon).</summary>
    DarkWithHalo,
}

/// <summary>
/// Draws the tray icon at the exact tray size: a headset glyph from the Windows icon font
/// (white on a dark taskbar, black on a light one) with a shield badge: green while the guard
/// is on, grey while it is off.
/// </summary>
internal static class TrayIconRenderer
{
    private const char HeadsetGlyph = '';

    // Segoe Fluent Icons ships with Windows 11, Segoe MDL2 Assets with Windows 10; same code point.
    private static readonly string[] s_iconFonts = ["Segoe Fluent Icons", "Segoe MDL2 Assets"];

    private static readonly Color s_shieldColor = Color.FromArgb(0x16, 0xA3, 0x4A);
    private static readonly Color s_disabledShieldColor = Color.FromArgb(0x8A, 0x8A, 0x8A);

    /// <summary>Whether the taskbar uses the light theme (per-user setting, read only).</summary>
    public static bool IsTaskbarLight()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
    }

    /// <summary>Creates the tray icon; the caller owns and disposes it.</summary>
    public static Icon Create(int size, bool lightTaskbar, bool guardEnabled)
    {
        using Bitmap bitmap = Render(size, lightTaskbar ? GlyphStyle.Dark : GlyphStyle.Light, guardEnabled);
        IntPtr handle = bitmap.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(handle);
            return (Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    /// <summary>
    /// Draws the icon into a new bitmap. Also used by tools/make-icon.ps1 to build the executable's
    /// .ico with <see cref="GlyphStyle.DarkWithHalo"/>, which stays visible on light and dark backgrounds.
    /// </summary>
    public static Bitmap Render(int size, GlyphStyle style, bool guardEnabled)
    {
        var bitmap = new Bitmap(size, size);
        using Graphics g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        Draw(g, size, style, guardEnabled);
        return bitmap;
    }

    private static void Draw(Graphics g, float s, GlyphStyle style, bool guardEnabled)
    {
        string? fontName = s_iconFonts.FirstOrDefault(IsInstalled);
        if (fontName is not null)
        {
            var bounds = new RectangleF(-s * 0.04f, s * 0.02f, s * 0.9f, s * 0.9f);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            Color glyphColor = style == GlyphStyle.Light ? Color.White : Color.FromArgb(0x1B, 0x1B, 0x1B);
            using var glyphBrush = new SolidBrush(glyphColor);

            if (style == GlyphStyle.DarkWithHalo)
            {
                // Slightly smaller and inset, so the outline does not touch the edges.
                var inset = new RectangleF(s * 0.03f, s * 0.07f, s * 0.84f, s * 0.84f);
                using var family = new FontFamily(fontName);
                using var glyph = new GraphicsPath();
                glyph.AddString(HeadsetGlyph.ToString(), family, (int)FontStyle.Regular, s * 0.84f, inset, format);
                using var halo = new Pen(Color.FromArgb(230, Color.White), Math.Max(1.5f, s * 0.09f)) { LineJoin = LineJoin.Round };
                g.DrawPath(halo, glyph);
                g.FillPath(glyphBrush, glyph);
            }
            else
            {
                using var font = new Font(fontName, s * 0.9f, GraphicsUnit.Pixel);
                g.DrawString(HeadsetGlyph.ToString(), font, glyphBrush, bounds, format);
            }
        }

        float w = s * 0.52f;
        float h = w * 1.1f;
        float x = s - w;
        float y = s - h;
        float gap = s * 0.06f;

        // Erase a thin outline around the badge so it stays separate from the glyph.
        using (GraphicsPath cutout = Shield(x - gap, y - gap, w + (2 * gap), h + (2 * gap)))
        {
            CompositingMode previous = g.CompositingMode;
            g.CompositingMode = CompositingMode.SourceCopy;
            using var clear = new SolidBrush(Color.Transparent);
            g.FillPath(clear, cutout);
            g.CompositingMode = previous;
        }

        using GraphicsPath shield = Shield(x, y, w, h);
        using var shieldBrush = new SolidBrush(guardEnabled ? s_shieldColor : s_disabledShieldColor);
        g.FillPath(shieldBrush, shield);
    }

    private static GraphicsPath Shield(float x, float y, float w, float h)
    {
        var path = new GraphicsPath();
        path.AddLine(x + (w / 2), y, x + w, y + (h * 0.18f));
        path.AddBezier(x + w, y + (h * 0.18f), x + w, y + (h * 0.65f), x + (w * 0.75f), y + (h * 0.85f), x + (w / 2), y + h);
        path.AddBezier(x + (w / 2), y + h, x + (w * 0.25f), y + (h * 0.85f), x, y + (h * 0.65f), x, y + (h * 0.18f));
        path.CloseFigure();
        return path;
    }

    private static bool IsInstalled(string fontName)
    {
        using var fonts = new InstalledFontCollection();
        return fonts.Families.Any(f => f.Name.Equals(fontName, StringComparison.OrdinalIgnoreCase));
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}
