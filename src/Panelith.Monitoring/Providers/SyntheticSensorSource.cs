using Panelith.Sensors;

namespace Panelith.Monitoring.Providers;

/// <summary>
/// Deterministic fake sensors for every <see cref="WellKnownSensors"/> alias.
/// Powers the theme editor and <c>panelith preview</c> when no hardware is
/// attached, and is the stable substrate the rest of the test suite builds on.
/// </summary>
public sealed class SyntheticSensorSource : ISensorSource
{
    private readonly int _seed;
    private readonly Dictionary<string, double> _phases;
    private long _tick;

    /// <summary>
    /// Creates a source. Pass a fixed <paramref name="seed"/> for reproducible
    /// tests; omit it for a time-derived seed in interactive previews.
    /// </summary>
    public SyntheticSensorSource(int? seed = null)
    {
        _seed = seed ?? Environment.TickCount;
        var rng = new Random(_seed);
        _phases = WellKnownSensors.All.Keys.ToDictionary(
            static alias => alias,
            _ => rng.NextDouble() * Math.PI * 2d,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The seed driving this source's waveforms.</summary>
    public int Seed => _seed;

    /// <inheritdoc />
    public string Id => "synthetic";

    /// <inheritdoc />
    public string DisplayName => "Synthetic";

    /// <inheritdoc />
    public bool IsAvailable => true;

    /// <inheritdoc />
    public ValueTask InitializeAsync(CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<SensorReading>> ReadAsync(CancellationToken cancellationToken = default)
    {
        var tick = Interlocked.Increment(ref _tick);
        var readings = new List<SensorReading>(WellKnownSensors.All.Count);

        foreach (var (alias, kind) in WellKnownSensors.All)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var phase = _phases[alias];
            var id = SensorId.From("synthetic", alias.TrimStart(WellKnownSensors.AliasPrefix).Replace('.', '/'));

            if (kind == SensorKind.Text)
            {
                var text = alias switch
                {
                    WellKnownSensors.CpuName => "Synthetic CPU",
                    WellKnownSensors.GpuName => "Synthetic GPU",
                    WellKnownSensors.HostName => "synthetic-host",
                    WellKnownSensors.Time => SystemInfoSensorSource.FormatTime(DateTime.Now),
                    WellKnownSensors.Date => SystemInfoSensorSource.FormatDate(DateTime.Now),
                    WellKnownSensors.OperatingSystem => "Synthetic OS",
                    WellKnownSensors.GameName => "SyntheticGame",
                    WellKnownSensors.NetworkAddress => "192.0.2.10",
                    WellKnownSensors.AudioDevice => "Speakers",
                    WellKnownSensors.MediaTitle => "Synthetic track",
                    WellKnownSensors.MediaArtist => "Synthetic artist",
                    WellKnownSensors.MediaAlbum => "Synthetic album",
                    _ => alias,
                };
                readings.Add(SensorReading.Textual(id, alias, text, Id));
                continue;
            }

            var value = Wave(kind, tick, phase);
            var (min, max) = SensorKindDefaults.DefaultRangeFor(kind) ?? kind switch
            {
                SensorKind.Data when alias.Contains("total", StringComparison.Ordinal) => (0d, value),
                SensorKind.Data => (0d, SuggestDataCeiling(alias)),
                SensorKind.Throughput => (0d, 125_000_000d),
                SensorKind.Power => (0d, 300d),
                SensorKind.Clock => (0d, 5000d),
                SensorKind.Fan => (0d, 3000d),
                SensorKind.Duration => (0d, value),
                _ => (0d, 100d),
            };

            readings.Add(SensorReading.Numeric(id, alias, kind, value, min, max, Id));
        }

        return ValueTask.FromResult<IReadOnlyList<SensorReading>>(readings);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>
    /// The raw synthetic id that backs <paramref name="alias"/> for a given seed
    /// stream — exposed so tests and the alias resolver can bind to it.
    /// </summary>
    public static SensorId IdForAlias(string alias) =>
        SensorId.From("synthetic", alias.TrimStart(WellKnownSensors.AliasPrefix).Replace('.', '/'));

    private static double Wave(SensorKind kind, long tick, double phase)
    {
        var t = tick * 0.15d + phase;
        var sine = (Math.Sin(t) + 1d) / 2d; // 0..1
        var slow = (Math.Sin(t * 0.27d + phase) + 1d) / 2d;

        return kind switch
        {
            SensorKind.Temperature => 35d + (sine * 45d),
            SensorKind.Load or SensorKind.Control => sine * 100d,
            SensorKind.Clock => 800d + (sine * 4200d),
            SensorKind.Power => 15d + (sine * 180d),
            SensorKind.Voltage => 0.7d + (sine * 0.5d),
            SensorKind.Current => 1d + (sine * 40d),
            SensorKind.Fan => 400d + (sine * 1600d),
            SensorKind.Data => SuggestDataCeiling("used") * (0.2d + (slow * 0.6d)),
            SensorKind.Throughput => sine * 80_000_000d,
            SensorKind.Duration => tick * 1.5d + 3600d,
            SensorKind.FrameRate => 30d + (sine * 90d),
            SensorKind.Count => Math.Round(sine * 200d),
            _ => sine * 100d,
        };
    }

    private static double SuggestDataCeiling(string alias) =>
        alias.Contains("vram", StringComparison.OrdinalIgnoreCase) ? 12L * 1024 * 1024 * 1024
        : alias.Contains("disk", StringComparison.OrdinalIgnoreCase) ? 2000L * 1024 * 1024 * 1024
        : 32L * 1024 * 1024 * 1024;
}
