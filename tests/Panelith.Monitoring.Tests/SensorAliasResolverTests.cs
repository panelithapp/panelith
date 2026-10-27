using Panelith.Monitoring.Aliasing;
using Panelith.Monitoring.Providers;
using Panelith.Sensors;
using Xunit;

namespace Panelith.Monitoring;

public sealed class SensorAliasResolverTests
{
    [Fact]
    public void CpuTempPrefersPackageOverCoreOverAcpi()
    {
        var readings = new[]
        {
            SensorReading.Numeric(SensorId.From("acpi", "0", "temperature", "temp1"), "acpitz", SensorKind.Temperature, 40),
            SensorReading.Numeric(SensorId.From("cpu", "0", "temperature", "core_0"), "Core 0", SensorKind.Temperature, 55),
            SensorReading.Numeric(SensorId.From("cpu", "0", "temperature", "package"), "Package id 0", SensorKind.Temperature, 60),
        };

        var resolver = new SensorAliasResolver();
        var map = resolver.Resolve(readings);
        Assert.Equal("cpu/0/temperature/package", map[WellKnownSensors.CpuTemperature].Value);
    }

    [Fact]
    public void CpuTempFallsBackToAcpiWhenNoCpuSensor()
    {
        var readings = new[]
        {
            SensorReading.Numeric(SensorId.From("acpi", "0", "temperature", "temp1"), "acpitz temp1", SensorKind.Temperature, 40),
        };

        var map = new SensorAliasResolver().Resolve(readings);
        Assert.Equal("acpi/0/temperature/temp1", map[WellKnownSensors.CpuTemperature].Value);
    }

    [Fact]
    public void GpuTempPrefersDiscreteOverIntegrated()
    {
        var readings = new[]
        {
            SensorReading.Numeric(SensorId.From("gpu", "0", "temperature", "edge"), "Intel UHD Graphics", SensorKind.Temperature, 50),
            SensorReading.Numeric(SensorId.From("gpu", "1", "temperature", "edge"), "NVIDIA GeForce RTX 4070", SensorKind.Temperature, 65),
        };

        var map = new SensorAliasResolver().Resolve(readings);
        Assert.Equal("gpu/1/temperature/edge", map[WellKnownSensors.GpuTemperature].Value);
    }

    [Fact]
    public void NetworkAliasesPreferAggregateAndExcludeVirtual()
    {
        var readings = new[]
        {
            SensorReading.Numeric(SensorId.From("network", "lo", "throughput", "down"), "lo Download", SensorKind.Throughput, 999),
            SensorReading.Numeric(SensorId.From("network", "docker0", "throughput", "down"), "docker0 Download", SensorKind.Throughput, 888),
            SensorReading.Numeric(SensorId.From("network", "eth0", "throughput", "down"), "eth0 Download", SensorKind.Throughput, 100),
            SensorReading.Numeric(SensorId.From("network", "0", "throughput", "down"), "Network Download", SensorKind.Throughput, 100),
            SensorReading.Numeric(SensorId.From("network", "0", "throughput", "up"), "Network Upload", SensorKind.Throughput, 50),
        };

        var map = new SensorAliasResolver().Resolve(readings);
        Assert.Equal("network/0/throughput/down", map[WellKnownSensors.NetworkDown].Value);
        Assert.Equal("network/0/throughput/up", map[WellKnownSensors.NetworkUp].Value);
    }

    [Fact]
    public void ResolutionIsDeterministicAcrossRepeatedCalls()
    {
        var readings = new[]
        {
            SensorReading.Numeric(SensorId.From("cpu", "0", "temperature", "core_1"), "Core 1", SensorKind.Temperature, 50),
            SensorReading.Numeric(SensorId.From("cpu", "0", "temperature", "core_0"), "Core 0", SensorKind.Temperature, 50),
        };

        var resolver = new SensorAliasResolver();
        var first = resolver.Resolve(readings)[WellKnownSensors.CpuTemperature];
        var second = resolver.Resolve(readings)[WellKnownSensors.CpuTemperature];
        Assert.Equal(first, second);
        // Tie on score → ordinal id wins.
        Assert.Equal("cpu/0/temperature/core_0", first.Value);
    }

