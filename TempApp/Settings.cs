using System.Diagnostics;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace TempApp;

/// <summary>User settings, stored as JSON in %AppData%\TempApp\settings.json.</summary>
public sealed class Settings
{
    public static readonly int[] AllowedIntervals = { 1000, 2000, 5000 };

    public bool Fahrenheit { get; set; }
    public int IntervalMs { get; set; } = 2000;
    // Off by default: the Arc 130T has no iGPU temperature sensor, so a second icon would just
    // duplicate the CPU package number. The iGPU line is still in the CPU icon's tooltip.
    public bool ShowGpuIcon { get; set; } = false;
    /// <summary>Any installed font family name; falls back to Segoe UI if missing.</summary>
    public string Font { get; set; } = TrayIconRenderer.DefaultFont;

    // Note: "Start with Windows" is deliberately NOT stored here. The scheduled task itself is
    // the source of truth, so the menu can't disagree with what Task Scheduler will actually do.

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TempApp", "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
                if (!AllowedIntervals.Contains(s.IntervalMs)) s.IntervalMs = 2000;
                if (string.IsNullOrWhiteSpace(s.Font)) s.Font = TrayIconRenderer.DefaultFont;
                return s;
            }
        }
        catch (Exception) { /* corrupt file: fall back to defaults */ }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception) { /* non-fatal: settings just won't persist */ }
    }
}

/// <summary>
/// "Start with Windows" via Task Scheduler. A Run registry key would trigger a UAC prompt at every
/// logon because the app requires admin; a task with RunLevel=HighestAvailable starts elevated silently.
/// </summary>
public static class StartupTask
{
    private const string TaskName = "TempApp";

    public static bool IsEnabled() => RunSchtasks($"/Query /TN \"{TaskName}\"") == 0;

    public static bool Enable()
    {
        string user = WindowsIdentity.GetCurrent().Name;
        string exe = Environment.ProcessPath!;
        // XML instead of plain "schtasks /Create /SC ONLOGON" because the defaults it creates are bad
        // for a laptop tray app: "only start on AC power" and "stop after 3 days".
        string xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>Starts the TempApp tray temperature monitor at logon.</Description></RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{SecurityElement.Escape(user)}</UserId>
                  <Delay>PT5S</Delay>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{SecurityElement.Escape(user)}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>7</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec><Command>{SecurityElement.Escape(exe)}</Command></Exec>
              </Actions>
            </Task>
            """;
        string tmp = Path.Combine(Path.GetTempPath(), "TempApp.task.xml");
        try
        {
            File.WriteAllText(tmp, xml, Encoding.Unicode);   // schtasks expects UTF-16 to match the header
            return RunSchtasks($"/Create /TN \"{TaskName}\" /XML \"{tmp}\" /F") == 0;
        }
        finally { File.Delete(tmp); }
    }

    public static bool Disable() => RunSchtasks($"/Delete /TN \"{TaskName}\" /F") == 0;

    private static int RunSchtasks(string args)
    {
        var psi = new ProcessStartInfo("schtasks.exe", args)
        {
            CreateNoWindow = true, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        using var p = Process.Start(psi)!;
        p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit();
        return p.ExitCode;
    }
}
