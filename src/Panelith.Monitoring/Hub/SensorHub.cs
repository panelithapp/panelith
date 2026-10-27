using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Panelith.Monitoring.Aliasing;
using Panelith.Expressions;
using Panelith.Sensors;

namespace Panelith.Monitoring.Hub;

/// <summary>
/// Composes every <see cref="ISensorSource"/> into a single polled
/// <see cref="SensorSnapshot"/>, applies <see cref="SensorAliasResolver"/>,
/// and retains bounded history for graph widgets and
/// <c>avgover</c>/<c>minover</c>/<c>maxover</c>.
/// </summary>
/// <remarks>
/// One slow or broken provider must never stall the others: each read is
/// budgeted by <see cref="SensorHubOptions.ProviderTimeout"/>, and a source
/// that throws repeatedly is quarantined with a logged recovery path.
/// </remarks>
public sealed class SensorHub : IAsyncDisposable
{
    private readonly IReadOnlyList<ISensorSource> _sources;
    private readonly SensorAliasResolver _resolver;
    private readonly SensorHubOptions _options;
    private readonly ILogger _logger;
    private readonly SensorHistoryStore _history;
    private readonly ConcurrentDictionary<string, ProviderState> _states = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _runCts = new();

    private Task? _loop;
    private readonly PeakTracker _peaks = new();
    private SensorSnapshot _current = SensorSnapshot.Empty;
    private IReadOnlyDictionary<string, SensorId> _aliases =
        new Dictionary<string, SensorId>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates a hub over <paramref name="sources"/>. Only sources reporting
    /// <see cref="ISensorSource.IsAvailable"/> are polled.
    /// </summary>
    public SensorHub(
        IEnumerable<ISensorSource> sources,
        SensorAliasResolver? resolver = null,
        SensorHubOptions? options = null,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(sources);
        _sources = sources.ToList();
        _resolver = resolver ?? new SensorAliasResolver();
        _options = options ?? new SensorHubOptions();
        _logger = logger ?? NullLogger.Instance;
        _history = new SensorHistoryStore(_options.HistorySamplesPerSensor, _options.HistoryCapacity);

        foreach (var source in _sources)
        {
            _states[source.Id] = new ProviderState();
        }
    }

    /// <summary>The most recent merged snapshot. Safe to read from any thread.</summary>
    public SensorSnapshot Current => Volatile.Read(ref _current!);

    /// <summary>Alias → concrete id mapping from the latest poll.</summary>
    public IReadOnlyDictionary<string, SensorId> Aliases => Volatile.Read(ref _aliases!);

    /// <summary>The resolver the hub applies each poll.</summary>
    public SensorAliasResolver Resolver => _resolver;

    /// <summary>Raised after every successful poll (including empty ones).</summary>
    public event Action<SensorSnapshot>? SnapshotUpdated;

    /// <summary>Whether a source is currently quarantined.</summary>
    public bool IsQuarantined(string sourceId) =>
        _states.TryGetValue(sourceId, out var state) && state.QuarantinedUntil > DateTimeOffset.UtcNow;

    /// <summary>
    /// History of a sensor reference (alias or raw id), newest last, matching
    /// <see cref="Panelith.Expressions.IBindingContext.ResolveHistory"/>.
    /// </summary>
    public IReadOnlyList<double> ResolveHistory(string reference, TimeSpan window) =>
        _history.Query(reference, window, DateTimeOffset.UtcNow);

    /// <summary>Like <see cref="ResolveHistory"/>, with each sample's time.</summary>
    public IReadOnlyList<HistorySample> ResolveHistorySamples(string reference, TimeSpan window) =>
        _history.QuerySamples(reference, window, DateTimeOffset.UtcNow);

    /// <summary>Initialises available sources and starts the background poll loop.</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        foreach (var source in _sources)
        {
            if (!source.IsAvailable)
            {
                continue;
            }

            try
            {
                await source.InitializeAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Sensor source {SourceId} failed to initialise; it will be skipped.", source.Id);
                if (_states.TryGetValue(source.Id, out var state))
                {
                    Quarantine(state, source.Id, ex.Message);
                }
            }
        }