    [Fact]
    public void CoreAliasesMatchTheirOwnCoreOnly()
    {
        var readings = new[]
        {
            SensorReading.Numeric(SensorId.From("cpu", "0", "load", "cpu_core_1"), "CPU Core #1", SensorKind.Load, 10),
            SensorReading.Numeric(SensorId.From("cpu", "0", "load", "cpu_core_10"), "CPU Core #10", SensorKind.Load, 20),
            SensorReading.Numeric(SensorId.From("cpu", "0", "load", "cpu_total"), "CPU Total", SensorKind.Load, 30),
            SensorReading.Numeric(SensorId.From("cpu", "1", "load", "cpu_core_2"), "Second socket core", SensorKind.Load, 40),
        };

        var map = new SensorAliasResolver().Resolve(readings);
        Assert.Equal("cpu/0/load/cpu_core_1", map[WellKnownSensors.CpuCore(1)].Value);
        Assert.Equal("cpu/0/load/cpu_core_10", map[WellKnownSensors.CpuCore(10)].Value);
        Assert.False(map.ContainsKey(WellKnownSensors.CpuCore(2)));
        Assert.Equal(10, WellKnownSensors.CoreNumber(WellKnownSensors.CpuCore(10)));
        Assert.Null(WellKnownSensors.CoreNumber(WellKnownSensors.CpuLoad));
    }

    [Fact]
    public void DiskRatesPreferTheAllDiskTotal()
    {
        var readings = new[]
        {
            SensorReading.Numeric(SensorId.From("disk", "0", "throughput", "read_rate"), "Read Rate", SensorKind.Throughput, 1),
            SensorReading.Numeric(SensorId.From("disk", "1", "throughput", "write_rate"), "Write Rate", SensorKind.Throughput, 2),
            SensorReading.Numeric(SensorId.From("disk", "all", "throughput", "read"), "Disk read", SensorKind.Throughput, 3),
            SensorReading.Numeric(SensorId.From("disk", "all", "throughput", "write"), "Disk write", SensorKind.Throughput, 4),
        };

        var map = new SensorAliasResolver().Resolve(readings);
        Assert.Equal("disk/all/throughput/read", map[WellKnownSensors.DiskRead].Value);
        Assert.Equal("disk/all/throughput/write", map[WellKnownSensors.DiskWrite].Value);
    }

    [Fact]
    public void GpuVideoPrefersTheVideoEngine()
    {
        var readings = new[]
        {
            SensorReading.Numeric(SensorId.From("gpu", "0", "load", "gpu_core"), "NVIDIA GeForce RTX 4070 GPU Core", SensorKind.Load, 50),
            SensorReading.Numeric(SensorId.From("gpu", "0", "load", "gpu_video_decode"), "NVIDIA GeForce RTX 4070 Video Decode", SensorKind.Load, 5),
            SensorReading.Numeric(SensorId.From("gpu", "0", "load", "gpu_video_engine"), "NVIDIA GeForce RTX 4070 GPU Video Engine", SensorKind.Load, 12),
        };

        var map = new SensorAliasResolver().Resolve(readings);
        Assert.Equal("gpu/0/load/gpu_video_engine", map[WellKnownSensors.GpuVideo].Value);
    }

    [Fact]
    public void GameAliasesBindToRivaTunerSensors()
    {
        var readings = new[]
        {
            SensorReading.Numeric(SensorId.From("panel", "0", "framerate", "fps"), "Panel FPS", SensorKind.FrameRate, 10),
            SensorReading.Numeric(RtssSensorSource.FpsId, "Game FPS", SensorKind.FrameRate, 144),
            SensorReading.Numeric(RtssSensorSource.Low1Id, "Game 1% low", SensorKind.FrameRate, 97),
            SensorReading.Numeric(RtssSensorSource.FrameTimeId, "Frame time", SensorKind.Unknown, 6.9),
            SensorReading.Textual(RtssSensorSource.NameId, "Game", "doom"),
        };

        var map = new SensorAliasResolver().Resolve(readings);
        Assert.Equal(RtssSensorSource.FpsId, map[WellKnownSensors.GameFps]);
        Assert.Equal(RtssSensorSource.Low1Id, map[WellKnownSensors.GameLow1]);
        Assert.Equal(RtssSensorSource.FrameTimeId, map[WellKnownSensors.GameFrameTime]);
        Assert.Equal(RtssSensorSource.NameId, map[WellKnownSensors.GameName]);
    }

