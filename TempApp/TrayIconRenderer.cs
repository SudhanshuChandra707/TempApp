using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace TempApp;

/// <summary>Draws a temperature like "81°" onto a tray-sized icon.</summary>
public static class TrayIconRenderer
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    // Default: Bahnschrift SemiBold SemiCondensed (ships with Windows 10/11). Narrow DIN-style digits
    // fit "81°" into a square icon at a larger height than a regular-width font.
    // GDI truncates family names to 31 chars, hence "SemiConden".
    public const string DefaultFont = "Bahnschrift SemiBold SemiConden";
    private static FontFamily _font = LoadFont(DefaultFont);

    /// <summary>Change the font family; falls back to Segoe UI if not installed.</summary>
    public static void SetFont(string name) => _font = LoadFont(name);

    public static FontFamily LoadFont(string name)
    {
        try { return new FontFamily(name); }
        catch (ArgumentException) { return new FontFamily("Segoe UI"); }
    }

    /// <summary>"81°", or "102" for 3 digits (no room for the degree sign), or "--" if no reading.</summary>
    public static string FormatTemp(float? celsius, bool fahrenheit = false)
    {
        if (celsius is not float c) return "--";
        int v = (int)MathF.Round(fahrenheit ? c * 9f / 5f + 32f : c);
        return v >= 100 ? v.ToString() : $"{v}°";
    }

    /// <summary>Colour by Celsius: green &lt; 60, yellow 60-80, red &gt; 80. Shade adapts to taskbar theme.</summary>
    public static Color ColorFor(float? celsius)
    {
        bool light = IsLightTaskbar();
        if (celsius is not float c) return light ? Color.FromArgb(90, 90, 90) : Color.FromArgb(200, 200, 200);
        if (c < 60)  return light ? Color.FromArgb(21, 128, 61) : Color.FromArgb(74, 222, 128);
        if (c <= 80) return light ? Color.FromArgb(180, 83, 9)  : Color.FromArgb(250, 204, 21);
        return light ? Color.FromArgb(220, 38, 38) : Color.FromArgb(248, 113, 113);
    }

    /// <summary>
    /// Returns an Icon that owns a native HICON. The caller must free it with <see cref="Release"/>,
    /// otherwise a GDI handle leaks on every update.
    /// </summary>
    public static Icon Render(string text, Color color)
    {
        using var bmp = RenderBitmap(text, color, SystemInformation.SmallIconSize.Width);
        return Icon.FromHandle(bmp.GetHicon());
    }

    /// <summary>Icon.FromHandle does not take ownership, so Dispose alone won't free the HICON.</summary>
    public static void Release(Icon? icon)
    {
        if (icon == null) return;
        IntPtr h = icon.Handle;
        icon.Dispose();
        DestroyIcon(h);
    }

    public static Bitmap RenderBitmap(string text, Color color, int size, FontFamily? font = null)
    {
        font ??= _font;
        // Bahnschrift named instances are already the right weight; only add Bold to plain families.
        var style = font.Name.Contains("Bold") || font.Name.Contains("Semib") ? FontStyle.Regular : FontStyle.Bold;
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        using var path = new GraphicsPath();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        // Draw the text as a vector path, then scale its *ink* bounds to fill the icon.
        // This gets far bigger digits than DrawString, whose layout box includes padding.
        path.AddString(text, font, (int)style, 100f, PointF.Empty, StringFormat.GenericTypographic);
        RectangleF b = path.GetBounds();
        float s = Math.Min(size / b.Width, size / b.Height);
        // "81°" is about twice as wide as tall, so allow some vertical stretch to use the square.
        float sy = Math.Min(size / b.Height, s * 1.5f);

        using var m = new Matrix();   // operations are prepended: read bottom-up
        m.Translate((size - b.Width * s) / 2f, (size - b.Height * sy) / 2f);
        m.Scale(s, sy);
        m.Translate(-b.X, -b.Y);
        path.Transform(m);

        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
        return bmp;
    }

    /// <summary>Windows 11 taskbar colour: SystemUsesLightTheme = 1 means light taskbar.</summary>
    private static bool IsLightTaskbar() =>
        Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                          "SystemUsesLightTheme", 0) is int v && v == 1;
}
