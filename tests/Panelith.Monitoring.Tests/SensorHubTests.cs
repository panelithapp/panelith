using Panelith.Monitoring.Aliasing;
using Panelith.Monitoring.Hub;
using Panelith.Sensors;
using Xunit;

namespace Panelith.Monitoring;

public sealed class SensorHubTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ThrowingProviderIsQuarantinedWhileOthersKeepReporting()
    {
        var good = new StubSource("good", [
            SensorReading.Numeric(SensorId.From("cpu", "0", "load", "total"), "CPU", SensorKind.Load, 42),
        ]);
        var bad = new ThrowingSource("bad");

        await using var hub = new SensorHub(
            [good, bad],
            options: new SensorHubOptions
            {
                QuarantineThreshold = 2,
                QuarantineDuration = TimeSpan.FromMinutes(5),
                ProviderTimeout = TimeSpan.FromMilliseconds(200),
            });

        await hub.PollOnceAsync(Ct);
        await hub.PollOnceAsync(Ct);

        Assert.True(hub.IsQuarantined("bad"));
        Assert.False(hub.IsQuarantined("good"));
        Assert.True(hub.Current.TryGet(SensorId.From("cpu", "0", "load", "total"), out var reading));
        Assert.Equal(42d, reading.Value);

        // Further polls must not call the quarantined source.
        var callsBefore = bad.Calls;
        await hub.PollOnceAsync(Ct);
        Assert.Equal(callsBefore, bad.Calls);
    }

    [Fact]
    public async Task HangingProviderHitsTimeout()
    {
        var good = new StubSource("good", [
            SensorReading.Numeric(SensorId.From("mem", "0", "load", "used"), "Mem", SensorKind.Load, 10),
        ]);
        var hang = new HangingSource("hang");

        await using var hub = new SensorHub(
            [good, hang],
            options: new SensorHubOptions
            {
                ProviderTimeout = TimeSpan.FromMilliseconds(50),
                QuarantineThreshold = 1,
                QuarantineDuration = TimeSpan.FromMinutes(5),
            });

        await hub.PollOnceAsync(Ct);
        Assert.True(hub.IsQuarantined("hang"));
        Assert.True(hub.Current.Contains(SensorId.From("mem", "0", "load", "used")));
    }

    [Fact]
    public async Task HistoryWindowReturnsRecordedSamples()
    {
        var source = new StubSource("s", [
            SensorReading.Numeric(SensorId.From("cpu", "0", "temperature", "package"), "Package", SensorKind.Temperature, 50),
        ]);
        var resolver = new SensorAliasResolver();

        await using var hub = new SensorHub(
            [source],
            resolver,
            new SensorHubOptions { HistorySamplesPerSensor = 64 });

        source.SetValue(50);
        await hub.PollOnceAsync(Ct);
        source.SetValue(60);
        await hub.PollOnceAsync(Ct);
        source.SetValue(70);
        await hub.PollOnceAsync(Ct);

        var history = hub.ResolveHistory(WellKnownSensors.CpuTemperature, TimeSpan.FromMinutes(1));
        Assert.Equal(3, history.Count);
        Assert.Equal([50d, 60d, 70d], history);
    }

    [Fact]
    public async Task SnapshotSwapIsConsistent()
    {
        var source = new StubSource("s", [
            SensorReading.Numeric(SensorId.From("cpu", "0", "load", "total"), "CPU", SensorKind.Load, 1),
            SensorReading.Numeric(SensorId.From("memory", "0", "data", "used"), "Mem", SensorKind.Data, 2),
        ]);

        await using var hub = new SensorHub([source]);
        await hub.PollOnceAsync(Ct);
        var snap = hub.Current;
        Assert.Equal(2, snap.Count);
        Assert.Equal(snap.Timestamp, hub.Current.Timestamp);
        Assert.Same(snap, hub.Current);
    }

    [Fact]
    public async Task TemperaturePeakRisesAndDoesNotFall()
    {
        var package = SensorId.From("cpu", "0", "temperature", "package");
        var source = new StubSource("cpu", [
            SensorReading.Numeric(package, "Package", SensorKind.Temperature, 50, 0d, 120d, "cpu"),
        ]);
        var resolver = new SensorAliasResolver();
        await using var hub = new SensorHub([source], resolver);

        source.SetValue(50);
        await hub.PollOnceAsync(Ct);
        source.SetValue(72);
        await hub.PollOnceAsync(Ct);
        source.SetValue(40);
        await hub.PollOnceAsync(Ct);

        Assert.True(hub.Aliases.TryGetValue(WellKnownSensors.CpuTemperaturePeak, out var peakId));
        Assert.True(hub.Current.TryGet(peakId, out var peak));
        Assert.Equal(72d, peak.Value);
        Assert.Equal(package, hub.Aliases[WellKnownSensors.CpuTemperature]);
    }

    private sealed class StubSource : ISensorSource
    {
        private SensorReading[] _readings;

        public StubSource(string id, SensorReading[] readings)
        {
            Id = id;
            _readings = readings;
        }

        public string Id { get; }
        public string DisplayName => Id;
        public bool IsAvailable => true;

        public void SetValue(double value) =>
            _readings = [.. _readings.Select(r => r with { Value = value })];

        public ValueTask InitializeAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<IReadOnlyList<SensorReading>> ReadAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<SensorReading>>(_readings);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ThrowingSource : ISensorSource
    {
        public ThrowingSource(string id)
        {
            Id = id;
            DisplayName = id;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public bool IsAvailable => true;
        public int Calls { get; private set; }

        public ValueTask InitializeAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask<IReadOnlyList<SensorReading>> ReadAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            throw new InvalidOperationException("boom");
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class HangingSource : ISensorSource
    {
        public HangingSource(string id)
        {
            Id = id;
            DisplayName = id;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public bool IsAvailable => true;

        public ValueTask InitializeAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public async ValueTask<IReadOnlyList<SensorReading>> ReadAsync(CancellationToken cancellationToken = default)
        {
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            return [];
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