    [Fact]
    public void UserOverrideWins()
    {
        var readings = new[]
        {
            SensorReading.Numeric(SensorId.From("cpu", "0", "temperature", "package"), "Package", SensorKind.Temperature, 60),
            SensorReading.Numeric(SensorId.From("cpu", "0", "temperature", "core_0"), "Core 0", SensorKind.Temperature, 55),
        };

        var resolver = new SensorAliasResolver();
        resolver.SetOverride(WellKnownSensors.CpuTemperature, SensorId.From("cpu", "0", "temperature", "core_0"));
        var map = resolver.Resolve(readings);
        Assert.Equal("cpu/0/temperature/core_0", map[WellKnownSensors.CpuTemperature].Value);
    }

    [Fact]
    public void PeaksBatteryAddressAndMediaResolveToTheirOwnIds()
    {
        var readings = new[]
        {
            SensorReading.Numeric(SensorId.From("cpu", "0", "temperature", "package"), "Package", SensorKind.Temperature, 60),
            SensorReading.Numeric(SensorId.From("peak", "0", "temperature", "cpu"), "CPU temperature peak", SensorKind.Temperature, 70),
            SensorReading.Numeric(SensorId.From("peak", "0", "temperature", "gpu"), "GPU temperature peak", SensorKind.Temperature, 80),
            SensorReading.Numeric(SensorId.From("battery", "0", "load", "charge"), "Charge", SensorKind.Load, 40),
            SensorReading.Numeric(SensorId.From("battery", "0", "load", "capacity"), "Capacity", SensorKind.Load, 90),
            SensorReading.Textual(SensorId.From("network", "0", "address", "ipv4"), "IPv4 address", "192.0.2.10"),
            SensorReading.Textual(SensorId.From("audio", "0", "name", "device"), "Playback device", "Speakers"),
            SensorReading.Numeric(SensorId.From("audio", "0", "load", "volume"), "Volume", SensorKind.Load, 30),
            SensorReading.Textual(SensorId.From("media", "0", "text", "title"), "Now playing", "Track"),
            SensorReading.Textual(SensorId.From("media", "0", "text", "artist"), "Artist", "Artist"),
            SensorReading.Textual(SensorId.From("media", "0", "text", "album"), "Album", "Album"),
        };

        var map = new SensorAliasResolver().Resolve(readings);

        Assert.Equal("cpu/0/temperature/package", map[WellKnownSensors.CpuTemperature].Value);
        Assert.Equal("peak/0/temperature/cpu", map[WellKnownSensors.CpuTemperaturePeak].Value);
        Assert.Equal("peak/0/temperature/gpu", map[WellKnownSensors.GpuTemperaturePeak].Value);
        Assert.Equal("battery/0/load/capacity", map[WellKnownSensors.Battery].Value);
        Assert.Equal("network/0/address/ipv4", map[WellKnownSensors.NetworkAddress].Value);
        Assert.Equal("audio/0/name/device", map[WellKnownSensors.AudioDevice].Value);
        Assert.Equal("audio/0/load/volume", map[WellKnownSensors.AudioVolume].Value);
        Assert.Equal("media/0/text/title", map[WellKnownSensors.MediaTitle].Value);
        Assert.Equal("media/0/text/artist", map[WellKnownSensors.MediaArtist].Value);
        Assert.Equal("media/0/text/album", map[WellKnownSensors.MediaAlbum].Value);
    }
}
