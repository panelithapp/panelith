using System.Runtime.Versioning;
using Panelith.Sensors;
using Xunit;

namespace Panelith.Hosting;

public sealed class WindowsSensorSourceTests
{
    [Fact]
    [SupportedOSPlatform("windows10.0.17763.0")]
    public async Task NowPlayingAndAudioReadWithoutLeavingTheMachine()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await using var media = new MediaSessionSensorSource();
        await media.InitializeAsync(TestContext.Current.CancellationToken);
        var playing = await media.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, playing.Count);
        Assert.All(playing, reading => Assert.Equal(SensorKind.Text, reading.Kind));

        await using var audio = new AudioDeviceSensorSource();
        var device = await audio.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, device.Count);
        Assert.Equal(SensorKind.Text, device[0].Kind);
        Assert.Equal(SensorKind.Load, device[1].Kind);
    }
}
