using System.Text.RegularExpressions;
using Panelith.Expressions;

namespace Panelith.Hosting.Profiles;

/// <summary>Why a profile is active.</summary>
public enum ProfileTrigger
{
    /// <summary>Nothing else applied.</summary>
    Default,

    /// <summary>The user chose it.</summary>
    Pinned,

    /// <summary>A listed program is running.</summary>
    App,

    /// <summary>A game was detected.</summary>
    Game,

    /// <summary>No input for a while.</summary>
    Idle,

    /// <summary>Windows is locked.</summary>
    Locked,

    /// <summary>Its schedule covers the current time.</summary>
    Schedule,

    /// <summary>A sensor expression is true, such as a temperature that has gone too high.</summary>
    Sensor,
}

/// <summary>The profile to use and why.</summary>
/// <param name="Profile">The chosen profile.</param>
/// <param name="Trigger">What chose it.</param>
/// <param name="Reason">A short explanation for the user, such as "Cyberpunk2077.exe".</param>
/// <param name="Dim">How much to darken the panel.</param>
public sealed record ProfileDecision(Profile Profile, ProfileTrigger Trigger, string Reason, double Dim);

/// <summary>
/// Picks the active profile from what the PC is doing, holding each decision
/// briefly so the panel does not flick between profiles.
/// </summary>
/// <remarks>
/// <para>
/// Order of precedence: a pinned profile, then a sensor expression, then programs
/// and games, then idle or lock, then schedules, then the default. Within each
/// kind, earlier profiles in the list win, so a warning profile has to sit above
/// Gaming to interrupt a game. Programs beat idle so a paused game stays on its profile.
/// </para>
/// <para>
/// When an idle or lock profile wins while a dimmed schedule (Night) also
/// matches, the schedule's dimming still applies, so stepping away at night
/// does not light the panel up.
/// </para>
/// </remarks>
public sealed class ProfileResolver
{
    private static readonly HashSet<string> NeverGames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Panelith", "explorer", "dwm", "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost", "TextInputHost",
        "ApplicationFrameHost", "SystemSettings", "LockApp", "Taskmgr", "msedge", "chrome", "firefox", "opera", "brave", "vivaldi",
        "Discord", "Spotify", "Teams", "ms-teams", "slack", "Code", "Cursor", "devenv", "WindowsTerminal", "steam", "steamwebhelper",
        "EpicGamesLauncher", "EpicWebHelper", "EADesktop", "EABackgroundService", "upc", "UbisoftConnect", "GalaxyClient", "Battle.net",
        "RiotClientServices", "RiotClientUx", "XboxPcApp", "GameBar", "obs64", "vlc", "mpc-hc64", "PotPlayerMini64", "Photos", "Video.UI",
        "UnityCrashHandler64", "UnityCrashHandler32", "CrashReportClient", "EasyAntiCheat", "EasyAntiCheat_EOS", "BEService",
        "nvcontainer", "NVIDIA Share", "NVIDIA Overlay", "RTSS", "MSIAfterburner", "EncoderServer", "RTSSHooksLoader64",
    };

    private ProfileDecision? _committed;
    private (string ProfileId, DateTime Since)? _pending;
    private (ProfileDecision Decision, DateTime At)? _lastApp;
    private DateTime? _fullscreenBusySince;
    private int? _fullscreenBusyProcess;

    /// <summary>Creates a resolver.</summary>
    /// <param name="gameLibraries">Folders games are installed into, from <see cref="GameLibraryLocator"/>.</param>
    public ProfileResolver(IReadOnlyList<string>? gameLibraries = null) => GameLibraries = gameLibraries ?? [];

    /// <summary>Folders whose programs count as games.</summary>
    public IReadOnlyList<string> GameLibraries { get; set; }

    /// <summary>How long a new decision must hold before the profile changes.</summary>
    public TimeSpan SwitchDelay { get; set; } = TimeSpan.FromSeconds(4);

    /// <summary>How long a program's profile stays after the program closes.</summary>
    public TimeSpan AppLinger { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>How long a fullscreen window must keep the GPU busy to count as a game.</summary>
    public TimeSpan FullscreenDuration { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>The GPU load a fullscreen window must cause to count as a game.</summary>
    public double FullscreenGpuLoad { get; set; } = 50;

    /// <summary>The decision currently in force.</summary>
    public ProfileDecision? Current => _committed;

    /// <summary>Forgets the held decision, so the next call switches at once.</summary>
    public void Reset()
    {
        _committed = null;
        _pending = null;
    }

    /// <summary>Works out the profile for this moment.</summary>
    public ProfileDecision? Resolve(ProfileSet set, ActivitySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(snapshot);

        var candidate = Evaluate(set, snapshot);
        if (candidate is null)
        {
            _committed = null;
            return null;
        }

        var immediate = _committed is null
            || candidate.Trigger == ProfileTrigger.Pinned
            || _committed.Trigger == ProfileTrigger.Pinned
            || set.Find(_committed.Profile.Id) is null;

        if (immediate || string.Equals(candidate.Profile.Id, _committed!.Profile.Id, StringComparison.OrdinalIgnoreCase))
        {
            _pending = null;
            _committed = candidate;
            return _committed;
        }

        if (_pending is not { } pending || !string.Equals(pending.ProfileId, candidate.Profile.Id, StringComparison.OrdinalIgnoreCase))
        {
            pending = (candidate.Profile.Id, snapshot.Now);
            _pending = pending;
        }

        if (snapshot.Now - pending.Since >= SwitchDelay)
        {
            _pending = null;
            _committed = candidate;
        }

        return _committed;
    }

    /// <summary>Whether a program matches an app pattern.</summary>
    public static bool Matches(string pattern, ProcessInfo process)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(process);

        var trimmed = pattern.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (trimmed.Contains('\\', StringComparison.Ordinal) || trimmed.Contains('/', StringComparison.Ordinal))
        {
            return process.Path is not null && string.Equals(Path.GetFullPath(trimmed), process.Path, StringComparison.OrdinalIgnoreCase);
        }

        if (trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^4];
        }

        if (!trimmed.Contains('*', StringComparison.Ordinal))
        {
            return string.Equals(trimmed, process.Name, StringComparison.OrdinalIgnoreCase);
        }

        var regex = "^" + Regex.Escape(trimmed).Replace(@"\*", ".*", StringComparison.Ordinal) + "$";
        return Regex.IsMatch(process.Name, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
    }

    /// <summary>The program that looks like a game, if any.</summary>
    public ProcessInfo? DetectGame(ActivitySnapshot snapshot, bool allowFullscreen)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.RtssProcessId is { } rtssId
            && snapshot.Processes.FirstOrDefault(p => p.Id == rtssId) is { } hooked
            && !NeverGames.Contains(hooked.Name))
        {
            return hooked;
        }

        if (snapshot.Foreground is { } foreground && !NeverGames.Contains(foreground.Name) && GameLibraryLocator.IsUnder(foreground.Path, GameLibraries))
        {
            return foreground;
        }

        var installed = snapshot.Processes.FirstOrDefault(p => !NeverGames.Contains(p.Name) && GameLibraryLocator.IsUnder(p.Path, GameLibraries));
        if (installed is not null)
        {
            return installed;
        }

        if (allowFullscreen
            && snapshot.ForegroundFullscreen
            && snapshot.Foreground is { } fullscreen
            && !NeverGames.Contains(fullscreen.Name)
            && snapshot.GpuLoad >= FullscreenGpuLoad)
        {
            if (_fullscreenBusyProcess != fullscreen.Id)
            {
                _fullscreenBusyProcess = fullscreen.Id;
                _fullscreenBusySince = snapshot.Now;
            }

            if (snapshot.Now - _fullscreenBusySince >= FullscreenDuration)
            {
                return fullscreen;
            }
        }
        else
        {
            _fullscreenBusyProcess = null;
            _fullscreenBusySince = null;
        }

        return null;
    }

    private ProfileDecision? Evaluate(ProfileSet set, ActivitySnapshot snapshot)
    {
        var fallback = set.Default;
        if (fallback is null)
        {
            return null;
        }

        if (set.Find(set.Pinned) is { } pinned)
        {
            return new ProfileDecision(pinned, ProfileTrigger.Pinned, "chosen by you", pinned.Dim);
        }

        if (!set.Automatic)
        {
            return new ProfileDecision(fallback, ProfileTrigger.Default, "automatic switching is off", fallback.Dim);
        }

        var enabled = set.Profiles.Where(p => p.Enabled).ToList();

        if (MatchSensor(enabled, snapshot) is { } sensor)
        {
            return sensor;
        }

        if (MatchApps(enabled, snapshot) is { } app)
        {
            _lastApp = (app, snapshot.Now);
            return app;
        }

        if (_lastApp is { } last
            && snapshot.Now - last.At < AppLinger
            && enabled.Any(p => string.Equals(p.Id, last.Decision.Profile.Id, StringComparison.OrdinalIgnoreCase)))
        {
            return last.Decision;
        }

        _lastApp = null;
        var scheduled = enabled.FirstOrDefault(p => p.Triggers.Schedules.Any(s => s.Contains(snapshot.Now)));

        foreach (var profile in enabled)
        {
            var triggers = profile.Triggers;
            if (triggers.OnLock && snapshot.Locked)
            {
                return new ProfileDecision(profile, ProfileTrigger.Locked, "PC locked", Math.Max(profile.Dim, scheduled?.Dim ?? 0));
            }

            if (triggers.IdleMinutes is { } minutes && snapshot.IdleSeconds >= minutes * 60d)
            {
                return new ProfileDecision(profile, ProfileTrigger.Idle, $"idle for {minutes} min", Math.Max(profile.Dim, scheduled?.Dim ?? 0));
            }
        }

        if (scheduled is not null)
        {
            var window = scheduled.Triggers.Schedules.First(s => s.Contains(snapshot.Now));
            return new ProfileDecision(scheduled, ProfileTrigger.Schedule, $"scheduled {window.Start:HH\\:mm}–{window.End:HH\\:mm}", scheduled.Dim);
        }

        return new ProfileDecision(fallback, ProfileTrigger.Default, "default", fallback.Dim);
    }

    private ProfileDecision? MatchApps(List<Profile> enabled, ActivitySnapshot snapshot)
    {
        ProcessInfo? game = null;
        var gameChecked = false;

        foreach (var profile in enabled)
        {
            var triggers = profile.Triggers;
            foreach (var pattern in triggers.Apps)
            {
                var match = snapshot.Foreground is { } fg && Matches(pattern, fg)
                    ? fg
                    : snapshot.Processes.FirstOrDefault(p => Matches(pattern, p));
                if (match is not null)
                {
                    return new ProfileDecision(profile, ProfileTrigger.App, match.Name + ".exe", profile.Dim);
                }
            }

            if (triggers.DetectGames)
            {
                if (!gameChecked)
                {
                    game = DetectGame(snapshot, triggers.DetectFullscreen);
                    gameChecked = true;
                }

                if (game is not null)
                {
                    return new ProfileDecision(profile, ProfileTrigger.Game, game.Name + ".exe", profile.Dim);
                }
            }
        }

        return null;
    }

    private static ProfileDecision? MatchSensor(List<Profile> enabled, ActivitySnapshot snapshot)
    {
        foreach (var profile in enabled)
        {
            if (SensorIsTrue(profile.Triggers.Sensor, snapshot.Sensors))
            {
                var reason = profile.Triggers.Sensor!.Trim();
                if (reason.Length > 48)
                {
                    reason = reason[..48];
                }

                return new ProfileDecision(profile, ProfileTrigger.Sensor, reason, profile.Dim);
            }
        }

        return null;
    }

    /// <summary>Whether a sensor expression is true for these readings. A missing sensor is false.</summary>
    internal static bool SensorIsTrue(string? expression, IReadOnlyDictionary<string, double> sensors)
    {
        if (string.IsNullOrWhiteSpace(expression) || expression.Length > 240)
        {
            return false;
        }

        if (!BindingExpression.TryParse(expression, out var parsed, out _))
        {
            return false;
        }

        try
        {
            return parsed!.Evaluate(new NumberMap(sensors)).AsBoolean();
        }
        catch (ExpressionException)
        {
            return false;
        }
    }

    private sealed class NumberMap(IReadOnlyDictionary<string, double> values) : IBindingContext
    {
        public BindingValue ResolveSensor(string reference) =>
            values.TryGetValue(reference, out var value) ? BindingValue.FromNumber(value) : BindingValue.Missing;

        public BindingValue ResolveVariable(string name) => BindingValue.Missing;
    }
}
