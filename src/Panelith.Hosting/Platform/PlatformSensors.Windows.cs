using System.Runtime.Versioning;
using Panelith.Engine.Configuration;
using Panelith.Sensors;

namespace Panelith.Hosting;

/// <summary>Sensor sources that exist only on Windows.</summary>
internal static class PlatformSensors
{
    public static IEnumerable<ISensorSource> Create(SensorOptions options)
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763) && options.IsSourceEnabled("media"))
        {
            yield return new MediaSessionSensorSource();
        }

        if (options.IsSourceEnabled("audio"))
        {
            yield return new AudioDeviceSensorSource();
        }
    }
}