        // Seed one snapshot before returning so callers never see Empty when
        // hardware is already readable.
        await PollOnceAsync(cancellationToken).ConfigureAwait(false);
        _loop = Task.Run(() => RunLoopAsync(_runCts.Token), CancellationToken.None);
    }

    /// <summary>Stops the poll loop and disposes every source.</summary>
    public async ValueTask DisposeAsync()
    {
        await _runCts.CancelAsync().ConfigureAwait(false);
        if (_loop is not null)
        {
            try
            {
                await _loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        foreach (var source in _sources)
        {
            try
            {
                await source.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Sensor source {SourceId} threw during dispose.", source.Id);
            }
        }

        _runCts.Dispose();
    }

    /// <summary>Runs a single poll — public for tests that drive the hub synchronously.</summary>
    public async Task PollOnceAsync(CancellationToken cancellationToken = default)
    {
        var timestamp = DateTimeOffset.UtcNow;
        var merged = new List<SensorReading>();
        var deduplicator = new SensorIdDeduplicator();

        foreach (var source in _sources)
        {
            if (!source.IsAvailable)
            {
                continue;
            }

            if (!_states.TryGetValue(source.Id, out var state))
            {
                continue;
            }

            if (state.QuarantinedUntil > timestamp)
            {
                continue;
            }

            // Leaving quarantine: allow one attempt and reset the failure counter
            // only on success so a still-broken source re-quarantines quickly.
            if (state.QuarantinedUntil != DateTimeOffset.MinValue && state.QuarantinedUntil <= timestamp)
            {
                _logger.LogInformation("Sensor source {SourceId} leaving quarantine; attempting recovery.", source.Id);
                state.QuarantinedUntil = DateTimeOffset.MinValue;
            }

            IReadOnlyList<SensorReading> readings;
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(_options.ProviderTimeout);
                readings = await source.ReadAsync(timeoutCts.Token).ConfigureAwait(false);
                state.ConsecutiveFailures = 0;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                state.ConsecutiveFailures++;
                _logger.LogWarning(
                    "Sensor source {SourceId} timed out after {Timeout}.",
                    source.Id,
                    _options.ProviderTimeout);
                if (state.ConsecutiveFailures >= _options.QuarantineThreshold)
                {
                    Quarantine(state, source.Id, "repeated timeouts");
                }

                continue;
            }
            catch (Exception ex)
            {
                state.ConsecutiveFailures++;
                _logger.LogWarning(ex, "Sensor source {SourceId} threw during read.", source.Id);
                if (state.ConsecutiveFailures >= _options.QuarantineThreshold)
                {
                    Quarantine(state, source.Id, ex.Message);
                }

                continue;
            }

            foreach (var reading in readings)
            {
                // Namespace by source only when two providers emit the same id;
                // the deduplicator handles genuine duplicates within one poll.
                var unique = deduplicator.Unique(reading.Id);
                merged.Add(reading with { Id = unique, SourceId = reading.SourceId ?? source.Id });
            }
        }

        var snapshot = SensorSnapshot.Create(timestamp, merged);
        var aliases = _resolver.Resolve(snapshot);
        _peaks.Observe(Number(snapshot, aliases, WellKnownSensors.CpuTemperature), Number(snapshot, aliases, WellKnownSensors.GpuTemperature));
        var peaks = _peaks.Readings();
        if (peaks.Count > 0)
        {
            merged.AddRange(peaks);
            snapshot = SensorSnapshot.Create(timestamp, merged);
            aliases = _resolver.Resolve(snapshot);
        }

        _history.RecordSnapshot(snapshot, aliases, timestamp);

        Volatile.Write(ref _aliases!, aliases);
        Volatile.Write(ref _current!, snapshot);
        SnapshotUpdated?.Invoke(snapshot);
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_options.PollInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    await PollOnceAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Sensor hub poll failed.");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static double? Number(SensorSnapshot snapshot, IReadOnlyDictionary<string, SensorId> aliases, string alias) =>
        aliases.TryGetValue(alias, out var id) && snapshot.TryGet(id, out var reading) ? reading.Value : null;

    private void Quarantine(ProviderState state, string sourceId, string reason)
    {
        state.QuarantinedUntil = DateTimeOffset.UtcNow + _options.QuarantineDuration;
        state.ConsecutiveFailures = 0;
        _logger.LogWarning(
            "Sensor source {SourceId} quarantined for {Duration}: {Reason}",
            sourceId,
            _options.QuarantineDuration,
            reason);
    }

    private sealed class ProviderState
    {
        public int ConsecutiveFailures;
        public DateTimeOffset QuarantinedUntil = DateTimeOffset.MinValue;
    }
}
