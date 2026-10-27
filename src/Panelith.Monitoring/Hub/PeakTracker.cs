using Panelith.Sensors;

namespace Panelith.Monitoring.Hub;

/// <summary>
/// The highest CPU and GPU temperature seen since this tracker was created.
/// Readings only ever rise, so a screen can show a peak after the live graph
/// has moved on.
/// </summary>
internal sealed class PeakTracker
{
    private double? _cpu;
    private double? _gpu;

    /// <summary>The sensor id of the CPU peak reading.</summary>
    public static SensorId CpuId { get; } = SensorId.From("peak", "0", "temperature", "cpu");

    /// <summary>The sensor id of the GPU peak reading.</summary>
    public static SensorId GpuId { get; } = SensorId.From("peak", "0", "temperature", "gpu");

    /// <summary>Raises the stored peaks when the live temperatures are higher.</summary>
    public void Observe(double? cpu, double? gpu)
    {
        if (cpu is { } c && !double.IsNaN(c))
        {
            _cpu = _cpu is { } had && had >= c ? had : c;
        }

        if (gpu is { } g && !double.IsNaN(g))
        {
            _gpu = _gpu is { } had && had >= g ? had : g;
        }
    }

    /// <summary>The peak readings gathered so far. An unseen temperature is omitted.</summary>
    public IReadOnlyList<SensorReading> Readings()
    {
        var readings = new List<SensorReading>(2);
        if (_cpu is { } cpu)
        {
            readings.Add(SensorReading.Numeric(CpuId, "CPU temperature peak", SensorKind.Temperature, cpu, 0d, 120d, "peak"));
        }

        if (_gpu is { } gpu)
        {
            readings.Add(SensorReading.Numeric(GpuId, "GPU temperature peak", SensorKind.Temperature, gpu, 0d, 120d, "peak"));
        }

        return readings;
    }
}
