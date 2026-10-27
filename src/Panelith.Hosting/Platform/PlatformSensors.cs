using Panelith.Engine.Configuration;
using Panelith.Sensors;

namespace Panelith.Hosting;

/// <summary>No Windows-only sources on this operating system.</summary>
internal static class PlatformSensors
{
    public static IEnumerable<ISensorSource> Create(SensorOptions options)
    {
        _ = options;
        yield break;
    }
}
