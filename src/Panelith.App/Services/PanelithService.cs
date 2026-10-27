using Microsoft.Extensions.Logging;
using Panelith.Engine;
using Panelith.Engine.Configuration;
using Panelith.Hosting;
using Panelith.Sensors;

namespace Panelith.App.Services;

/// <summary>
/// The running engine behind the window: sensors, the panel host and the
/// switchable renderer, plus the configuration they were started with.
/// </summary>
public sealed class PanelithService : IAsyncDisposable
{
    private readonly ILogger _logger;

    private PanelithService(PanelithRuntime runtime, AppLog log)
    {
        Runtime = runtime;
        Log = log;
        _logger = log.CreateLogger("Panelith.App");
        Options = runtime.Options;
        Library = new ThemeLibrary(runtime.Paths);
        Renderers = new LiveThemeRendererFactory(Library, log.CreateLogger<LiveThemeRendererFactory>());
        Renderers.Show(Options.Defaults.Theme);
        Host = new PanelHost(runtime.Discovery, Renderers, runtime.Bindings, Options, log);
    }

    /// <summary>Sensors, discovery and configuration.</summary>
    public PanelithRuntime Runtime { get; }

    /// <summary>The activity log.</summary>
    public AppLog Log { get; }

    /// <summary>Drives every attached panel.</summary>
    public PanelHost Host { get; }

    /// <summary>Builds renderers and switches their screen on the fly.</summary>
    public LiveThemeRendererFactory Renderers { get; }

    /// <summary>Every screen that can be shown.</summary>
    public ThemeLibrary Library { get; }

    /// <summary>The configuration currently applied.</summary>
    public PanelithOptions Options { get; private set; }

    /// <summary>The screen on the panel.</summary>
    public string ActiveScreen => Renderers.ActiveTheme ?? Options.Defaults.Theme;

    /// <summary>The size of picture the panel shows, or the Trofeo's when no panel has connected yet.</summary>
    public (int Width, int Height) PanelSize
    {
        get
        {
            var statuses = Host.Statuses;
            return statuses.FirstOrDefault(s => s.State == PanelRuntimeState.Running)?.FrameSize
                ?? statuses.FirstOrDefault(s => s.FrameSize is not null)?.FrameSize
                ?? (1920, 480);
        }
    }

    /// <summary>Starts sensors and begins driving panels.</summary>
    public static async Task<PanelithService> StartAsync(AppLog log, bool demoSensors, CancellationToken cancellationToken = default)
    {
        var runtime = await PanelithRuntime.StartAsync(demoSensors, new NonDisposingLoggerFactory(log), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var service = new PanelithService(runtime, log);
        await service.Host.StartAsync(cancellationToken).ConfigureAwait(false);
        return service;
    }

    /// <summary>
    /// Puts a screen on every panel immediately, and remembers it for next time.
    /// </summary>
    /// <remarks>
    /// The host is deliberately not told: it would reopen the USB connection
    /// for what is only a change of picture.
    /// </remarks>
    public void Show(string screenId)
    {
        Renderers.Show(screenId);
        Persist(Options with { Defaults = Options.Defaults with { Theme = screenId } });
    }

    /// <summary>
    /// Pins a theme name to one raw sensor, or clears the pin when
    /// <paramref name="sensorId"/> is null so the automatic choice comes back.
    /// </summary>
    public void SetAliasOverride(string alias, string? sensorId)
    {
        var map = new Dictionary<string, string>(
            Options.Sensors.AliasOverrides ?? new Dictionary<string, string>(),
            StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(sensorId))
        {
            map.Remove(alias);
            Runtime.Sensors.Resolver.ClearOverride(alias);
        }
        else if (SensorId.TryParse(sensorId, out var id))
        {
            map[alias] = id.Value;
            Runtime.Sensors.Resolver.SetOverride(alias, id);
        }
        else
        {
            return;
        }

        Persist(Options with { Sensors = Options.Sensors with { AliasOverrides = map.Count == 0 ? null : map } });
    }

    /// <summary>Changes panel settings that need the connection reopened, such as frame rate.</summary>
    public async Task UpdateDefaultsAsync(Func<PanelDefaults, PanelDefaults> change)
    {
        var updated = Options with { Defaults = change(Options.Defaults) with { Theme = ActiveScreen } };
        Persist(updated);
        await Host.ApplyAsync(WithOverrides(updated)).ConfigureAwait(false);
    }

    /// <summary>The frame rate a profile asked for, or null to use the saved one.</summary>
    public int? FrameRateOverride { get; private set; }

    /// <summary>
    /// Runs the panel at a profile's frame rate without changing the saved
    /// setting. Reopens the connection only when the rate actually changes.
    /// </summary>
    public async Task SetFrameRateOverrideAsync(int? framesPerSecond)
    {
        var before = FrameRateOverride ?? Options.Defaults.FramesPerSecond;
        FrameRateOverride = framesPerSecond;
        var after = FrameRateOverride ?? Options.Defaults.FramesPerSecond;
        if (before != after)
        {
            await Host.ApplyAsync(WithOverrides(Options with { Defaults = Options.Defaults with { Theme = ActiveScreen } })).ConfigureAwait(false);
        }
    }

    private PanelithOptions WithOverrides(PanelithOptions options) =>
        FrameRateOverride is { } fps
            ? options with { Defaults = options.Defaults with { FramesPerSecond = fps } }
            : options;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await Host.DisposeAsync().ConfigureAwait(false);
        await Runtime.DisposeAsync().ConfigureAwait(false);
    }

    private void Persist(PanelithOptions options)
    {
        Options = options;
        try
        {
            Runtime.Store.Save(options);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("Settings could not be saved: {Message}", ex.Message);
        }
    }

    /// <summary>Lets the runtime "own" a logger the app keeps using after the runtime is gone.</summary>
    private sealed class NonDisposingLoggerFactory(ILoggerFactory inner) : ILoggerFactory
    {
        public ILogger CreateLogger(string categoryName) => inner.CreateLogger(categoryName);

        public void AddProvider(ILoggerProvider provider) => inner.AddProvider(provider);

        public void Dispose()
        {
        }
    }
}
