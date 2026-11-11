using LibreHardwareMonitor.Hardware;

namespace TempApp;

/// <summary>One reading of everything the UI shows. Null = sensor missing or no value yet.</summary>
public sealed record SensorSnapshot(
    float? CpuPackageTemp,
    float? CpuCoreMaxTemp,
    float? CpuPackagePower,
    float? GpuTemp,       // null on the Arc 130T: LHM exposes no iGPU temperature sensor
    float? GpuPower);

/// <summary>Wraps LibreHardwareMonitor. Sensor objects are looked up once and reused.</summary>
public sealed class SensorReader : IDisposable
{
    private readonly Computer _computer;
    private readonly IHardware? _cpu;
    private readonly IHardware? _gpu;
    private readonly ISensor? _cpuPkgTemp, _cpuCoreMax, _cpuPkgPower, _gpuTemp, _gpuPower;

    public SensorReader()
    {
        // Motherboard is not enabled: on this laptop it exposes no sensors (see Stage 1 dump),
        // and leaving it off keeps each update cheaper.
        _computer = new Computer { IsCpuEnabled = true, IsGpuEnabled = true };
        _computer.Open();

        _cpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
        _gpu = _computer.Hardware.FirstOrDefault(h =>
            h.HardwareType is HardwareType.GpuIntel or HardwareType.GpuAmd or HardwareType.GpuNvidia);

        // Some sensors (notably GPU ones) are only created on the first Update().
        _cpu?.Update();
        _gpu?.Update();

        _cpuPkgTemp  = Find(_cpu, SensorType.Temperature, "CPU Package");
        _cpuCoreMax  = Find(_cpu, SensorType.Temperature, "Core Max");
        _cpuPkgPower = Find(_cpu, SensorType.Power, "CPU Package");
        _gpuPower    = Find(_gpu, SensorType.Power, "GPU Power");
        // Take any GPU temperature sensor if a future LHM/driver version adds one.
        _gpuTemp     = _gpu?.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature);
    }

    public SensorSnapshot Read()
    {
        _cpu?.Update();
        _gpu?.Update();
        return new SensorSnapshot(
            _cpuPkgTemp?.Value, _cpuCoreMax?.Value, _cpuPkgPower?.Value,
            _gpuTemp?.Value, _gpuPower?.Value);
    }

    private static ISensor? Find(IHardware? hw, SensorType type, string name) =>
        hw?.Sensors.FirstOrDefault(s => s.SensorType == type && s.Name == name);

    public void Dispose() => _computer.Close();
}
