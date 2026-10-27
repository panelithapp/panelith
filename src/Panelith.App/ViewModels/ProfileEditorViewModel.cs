using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Panelith.App.Services.Activity;
using Panelith.Expressions;
using Panelith.Hosting.Profiles;

namespace Panelith.App.ViewModels;

/// <summary>A colour a profile can use.</summary>
public sealed record AccentChoice(string Hex, IBrush Brush);

/// <summary>An icon a profile can use.</summary>
public sealed record IconChoice(string Key, Geometry Geometry);

/// <summary>A schedule row in the editor.</summary>
public sealed partial class ScheduleViewModel : ObservableObject
{
    private readonly Action _changed;
    private readonly bool _loading;

    public ScheduleViewModel(ScheduleWindow window, Action changed)
    {
        _loading = true;
        _changed = changed;
        Start = window.Start.ToTimeSpan();
        End = window.End.ToTimeSpan();
        Monday = window.Days.Contains(DayOfWeek.Monday);
        Tuesday = window.Days.Contains(DayOfWeek.Tuesday);
        Wednesday = window.Days.Contains(DayOfWeek.Wednesday);
        Thursday = window.Days.Contains(DayOfWeek.Thursday);
        Friday = window.Days.Contains(DayOfWeek.Friday);
        Saturday = window.Days.Contains(DayOfWeek.Saturday);
        Sunday = window.Days.Contains(DayOfWeek.Sunday);
        _loading = false;
    }

    [ObservableProperty]
    public partial TimeSpan? Start { get; set; }

    [ObservableProperty]
    public partial TimeSpan? End { get; set; }

    [ObservableProperty]
    public partial bool Monday { get; set; }

    [ObservableProperty]
    public partial bool Tuesday { get; set; }

    [ObservableProperty]
    public partial bool Wednesday { get; set; }

    [ObservableProperty]
    public partial bool Thursday { get; set; }

    [ObservableProperty]
    public partial bool Friday { get; set; }

    [ObservableProperty]
    public partial bool Saturday { get; set; }

    [ObservableProperty]
    public partial bool Sunday { get; set; }

    public ScheduleWindow ToWindow()
    {
        var days = new List<DayOfWeek>();
        if (Monday) days.Add(DayOfWeek.Monday);
        if (Tuesday) days.Add(DayOfWeek.Tuesday);
        if (Wednesday) days.Add(DayOfWeek.Wednesday);
        if (Thursday) days.Add(DayOfWeek.Thursday);
        if (Friday) days.Add(DayOfWeek.Friday);
        if (Saturday) days.Add(DayOfWeek.Saturday);
        if (Sunday) days.Add(DayOfWeek.Sunday);
        return new ScheduleWindow
        {
            Days = days,
            Start = TimeOnly.FromTimeSpan(Start ?? TimeSpan.Zero),
            End = TimeOnly.FromTimeSpan(End ?? TimeSpan.Zero),
        };
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!_loading)
        {
            _changed();
        }
    }
}

/// <summary>Edits one profile. Every change is saved straight away.</summary>
public sealed partial class ProfileEditorViewModel : ObservableObject
{
    private static readonly string[] Palette =
        ["#3ee0c5", "#76b900", "#7aa2ff", "#d29cff", "#ff5d73", "#ff8a5c", "#f0a35c", "#f0c14a", "#5bd1ff", "#e8eef6"];

    private readonly ProfilesViewModel _owner;
    private Profile _profile;
    private bool _loading;

    internal ProfileEditorViewModel(ProfilesViewModel owner, Profile profile)
    {
        _owner = owner;
        _profile = profile;
        Accents = [.. Palette.Select(hex => new AccentChoice(hex, new SolidColorBrush(Color.Parse(hex))))];
        IconChoices = [.. Icons.ProfileIcons.Select(kv => new IconChoice(kv.Key, kv.Value))];
        Load(profile);
    }

    public string Id => _profile.Id;

    public IReadOnlyList<AccentChoice> Accents { get; }

    public IReadOnlyList<IconChoice> IconChoices { get; }

    public ObservableCollection<ScreenThumb> Screens { get; } = [];

    public ObservableCollection<ScreenThumb> AddableScreens { get; } = [];

    public ObservableCollection<string> Apps { get; } = [];

    public ObservableCollection<ScheduleViewModel> Schedules { get; } = [];

    /// <summary>Programs with a window right now, for the "running apps" picker.</summary>
    public ObservableCollection<string> RunningApps { get; } = [];

    /// <summary>Well-known creative and streaming programs.</summary>
    public IReadOnlyList<string> SuggestedApps { get; } = [.. DefaultProfiles.CreativeApps.Concat(DefaultProfiles.StreamingApps).Order(StringComparer.OrdinalIgnoreCase)];

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial AccentChoice? Accent { get; set; }

