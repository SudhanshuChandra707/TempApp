using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace TempApp;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        switch (args)
        {
            // Dev helper: render sample icons to a PNG without touching sensors.
            case ["--preview", var file]: Preview(file); return 0;
            // Scriptable equivalents of the "Start with Windows" menu item.
            case ["--install-startup"]: return StartupTask.Enable() ? 0 : 1;
            case ["--remove-startup"]:  return StartupTask.Disable() ? 0 : 1;
        }

        // Single instance. "Local\" scopes the mutex to this logon session.
        // `using` keeps the mutex alive (and owned) until Application.Run returns.
        using var mutex = new Mutex(true, @"Local\TempApp.SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            MessageBox.Show("TempApp is already running in the notification area.", "TempApp",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        Application.Run(new TrayApp());
        return 0;
    }

    private static void Preview(string file)
    {
        float?[] temps = { null, 45, 72, 81, 99, 102 };
        int[] sizes = { 16, 20, 24 };
        const int zoom = 6, cell = 24 * zoom + 8;
        using var sheet = new Bitmap(cell * temps.Length, cell * sizes.Length);
        using var g = Graphics.FromImage(sheet);
        g.Clear(Color.FromArgb(32, 32, 32));          // Windows 11 dark taskbar
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        for (int r = 0; r < sizes.Length; r++)
            for (int c = 0; c < temps.Length; c++)
            {
                using var bmp = TrayIconRenderer.RenderBitmap(
                    TrayIconRenderer.FormatTemp(temps[c]), TrayIconRenderer.ColorFor(temps[c]), sizes[r]);
                g.DrawImage(bmp, c * cell + 4, r * cell + 4, sizes[r] * zoom, sizes[r] * zoom);
            }
        sheet.Save(file, ImageFormat.Png);
    }
}

/// <summary>No window: the app lives entirely in the notification area.</summary>
internal sealed class TrayApp : ApplicationContext
{
    private readonly Settings _settings = Settings.Load();
    private readonly SensorReader _sensors = new();
    private readonly NotifyIcon _cpuTray = new();
    private readonly NotifyIcon _gpuTray = new();
    private readonly System.Windows.Forms.Timer _timer = new();
    private Icon? _cpuIcon, _gpuIcon;   // icons currently shown; freed when replaced

