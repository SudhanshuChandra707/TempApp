// Stage 1 diagnostic: dump every sensor LibreHardwareMonitor can see.
// Usage: SensorDump.exe [outputFile]   (run elevated - ring0/MSR access needs admin)
using System.Security.Principal;
using System.Text;
using LibreHardwareMonitor.Hardware;

var outPath = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "sensors.txt");
var sb = new StringBuilder();
void Log(string s) { Console.WriteLine(s); sb.AppendLine(s); }

bool isAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
Log($"Admin: {isAdmin}   OS: {Environment.OSVersion}   Time: {DateTime.Now}");
Log(new string('-', 90));

var computer = new Computer
{
    IsCpuEnabled = true,
    IsGpuEnabled = true,
    IsMotherboardEnabled = true,
};

try
{
    computer.Open();

    // Some sensors only report a value after the second Update(), so poll twice.
    var visitor = new UpdateVisitor();
    computer.Accept(visitor);
    Thread.Sleep(1000);
    computer.Accept(visitor);

    foreach (var hw in computer.Hardware)
        DumpHardware(hw, 0);
}
catch (Exception ex)
{
    Log("ERROR: " + ex);
}
finally
{
    computer.Close();
}

File.WriteAllText(outPath, sb.ToString());
Console.WriteLine($"\nWritten to {outPath}");

void DumpHardware(IHardware hw, int depth)
{
    string pad = new(' ', depth * 2);
    Log($"{pad}[{hw.HardwareType}] {hw.Name}   ({hw.Identifier})");
    foreach (var s in hw.Sensors.OrderBy(s => s.SensorType).ThenBy(s => s.Index))
    {
        string val = s.Value.HasValue ? s.Value.Value.ToString("0.##") : "null";
        Log($"{pad}    {s.SensorType,-12} {s.Name,-35} = {val}   ({s.Identifier})");
    }
    foreach (var sub in hw.SubHardware)
        DumpHardware(sub, depth + 1);
}

// Standard LHM pattern: walk the tree and call Update() on every hardware node.
class UpdateVisitor : IVisitor
{
    public void VisitComputer(IComputer computer) => computer.Traverse(this);
    public void VisitHardware(IHardware hardware)
    {
        hardware.Update();
        foreach (var sub in hardware.SubHardware) sub.Accept(this);
    }
    public void VisitSensor(ISensor sensor) { }
    public void VisitParameter(IParameter parameter) { }
}
