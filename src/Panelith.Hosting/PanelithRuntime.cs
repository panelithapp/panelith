using Microsoft.Extensions.Logging;
using Panelith.Devices.Drivers;
using Panelith.Devices.Profiles;
using Panelith.Devices.Transports;
using Panelith.Engine;
using Panelith.Engine.Configuration;
using Panelith.Monitoring.Binding;
using Panelith.Monitoring.Hub;
using Panelith.Monitoring.Providers;
using Panelith.Monitoring.Providers.Hwinfo;
using Panelith.Rendering;
using Panelith.Sensors;

namespace Panelith.Hosting;

/// <summary>The process-wide pieces the command line and the desktop app share.</summary>
public sealed class PanelithRuntime : IAsyncDisposable
{
    private PanelithRuntime(
        AppPaths paths,
        PanelithOptions options,
        ConfigurationStore store,
        PanelProfileRegistry profiles,
        PanelDiscoveryService discovery,
        SensorHub sensors,
        SkiaImageCodec codec,
        ILoggerFactory loggers)
    {
        Paths = paths;
        Options = options;
        Store = store;
        Profiles = profiles;
        Discovery = discovery;
        Sensors = sensors;
        Codec = codec;
        Loggers = loggers;
        Bindings = new SensorBindingContext(sensors);
    }

    /// <summary>Where configuration lives.</summary>
    public AppPaths Paths { get; }

    /// <summary>Loaded configuration.</summary>
    public PanelithOptions Options { get; }

    /// <summary>The store that loaded <see cref="Options"/>.</summary>
    public ConfigurationStore Store { get; }

    /// <summary>Panel catalogue.</summary>
    public PanelProfileRegistry Profiles { get; }

    /// <summary>Discovery and open.</summary>
    public PanelDiscoveryService Discovery { get; }

    /// <summary>Sensor poller.</summary>
    public SensorHub Sensors { get; }

    /// <summary>Bindings the renderer reads.</summary>
    public SensorBindingContext Bindings { get; }

    /// <summary>PNG and JPEG codec.</summary>
    public SkiaImageCodec Codec { get; }

    /// <summary>Log sink.</summary>
    public ILoggerFactory Loggers { get; }

    /// <summary>
    /// Builds a runtime. Sensor polling starts immediately so the first frame
    /// already has values.
    /// </summary>
    /// <param name="demoSensors">Use simulated sensors instead of the machine's.</param>
    /// <param name="loggers">Log sink. The runtime takes ownership and disposes it.</param>
    /// <param name="virtualPanel">Report the on-screen virtual panel during discovery.</param>
    /// <param name="virtualOptions">How that virtual panel behaves. Ignored when <paramref name="virtualPanel"/> is false.</param>
    /// <param name="paths">Configuration root. Defaults to <see cref="AppPaths.Default"/>.</param>
    /// <param name="cancellationToken">Cancels startup.</param>
    public static async Task<PanelithRuntime> StartAsync(
        bool demoSensors,
        ILoggerFactory loggers,
        bool virtualPanel = false,
        VirtualPanelOptions? virtualOptions = null,
        AppPaths? paths = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(loggers);
        if (paths is null)
        {
            paths = AppPaths.Default;
            AppPaths.MigrateLegacy(paths.Root);
        }

        var store = new ConfigurationStore(paths, loggers.CreateLogger<ConfigurationStore>());
        var options = store.Load();

        var profiles = PanelProfileRegistry.Load(paths.ProfilesDirectory, loggers.CreateLogger<PanelProfileRegistry>());
        var codec = new SkiaImageCodec();
        var context = new PanelDriverContext(
            profiles,
            codec,
            new LibUsbDeviceAccess(loggers.CreateLogger<LibUsbDeviceAccess>()),
            new HidSharpDeviceAccess(loggers.CreateLogger<HidSharpDeviceAccess>()),
            new SerialPortEnumerator(loggers.CreateLogger<SerialPortEnumerator>()),
            loggers);

        var virtualSettings = virtualPanel
            ? virtualOptions ?? new VirtualPanelOptions { Enabled = true }
            : new VirtualPanelOptions { Enabled = false };
        if (virtualPanel)
        {
            virtualSettings = virtualSettings with { Enabled = true };
        }

        var discovery = PanelDiscoveryService.CreateDefault(context, virtualSettings);

        var resolver = new Monitoring.Aliasing.SensorAliasResolver();
        var sensorLog = loggers.CreateLogger<SensorHub>();
        if (options.Sensors.AliasOverrides is { } overrides)
        {
            foreach (var (alias, target) in overrides)
            {
                if (!SensorId.TryParse(target, out var id))
                {
                    sensorLog.LogWarning("Ignoring alias override {Alias}: \"{Target}\" is not a sensor id.", alias, target);
                    continue;
                }

                try
                {
                    resolver.SetOverride(alias, id);
                }
                catch (ArgumentException ex)
                {
                    sensorLog.LogWarning("Ignoring alias override {Alias}: {Message}", alias, ex.Message);
                }
            }
        }

        var sensors = new SensorHub(
            CreateSources(demoSensors, options.Sensors),
            resolver,
            new SensorHubOptions
            {
                PollInterval = TimeSpan.FromSeconds(Math.Clamp(options.Sensors.PollIntervalSeconds, 0.2, 60)),
                HistoryCapacity = TimeSpan.FromSeconds(Math.Clamp(options.Sensors.HistorySeconds, 1, 86400)),
            },
            sensorLog);

        await sensors.StartAsync(cancellationToken).ConfigureAwait(false);

        return new PanelithRuntime(paths, options, store, profiles, discovery, sensors, codec, loggers);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await Sensors.DisposeAsync().ConfigureAwait(false);
        if (Loggers is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private static IEnumerable<ISensorSource> CreateSources(bool demoSensors, SensorOptions sensorOptions)
    {
        if (demoSensors)
        {
            if (sensorOptions.IsSourceEnabled("synthetic"))
            {
                yield return new SyntheticSensorSource(seed: 7);
            }

            yield break;
        }

        if (OperatingSystem.IsLinux())
        {
            if (sensorOptions.IsSourceEnabled("linux-hwmon"))
            {
                yield return new LinuxHwmonSensorSource();
            }

            if (sensorOptions.IsSourceEnabled("linux-system"))
            {
                yield return new LinuxSystemSensorSource();
            }
        }

        if (OperatingSystem.IsWindows())
        {
            if (sensorOptions.IsSourceEnabled("lhm"))
            {
                yield return new LibreHardwareMonitorFacade();
            }

            if (sensorOptions.IsSourceEnabled("hwinfo"))
            {
                yield return new HwinfoSensorSource();
            }

            if (sensorOptions.IsSourceEnabled("rtss"))
            {
                yield return new RtssSensorSource();
            }
        }

        if (OperatingSystem.IsWindows())
        {
            foreach (var source in PlatformSensors.Create(sensorOptions))
            {
                yield return source;
            }
        }

        if (sensorOptions.IsSourceEnabled("system"))
        {
            yield return new SystemInfoSensorSource();
        }

        if (sensorOptions.IsSourceEnabled("process"))
        {
            yield return new ProcessSensorSource();
        }
    }
}
