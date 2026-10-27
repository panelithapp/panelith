using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Panelith.Hosting.Profiles;
using Panelith.Monitoring.Providers;
using Panelith.Sensors;

namespace Panelith.App.Services.Activity;

/// <summary>Raised when the active profile, or its settings, change.</summary>
/// <param name="profile">The active profile.</param>
/// <param name="switched">True when a different profile became active, false when the same one was edited.</param>
public sealed class ProfileActivatedEventArgs(Profile profile, bool switched) : EventArgs
{
    public Profile Profile { get; } = profile;

    public bool Switched { get; } = switched;
}

/// <summary>
/// Watches what the PC is doing every two seconds and keeps the right profile
/// on the panel: its screens and rotation, its dimming and its frame rate.
/// </summary>
public sealed partial class ProfileController : ObservableObject, IDisposable
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan LibraryRefresh = TimeSpan.FromMinutes(10);

    private readonly PanelithService _service;
    private readonly ProfileStore _store;
    private readonly ILogger _logger;
    private readonly ProcessWatcher _processes = new();
    private readonly ProfileResolver _resolver = new();
    private readonly DispatcherTimer _timer;
    private ActivitySnapshot? _lastSnapshot;
    private DateTime _librariesFoundAt = DateTime.MinValue;
    private bool _ticking;

    public ProfileController(PanelithService service, ProfileStore store, ProfileSet set)
    {
        _service = service;
        _store = store;
        _logger = service.Log.CreateLogger("Panelith.App.Profiles");
        Set = set;
        _timer = new DispatcherTimer(TickInterval, DispatcherPriority.Background, (_, _) => _ = TickAsync());
    }

    /// <summary>Raised on the UI thread when the active profile or its settings change.</summary>
    public event EventHandler<ProfileActivatedEventArgs>? ActiveProfileChanged;

    /// <summary>Raised on the UI thread when any profile is added, removed or edited.</summary>
    public event EventHandler? SetChanged;

    /// <summary>Every profile.</summary>
    public ProfileSet Set { get; private set; }

    /// <summary>The current decision, or null before the first tick.</summary>
    [ObservableProperty]
    public partial ProfileDecision? Decision { get; private set; }

    /// <summary>The active profile.</summary>
    public Profile? Active => Decision?.Profile;

    /// <summary>Why the active profile is active, for the user.</summary>
    public string Reason => Decision?.Reason ?? string.Empty;

    /// <summary>Whether a profile is pinned by hand.</summary>
    public bool IsPinned => Set.Pinned is not null;

    /// <summary>Whether profiles switch by themselves.</summary>
    public bool Automatic => Set.Automatic;

    /// <summary>Starts watching. The first decision is made straight away.</summary>
    public void Start()
    {
        Evaluate(_lastSnapshot ?? new ActivitySnapshot());
        _timer.Start();
        _ = TickAsync();
    }

    /// <summary>Replaces the whole set, saves it and re-evaluates.</summary>
    public void Update(ProfileSet next)
    {
        ArgumentNullException.ThrowIfNull(next);
        Set = next;
        try
        {
            _store.Save(next);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("Profiles could not be saved: {Message}", ex.Message);
        }

        SetChanged?.Invoke(this, EventArgs.Empty);
        OnPropertyChanged(nameof(IsPinned));
        OnPropertyChanged(nameof(Automatic));
        Evaluate(_lastSnapshot ?? new ActivitySnapshot());
    }

    /// <summary>Replaces one profile, matched by id.</summary>
    public void UpdateProfile(Profile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Update(Set with
        {
            Profiles = [.. Set.Profiles.Select(p => string.Equals(p.Id, profile.Id, StringComparison.OrdinalIgnoreCase) ? profile : p)],
        });
    }

    /// <summary>Holds a profile until unpinned, or returns to automatic switching with null.</summary>
    public void Pin(string? profileId)
    {
        if (profileId is not null && Set.Find(profileId) is null)
        {
            return;
        }

        Update(Set with { Pinned = profileId });
    }

    /// <summary>Pins a profile, or unpins it when it is already the pinned one.</summary>
    public void TogglePin(string profileId) =>
        Pin(string.Equals(Set.Pinned, profileId, StringComparison.OrdinalIgnoreCase) ? null : profileId);

    /// <summary>Pins the profile after the active one, skipping disabled ones.</summary>
    public void PinNext()
    {
        var candidates = Set.Profiles.Where(p => p.Enabled).ToList();
        if (candidates.Count == 0)
        {
            return;
        }

        var index = Active is null ? -1 : candidates.FindIndex(p => p.Id == Active.Id);
        Pin(candidates[(index + 1) % candidates.Count].Id);
    }

    /// <summary>Turns automatic switching on or off.</summary>
    public void SetAutomatic(bool automatic) => Update(Set with { Automatic = automatic });

    /// <inheritdoc />
    public void Dispose() => _timer.Stop();

    private async Task TickAsync()
    {
        if (_ticking)
        {
            return;
        }

        _ticking = true;
        try
        {
            var refreshLibraries = DateTime.UtcNow - _librariesFoundAt > LibraryRefresh;
            var gpuLoad = SensorNumber(WellKnownSensors.GpuLoad);
            var rtssProcess = RtssProcessId();
            var sensors = ReadSensors();
            var (snapshot, libraries) = await Task.Run(() => (Snapshot(gpuLoad, rtssProcess, sensors), refreshLibraries ? GameLibraryLocator.Locate() : null));
            if (libraries is not null)
            {
                _librariesFoundAt = DateTime.UtcNow;
                _resolver.GameLibraries = libraries;
                _logger.LogDebug("Game libraries: {Folders}", string.Join("; ", libraries));
            }

            _lastSnapshot = snapshot;
            Evaluate(snapshot);
        }
        catch (Exception ex)
        {
            // Nothing waits on this tick, and stopping for good would leave
            // profiles stuck on whichever one was last chosen.
            _logger.LogWarning("Could not check running programs: {Message}", ex.Message);
        }
        finally
        {
            _ticking = false;
        }
    }

    private ActivitySnapshot Snapshot(double? gpuLoad, int? rtssProcess, IReadOnlyDictionary<string, double> sensors)
    {
        var processes = _processes.Snapshot();
        var (foregroundId, fullscreen) = ForegroundProbe.Read();
        return new ActivitySnapshot
        {
            Now = DateTime.Now,
            Processes = processes,
            Foreground = foregroundId is { } id ? _processes.Find(id) : null,
            ForegroundFullscreen = fullscreen,
            IdleSeconds = IdleProbe.IdleSeconds(),
            Locked = IdleProbe.IsLocked(),
            RtssProcessId = rtssProcess,
            GpuLoad = gpuLoad,
            Sensors = sensors,
        };
    }

    private Dictionary<string, double> ReadSensors()
    {
        var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var hub = _service.Runtime.Sensors;
        var snapshot = hub.Current;
        foreach (var (alias, id) in hub.Aliases)
        {
            if (snapshot.TryGet(id, out var reading) && reading.Value is { } value && !double.IsNaN(value))
            {
                values[alias] = value;
            }
        }

        return values;
    }

    private void Evaluate(ActivitySnapshot snapshot)
    {
        var previous = Decision;
        var decision = _resolver.Resolve(Set, snapshot);
        if (decision is null)
        {
            return;
        }

        if (decision == previous)
        {
            return;
        }

        Decision = decision;
        OnPropertyChanged(nameof(Active));
        OnPropertyChanged(nameof(Reason));
        _service.Renderers.Dim = decision.Dim;

        var switched = previous is null || !string.Equals(previous.Profile.Id, decision.Profile.Id, StringComparison.OrdinalIgnoreCase);
        if (switched)
        {
            _logger.LogInformation("Profile: {Profile} ({Reason})", decision.Profile.Name, decision.Reason);
        }

        if (switched || previous!.Profile != decision.Profile)
        {
            ActiveProfileChanged?.Invoke(this, new ProfileActivatedEventArgs(decision.Profile, switched));
            _ = ApplyFrameRateAsync(decision.Profile.FramesPerSecond);
        }
    }

    private async Task ApplyFrameRateAsync(int? framesPerSecond)
    {
        try
        {
            await _service.SetFrameRateOverrideAsync(framesPerSecond is { } fps ? Math.Clamp(fps, 1, 60) : null);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException)
        {
            _logger.LogWarning("The profile's frame rate could not be applied: {Message}", ex.Message);
        }
    }

    private double? SensorNumber(string alias) =>
        _service.Runtime.Sensors.Aliases.TryGetValue(alias, out var id) && _service.Runtime.Sensors.Current.TryGet(id, out var reading)
            ? reading.Value
            : null;

    private int? RtssProcessId() =>
        _service.Runtime.Sensors.Current.TryGet(RtssSensorSource.ProcessIdId, out var reading) && reading.Value is { } pid
            ? (int)pid
            : null;
}