    public TrayApp()
    {
        TrayIconRenderer.SetFont(_settings.Font);

        var menu = BuildMenu();
        _cpuTray.ContextMenuStrip = menu;   // both icons share one menu
        _gpuTray.ContextMenuStrip = menu;

        UpdateAll();
        _cpuTray.Visible = true;            // created first, so it sits left of the GPU icon
        _gpuTray.Visible = _settings.ShowGpuIcon;

        _timer.Interval = _settings.IntervalMs;
        _timer.Tick += (_, _) => UpdateAll();
        _timer.Start();
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        var celsius = new ToolStripMenuItem("Celsius (°C)", null, (_, _) => Change(() => _settings.Fahrenheit = false));
        var fahrenheit = new ToolStripMenuItem("Fahrenheit (°F)", null, (_, _) => Change(() => _settings.Fahrenheit = true));

        var interval = new ToolStripMenuItem("Update interval");
        foreach (int ms in Settings.AllowedIntervals)
            interval.DropDownItems.Add(new ToolStripMenuItem($"{ms / 1000} s", null, (_, _) => Change(() =>
            {
                _settings.IntervalMs = ms;
                _timer.Interval = ms;
            })) { Tag = ms });

        var showGpu = new ToolStripMenuItem("Show separate iGPU icon", null, (_, _) =>
        {
            Change(() => _settings.ShowGpuIcon = !_settings.ShowGpuIcon);  // redraws, incl. GPU icon if now on
            _gpuTray.Visible = _settings.ShowGpuIcon;
        });

        var startup = new ToolStripMenuItem("Start with Windows", null, (_, _) =>
        {
            bool ok = StartupTask.IsEnabled() ? StartupTask.Disable() : StartupTask.Enable();
            if (!ok)
                MessageBox.Show("Could not update the Task Scheduler entry.", "TempApp",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
        });

        menu.Items.AddRange(new ToolStripItem[]
        {
            celsius, fahrenheit, new ToolStripSeparator(),
            interval, showGpu, startup, new ToolStripSeparator(),
            new ToolStripMenuItem("Exit", null, (_, _) => ExitThread()),
        });

        // Refresh check marks every time the menu opens. Startup state is queried from
        // Task Scheduler, so it stays correct even if the task is removed manually.
        menu.Opening += (_, _) =>
        {
            celsius.Checked = !_settings.Fahrenheit;
            fahrenheit.Checked = _settings.Fahrenheit;
            foreach (ToolStripMenuItem item in interval.DropDownItems)
                item.Checked = (int)item.Tag! == _settings.IntervalMs;
            showGpu.Checked = _settings.ShowGpuIcon;
            startup.Checked = StartupTask.IsEnabled();
        };
        return menu;
    }

    /// <summary>Apply a settings change, persist it, and redraw immediately.</summary>
    private void Change(Action apply)
    {
        apply();
        _settings.Save();
        UpdateAll();
    }

    private void UpdateAll()
    {
        SensorSnapshot s;
        try { s = _sensors.Read(); }
        catch (Exception) { s = new SensorSnapshot(null, null, null, null, null); }  // show "--", keep running

        // The Arc 130T exposes no temperature sensor, so the GPU icon shows the CPU package
        // temperature (the iGPU is on the same package). Uses a real GPU sensor if one ever appears.
        float? gpuTemp = s.GpuTemp ?? s.CpuPackageTemp;

        SetIcon(_cpuTray, ref _cpuIcon, s.CpuPackageTemp);
        _cpuTray.Text = BuildTooltip(s, gpuFirst: false);
        if (_settings.ShowGpuIcon)   // skip drawing an icon nobody can see
        {
            SetIcon(_gpuTray, ref _gpuIcon, gpuTemp);
            _gpuTray.Text = BuildTooltip(s, gpuFirst: true);
        }
    }

    private void SetIcon(NotifyIcon tray, ref Icon? current, float? celsius)
    {
        var icon = TrayIconRenderer.Render(
            TrayIconRenderer.FormatTemp(celsius, _settings.Fahrenheit), TrayIconRenderer.ColorFor(celsius));
        tray.Icon = icon;                   // the shell copies the icon...
        TrayIconRenderer.Release(current);  // ...so the previous HICON can be destroyed now
        current = icon;
    }

    /// <summary>Same details on both icons; the hovered icon's own reading goes on the first line.</summary>
    private string BuildTooltip(SensorSnapshot s, bool gpuFirst)
    {
        string T(float? c) => c is not float v ? "n/a"
            : _settings.Fahrenheit ? $"{v * 9 / 5 + 32:0}°F" : $"{v:0}°C";
        static string W(float? v) => v is not float f ? "n/a" : f < 1 ? $"{f:0.00} W" : $"{f:0.0} W";

        string cpu = $"CPU package: {T(s.CpuPackageTemp)} (core max {T(s.CpuCoreMaxTemp)})";
        string gpu = s.GpuTemp is not null
            ? $"iGPU: {T(s.GpuTemp)}"
            : $"iGPU: {T(s.CpuPackageTemp)} (shares CPU package)";
        string power = $"CPU power: {W(s.CpuPackagePower)}\niGPU power: {W(s.GpuPower)}";

        string text = gpuFirst ? $"{gpu}\n{cpu}\n{power}" : $"{cpu}\n{gpu}\n{power}";
        return text.Length <= 127 ? text : text[..127];   // NotifyIcon.Text throws above 127 chars
    }

    protected override void ExitThreadCore()
    {
        _timer.Stop();
        _cpuTray.Visible = false;   // remove immediately instead of leaving ghost icons
        _gpuTray.Visible = false;
        _cpuTray.Dispose();
        _gpuTray.Dispose();
        TrayIconRenderer.Release(_cpuIcon);
        TrayIconRenderer.Release(_gpuIcon);
        _sensors.Dispose();
        base.ExitThreadCore();
    }
}
