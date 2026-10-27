namespace Panelith.Hosting.Profiles;

/// <summary>Every profile, in priority order, plus how the active one is chosen.</summary>
public sealed record ProfileSet
{
    /// <summary>The file format version.</summary>
    public int Version { get; init; } = 1;

    /// <summary>Whether profiles switch by themselves. When off, the pinned or default profile is used.</summary>
    public bool Automatic { get; init; } = true;

    /// <summary>A profile the user chose by hand, which overrides every trigger until unpinned.</summary>
    public string? Pinned { get; init; }

    /// <summary>The profiles. Earlier ones win when two match at the same time.</summary>
    public IReadOnlyList<Profile> Profiles { get; init; } = [];

    /// <summary>Finds a profile by id.</summary>
    public Profile? Find(string? id) =>
        id is null ? null : Profiles.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>The profile used when nothing else applies.</summary>
    public Profile? Default =>
        Profiles.FirstOrDefault(p => p.IsDefault && p.Enabled)
        ?? Profiles.FirstOrDefault(p => p.Enabled)
        ?? Profiles.FirstOrDefault();
}

/// <summary>A named set of screens with the conditions that bring it up.</summary>
public sealed record Profile
{
    /// <summary>A stable identifier, used in settings and logs.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>The name shown to the user.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>An icon key understood by the desktop app.</summary>
    public string Icon { get; init; } = "screen";

    /// <summary>The colour that identifies the profile in the app.</summary>
    public string Accent { get; init; } = "#3ee0c5";

    /// <summary>Disabled profiles are never chosen automatically.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Whether this is the profile used when no trigger matches.</summary>
    public bool IsDefault { get; init; }

    /// <summary>The screens to show, in order. The first is shown when the profile starts.</summary>
    public IReadOnlyList<string> Screens { get; init; } = [];

    /// <summary>Whether the screens cycle on a timer.</summary>
    public bool Rotate { get; init; } = true;

    /// <summary>How long each screen stays up while rotating.</summary>
    public int RotationSeconds { get; init; } = 20;

    /// <summary>How much to darken the panel, from 0 (normal) to 1 (black).</summary>
    public double Dim { get; init; }

    /// <summary>A frame rate for the panel while this profile is active, or null to keep the current one.</summary>
    public int? FramesPerSecond { get; init; }

    /// <summary>When the profile turns on by itself.</summary>
    public ProfileTriggers Triggers { get; init; } = new();
}

/// <summary>The conditions that switch to a profile.</summary>
public sealed record ProfileTriggers
{
    /// <summary>
    /// Programs that bring the profile up while running: an executable name with
    /// or without ".exe", a full path, or a name with <c>*</c> wildcards.
    /// </summary>
    public IReadOnlyList<string> Apps { get; init; } = [];

    /// <summary>Also turn on for any detected game.</summary>
    public bool DetectGames { get; init; }

    /// <summary>
    /// When detecting games, also count a fullscreen window keeping the GPU busy.
    /// </summary>
    public bool DetectFullscreen { get; init; } = true;

    /// <summary>Times of the week when the profile is on.</summary>
    public IReadOnlyList<ScheduleWindow> Schedules { get; init; } = [];

    /// <summary>Turn on after this many minutes without keyboard or mouse input.</summary>
    public int? IdleMinutes { get; init; }

    /// <summary>Turn on while Windows is locked.</summary>
    public bool OnLock { get; init; }

    /// <summary>A global shortcut such as "Ctrl+Alt+1" that pins the profile.</summary>
    public string? Hotkey { get; init; }

    /// <summary>
    /// An expression that brings the profile up while it is true, such as
    /// <c>@gpu.temp &gt; 85</c> or <c>@cpu.fan &lt; 100</c>. Empty means this
    /// trigger is off. A missing sensor does not match.
    /// </summary>
    public string? Sensor { get; init; }

    /// <summary>Whether any automatic trigger is set.</summary>
    public bool HasAny =>
        Apps.Count > 0 || DetectGames || Schedules.Count > 0 || IdleMinutes is not null || OnLock
        || !string.IsNullOrWhiteSpace(Sensor);
}

/// <summary>A daily time range on chosen days. A range that ends before it starts runs past midnight.</summary>
public sealed record ScheduleWindow
{
    /// <summary>Every day of the week.</summary>
    public static readonly IReadOnlyList<DayOfWeek> EveryDay =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];

    /// <summary>Monday to Friday.</summary>
    public static readonly IReadOnlyList<DayOfWeek> Weekdays =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];

    /// <summary>The days the range starts on.</summary>
    public IReadOnlyList<DayOfWeek> Days { get; init; } = EveryDay;

    /// <summary>When the range starts.</summary>
    public TimeOnly Start { get; init; }

    /// <summary>When the range ends.</summary>
    public TimeOnly End { get; init; }

    /// <summary>Whether a moment falls inside the range.</summary>
    /// <remarks>
    /// For a range that crosses midnight the day that counts is the day it
    /// started, so "Friday 23:00 to 07:00" covers early Saturday but not early Friday.
    /// </remarks>
    public bool Contains(DateTime now)
    {
        var time = TimeOnly.FromDateTime(now);
        if (Start == End)
        {
            return Days.Contains(now.DayOfWeek);
        }

        if (Start < End)
        {
            return Days.Contains(now.DayOfWeek) && time >= Start && time < End;
        }

        return (time >= Start && Days.Contains(now.DayOfWeek))
            || (time < End && Days.Contains(now.AddDays(-1).DayOfWeek));
    }
}