    [ObservableProperty]
    public partial IconChoice? Icon { get; set; }

    [ObservableProperty]
    public partial bool Enabled { get; set; }

    [ObservableProperty]
    public partial bool IsDefault { get; set; }

    [ObservableProperty]
    public partial bool Rotate { get; set; }

    [ObservableProperty]
    public partial int RotationSeconds { get; set; }

    [ObservableProperty]
    public partial ScreenThumb? ScreenToAdd { get; set; }

    [ObservableProperty]
    public partial string NewApp { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? RunningAppToAdd { get; set; }

    [ObservableProperty]
    public partial string? SuggestedAppToAdd { get; set; }

    [ObservableProperty]
    public partial bool DetectGames { get; set; }

    [ObservableProperty]
    public partial bool DetectFullscreen { get; set; }

    [ObservableProperty]
    public partial bool IdleEnabled { get; set; }

    [ObservableProperty]
    public partial int IdleMinutes { get; set; }

    [ObservableProperty]
    public partial bool OnLock { get; set; }

    /// <summary>Expression that brings this profile up, such as <c>@gpu.temp &gt; 85</c>.</summary>
    [ObservableProperty]
    public partial string Sensor { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? SensorError { get; set; }

    [ObservableProperty]
    public partial string Hotkey { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? HotkeyError { get; set; }

    [ObservableProperty]
    public partial int DimPercent { get; set; }

    [ObservableProperty]
    public partial bool FrameRateOverride { get; set; }

    [ObservableProperty]
    public partial int FramesPerSecond { get; set; }

    [ObservableProperty]
    public partial bool IsPinned { get; set; }

    public bool CanDelete => _owner.Count > 1;

    partial void OnScreenToAddChanged(ScreenThumb? value)
    {
        if (value is not null && !_loading)
        {
            Screens.Add(value);
            AddableScreens.Remove(value);
            ScreenToAdd = null;
            Commit();
        }
    }

    partial void OnRunningAppToAddChanged(string? value)
    {
        if (value is not null)
        {
            AddApp(value);
            RunningAppToAdd = null;
        }
    }

    partial void OnSuggestedAppToAddChanged(string? value)
    {
        if (value is not null)
        {
            AddApp(value);
            SuggestedAppToAdd = null;
        }
    }

    partial void OnSensorChanged(string value)
    {
        var text = value.Trim();
        SensorError = text.Length == 0
            ? null
            : text.Length > 240
                ? "Keep the condition under 240 characters."
                : BindingExpression.TryParse(text, out _, out var error) ? null : error;
        if (SensorError is null)
        {
            Commit();
        }
    }

    partial void OnHotkeyChanged(string value)
    {
        HotkeyError = value.Length == 0 || Services.Activity.Hotkey.TryParse(value) is not null
            ? null
            : "Use Ctrl, Alt or Win with a key, such as Ctrl+Alt+1.";
        if (HotkeyError is null)
        {
            Commit();
        }
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        switch (e.PropertyName)
        {
            case nameof(Name) or nameof(Accent) or nameof(Icon) or nameof(Enabled) or nameof(IsDefault) or nameof(Rotate)
                or nameof(RotationSeconds) or nameof(DetectGames) or nameof(DetectFullscreen) or nameof(IdleEnabled)
                or nameof(IdleMinutes) or nameof(OnLock) or nameof(DimPercent) or nameof(FrameRateOverride) or nameof(FramesPerSecond):
                Commit();
                break;
        }
    }

    /// <summary>Adds a program by name or path.</summary>
    [RelayCommand]
    public void AddApp(string? app)
    {
        var value = (app ?? NewApp).Trim();
        if (value.Length == 0 || Apps.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            NewApp = string.Empty;
            return;
        }

        Apps.Add(value);
        NewApp = string.Empty;
        Commit();
    }

    [RelayCommand]
    private void RemoveApp(string app)
    {
        Apps.Remove(app);
        Commit();
    }

    [RelayCommand]
    private void RemoveScreen(ScreenThumb screen)
    {
        Screens.Remove(screen);
        AddableScreens.Add(screen);
        Commit();
    }

    [RelayCommand]
    private void MoveScreenUp(ScreenThumb screen)
    {
        var index = Screens.IndexOf(screen);
        if (index > 0)
        {
            Screens.Move(index, index - 1);
            Commit();
        }
    }

    [RelayCommand]
    private void MoveScreenDown(ScreenThumb screen)
    {
        var index = Screens.IndexOf(screen);
        if (index >= 0 && index < Screens.Count - 1)
        {
            Screens.Move(index, index + 1);
            Commit();
        }
    }

    [RelayCommand]
    private void AddSchedule()
    {
        Schedules.Add(new ScheduleViewModel(new ScheduleWindow { Start = new TimeOnly(9, 0), End = new TimeOnly(17, 0), Days = ScheduleWindow.Weekdays }, Commit));
        Commit();
    }

    [RelayCommand]
    private void RemoveSchedule(ScheduleViewModel schedule)
    {
        Schedules.Remove(schedule);
        Commit();
    }

    [RelayCommand]
    private void ClearHotkey() => Hotkey = string.Empty;

    /// <summary>Fills the running-apps picker.</summary>
    [RelayCommand]
    public void RefreshRunningApps()
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.MainWindowHandle != 0 && !string.Equals(process.ProcessName, "Panelith", StringComparison.OrdinalIgnoreCase))
                    {
                        names.Add(process.ProcessName);
                    }
                }
                catch (InvalidOperationException)
                {
                    // Exited while listing.
                }
            }
        }

        RunningApps.Clear();
        foreach (var name in names)
        {
            RunningApps.Add(name);
        }
    }

    /// <summary>Takes on a newer copy of the profile saved elsewhere, such as the Screens page.</summary>
    internal void Reload(Profile profile)
    {
        if (profile == _profile)
        {
            return;
        }

        Load(profile);
    }

    internal void SetPinned(bool pinned) => IsPinned = pinned;

    private void Load(Profile profile)
    {
        _loading = true;
        _profile = profile;
        Name = profile.Name;
        Accent = Accents.FirstOrDefault(a => string.Equals(a.Hex, profile.Accent, StringComparison.OrdinalIgnoreCase))
                 ?? new AccentChoice(profile.Accent, new SolidColorBrush(Color.TryParse(profile.Accent, out var c) ? c : Colors.Teal));
        Icon = IconChoices.FirstOrDefault(i => string.Equals(i.Key, profile.Icon, StringComparison.OrdinalIgnoreCase)) ?? IconChoices[^1];
        Enabled = profile.Enabled;
        IsDefault = profile.IsDefault;
        Rotate = profile.Rotate;
        RotationSeconds = Math.Clamp(profile.RotationSeconds, 5, 600);
        DetectGames = profile.Triggers.DetectGames;
        DetectFullscreen = profile.Triggers.DetectFullscreen;
        IdleEnabled = profile.Triggers.IdleMinutes is not null;
        IdleMinutes = profile.Triggers.IdleMinutes ?? 5;
        OnLock = profile.Triggers.OnLock;
        Sensor = profile.Triggers.Sensor ?? string.Empty;
        Hotkey = profile.Triggers.Hotkey ?? string.Empty;
        DimPercent = (int)Math.Round(profile.Dim * 100);
        FrameRateOverride = profile.FramesPerSecond is not null;
        FramesPerSecond = profile.FramesPerSecond ?? 10;

        Screens.Clear();
        foreach (var thumb in _owner.ThumbsFor(profile.Screens, int.MaxValue))
        {
            Screens.Add(thumb);
        }

        AddableScreens.Clear();
        foreach (var thumb in _owner.AllThumbs().Where(t => !profile.Screens.Contains(t.Id, StringComparer.OrdinalIgnoreCase)))
        {
            AddableScreens.Add(thumb);
        }

        Apps.Clear();
        foreach (var app in profile.Triggers.Apps)
        {
            Apps.Add(app);
        }

        Schedules.Clear();
        foreach (var window in profile.Triggers.Schedules)
        {
            Schedules.Add(new ScheduleViewModel(window, Commit));
        }

        _loading = false;
    }

    private void Commit()
    {
        if (_loading)
        {
            return;
        }

        _profile = _profile with
        {
            Name = string.IsNullOrWhiteSpace(Name) ? _profile.Name : Name.Trim(),
            Accent = Accent?.Hex ?? _profile.Accent,
            Icon = Icon?.Key ?? _profile.Icon,
            Enabled = Enabled,
            IsDefault = IsDefault,
            Screens = [.. Screens.Select(s => s.Id)],
            Rotate = Rotate,
            RotationSeconds = Math.Clamp(RotationSeconds, 5, 600),
            Dim = Math.Clamp(DimPercent, 0, 100) / 100d,
            FramesPerSecond = FrameRateOverride ? Math.Clamp(FramesPerSecond, 1, 15) : null,
            Triggers = _profile.Triggers with
            {
                Apps = [.. Apps],
                DetectGames = DetectGames,
                DetectFullscreen = DetectFullscreen,
                IdleMinutes = IdleEnabled ? Math.Clamp(IdleMinutes, 1, 240) : null,
                OnLock = OnLock,
                Sensor = SensorError is null && Sensor.Trim().Length > 0 ? Sensor.Trim() : null,
                Schedules = [.. Schedules.Select(s => s.ToWindow())],
                Hotkey = HotkeyError is null && Hotkey.Length > 0 ? Services.Activity.Hotkey.TryParse(Hotkey)?.Text : null,
            },
        };

        _owner.Commit(_profile);
    }
}
