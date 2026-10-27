using Panelith.Hosting.Profiles;
using Xunit;

namespace Panelith.Hosting;

public sealed class ProfileResolverTests
{
    // A Thursday afternoon, outside Night's 23:00-07:00 window.
    private static readonly DateTime Afternoon = new(2026, 10, 15, 15, 0, 0);

    private static readonly ProcessInfo Explorer = new(10, "explorer", @"C:\Windows\explorer.exe");
    private static readonly ProcessInfo Obs = new(20, "obs64", @"C:\Program Files\obs-studio\bin\64bit\obs64.exe");
    private static readonly ProcessInfo Blender = new(30, "blender", @"C:\Program Files\Blender Foundation\Blender 4.2\blender.exe");
    private static readonly ProcessInfo Doom = new(40, "DOOMEternalx64vk", @"D:\SteamLibrary\steamapps\common\DOOMEternal\DOOMEternalx64vk.exe");

    private static ActivitySnapshot At(DateTime now, params ProcessInfo[] processes) =>
        new() { Now = now, Processes = [Explorer, .. processes], Foreground = processes.Length > 0 ? processes[^1] : Explorer };

    private static ProfileResolver NewResolver() => new([@"D:\SteamLibrary\steamapps\common"]);

    [Fact]
    public void NothingRunningUsesTheDefault()
    {
        var decision = NewResolver().Resolve(DefaultProfiles.Create(), At(Afternoon))!;

        Assert.Equal("everyday", decision.Profile.Id);
        Assert.Equal(ProfileTrigger.Default, decision.Trigger);
        Assert.Equal("default", decision.Reason);
    }

    [Fact]
    public void GameInALibraryPicksGaming()
    {
        var decision = NewResolver().Resolve(DefaultProfiles.Create(), At(Afternoon, Doom))!;

        Assert.Equal("gaming", decision.Profile.Id);
        Assert.Equal(ProfileTrigger.Game, decision.Trigger);
        Assert.Equal("DOOMEternalx64vk.exe", decision.Reason);
    }

    [Fact]
    public void EarlierProfilesWinWhenSeveralMatch()
    {
        // Streaming a creative session: Streaming sits above Creating in the list.
        var decision = NewResolver().Resolve(DefaultProfiles.Create(), At(Afternoon, Blender, Obs))!;
        Assert.Equal("streaming", decision.Profile.Id);

        // Streaming a game: Gaming sits above Streaming.
        decision = NewResolver().Resolve(DefaultProfiles.Create(), At(Afternoon, Obs, Doom))!;
        Assert.Equal("gaming", decision.Profile.Id);
    }

    [Fact]
    public void RtssHookedProcessCountsAsAGame()
    {
        var game = new ProcessInfo(50, "eldenring", @"E:\Games\ELDEN RING\eldenring.exe");
        var snapshot = At(Afternoon, game) with { RtssProcessId = 50 };

        var decision = new ProfileResolver().Resolve(DefaultProfiles.Create(), snapshot)!;

        Assert.Equal("gaming", decision.Profile.Id);
    }

    [Fact]
    public void RtssHookOnIgnoredProgramIsNotAGame()
    {
        var browser = new ProcessInfo(60, "chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe");
        var snapshot = At(Afternoon, browser) with { RtssProcessId = 60 };

        Assert.Equal("everyday", new ProfileResolver().Resolve(DefaultProfiles.Create(), snapshot)!.Profile.Id);
    }

    [Fact]
    public void FullscreenGpuLoadCountsAsAGameOnlyAfterItHolds()
    {
        var game = new ProcessInfo(70, "indiegame", @"C:\Itch\indiegame\indiegame.exe");
        var resolver = new ProfileResolver();
        var set = DefaultProfiles.Create();
        ActivitySnapshot Busy(DateTime now) => At(now, game) with { ForegroundFullscreen = true, GpuLoad = 90 };

        Assert.Equal("everyday", resolver.Resolve(set, Busy(Afternoon))!.Profile.Id);
        Assert.Equal("everyday", resolver.Resolve(set, Busy(Afternoon.AddSeconds(5)))!.Profile.Id);

        // Detected at 10 s, then held for the 4 s switch delay.
        Assert.Equal("everyday", resolver.Resolve(set, Busy(Afternoon.AddSeconds(10)))!.Profile.Id);
        Assert.Equal("gaming", resolver.Resolve(set, Busy(Afternoon.AddSeconds(14)))!.Profile.Id);
    }

    [Fact]
    public void FullscreenVideoWithIdleGpuIsNotAGame()
    {
        var player = new ProcessInfo(80, "movies", @"C:\Apps\movies.exe");
        var resolver = new ProfileResolver();
        var set = DefaultProfiles.Create();

        for (var s = 0; s <= 30; s += 2)
        {
            var snapshot = At(Afternoon.AddSeconds(s), player) with { ForegroundFullscreen = true, GpuLoad = 8 };
            Assert.Equal("everyday", resolver.Resolve(set, snapshot)!.Profile.Id);
        }
    }

    [Fact]
    public void NewDecisionsHoldBeforeSwitching()
    {
        var resolver = NewResolver();
        var set = DefaultProfiles.Create();

        Assert.Equal("everyday", resolver.Resolve(set, At(Afternoon))!.Profile.Id);
        Assert.Equal("everyday", resolver.Resolve(set, At(Afternoon.AddSeconds(2), Blender))!.Profile.Id);
        Assert.Equal("everyday", resolver.Resolve(set, At(Afternoon.AddSeconds(4), Blender))!.Profile.Id);
        Assert.Equal("creating", resolver.Resolve(set, At(Afternoon.AddSeconds(6), Blender))!.Profile.Id);
    }

    [Fact]
    public void AppProfileLingersAfterTheAppCloses()
    {
        var resolver = NewResolver();
        resolver.SwitchDelay = TimeSpan.Zero;
        var set = DefaultProfiles.Create();

        Assert.Equal("creating", resolver.Resolve(set, At(Afternoon, Blender))!.Profile.Id);
        Assert.Equal("creating", resolver.Resolve(set, At(Afternoon.AddSeconds(5)))!.Profile.Id);
        Assert.Equal("everyday", resolver.Resolve(set, At(Afternoon.AddSeconds(11)))!.Profile.Id);
    }

    [Fact]
    public void PinBeatsEverythingAndSwitchesAtOnce()
    {
        var resolver = NewResolver();
        var set = DefaultProfiles.Create();
        Assert.Equal("gaming", resolver.Resolve(set, At(Afternoon, Doom))!.Profile.Id);

        var pinned = set with { Pinned = "night" };
        var decision = resolver.Resolve(pinned, At(Afternoon.AddSeconds(1), Doom) with { Locked = true })!;

        Assert.Equal("night", decision.Profile.Id);
        Assert.Equal(ProfileTrigger.Pinned, decision.Trigger);
        Assert.Equal(0.4, decision.Dim);

        // Unpinning also takes effect immediately.
        Assert.Equal("gaming", resolver.Resolve(set, At(Afternoon.AddSeconds(2), Doom))!.Profile.Id);
    }

    [Fact]
    public void ManualModeIgnoresTriggers()
    {
        var set = DefaultProfiles.Create() with { Automatic = false };

        var decision = NewResolver().Resolve(set, At(Afternoon, Doom))!;

        Assert.Equal("everyday", decision.Profile.Id);
        Assert.Equal("automatic switching is off", decision.Reason);
    }

    [Fact]
    public void RunningAppBeatsIdleSoAPausedGameStaysPut()
    {
        var snapshot = At(Afternoon, Doom) with { IdleSeconds = 3600 };

        Assert.Equal("gaming", NewResolver().Resolve(DefaultProfiles.Create(), snapshot)!.Profile.Id);
    }

    [Fact]
    public void IdleAndLockPickAway()
    {
        var idle = NewResolver().Resolve(DefaultProfiles.Create(), At(Afternoon) with { IdleSeconds = 301 })!;
        Assert.Equal("away", idle.Profile.Id);
        Assert.Equal("idle for 5 min", idle.Reason);

        var locked = NewResolver().Resolve(DefaultProfiles.Create(), At(Afternoon) with { Locked = true })!;
        Assert.Equal("away", locked.Profile.Id);
        Assert.Equal(ProfileTrigger.Locked, locked.Trigger);

        var notYet = NewResolver().Resolve(DefaultProfiles.Create(), At(Afternoon) with { IdleSeconds = 120 })!;
        Assert.Equal("everyday", notYet.Profile.Id);
    }

    [Fact]
    public void AwayAtNightKeepsTheNightDimming()
    {
        var midnight = new DateTime(2026, 10, 15, 23, 30, 0);

        var decision = NewResolver().Resolve(DefaultProfiles.Create(), At(midnight) with { Locked = true })!;

        Assert.Equal("away", decision.Profile.Id);
        Assert.Equal(0.4, decision.Dim);
    }

    [Fact]
    public void ScheduleAppliesInsideItsWindow()
    {
        var late = NewResolver().Resolve(DefaultProfiles.Create(), At(new DateTime(2026, 10, 16, 2, 0, 0)))!;
        Assert.Equal("night", late.Profile.Id);
        Assert.Equal(ProfileTrigger.Schedule, late.Trigger);
        Assert.Equal("scheduled 23:00–07:00", late.Reason);
        Assert.Equal(0.4, late.Dim);

        var morning = NewResolver().Resolve(DefaultProfiles.Create(), At(new DateTime(2026, 10, 16, 7, 0, 0)))!;
        Assert.Equal("everyday", morning.Profile.Id);
    }

    [Fact]
    public void DisabledProfilesNeverTrigger()
    {
        // Work is disabled by default even though it is a weekday afternoon.
        Assert.Equal("everyday", NewResolver().Resolve(DefaultProfiles.Create(), At(Afternoon))!.Profile.Id);

        var set = DefaultProfiles.Create();
        var enabled = set with { Profiles = [.. set.Profiles.Select(p => p.Id == "work" ? p with { Enabled = true } : p)] };
        Assert.Equal("work", NewResolver().Resolve(enabled, At(Afternoon))!.Profile.Id);
    }

    [Theory]
    [InlineData("obs64", true)]
    [InlineData("OBS64.EXE", true)]
    [InlineData("obs", false)]
    [InlineData("obs*", true)]
    [InlineData("*64", true)]
    [InlineData("o*s*4", true)]
    [InlineData("x*", false)]
    [InlineData(@"C:\Program Files\obs-studio\bin\64bit\obs64.exe", true)]
    [InlineData(@"C:\Other\obs64.exe", false)]
    [InlineData("  ", false)]
    public void AppPatternsMatchNamesPathsAndWildcards(string pattern, bool expected)
    {
        Assert.Equal(expected, ProfileResolver.Matches(pattern, Obs));
    }

    [Fact]
    public void WildcardPatternsEscapeRegexCharacters()
    {
        var process = new ProcessInfo(1, "app.v2", null);

        Assert.True(ProfileResolver.Matches("app.v*", process));
        Assert.False(ProfileResolver.Matches("app.v2(*", process));
        Assert.False(ProfileResolver.Matches(@"C:\app.v2.exe", process));
    }

    [Fact]
    public void AHotGpuBringsUpAWarningProfileAheadOfAGame()
    {
        var set = DefaultProfiles.Create();
        var warning = new Profile
        {
            Id = "hot",
            Name = "Hot",
            Screens = ["clock"],
            Triggers = new ProfileTriggers { Sensor = "@gpu.temp > 85" },
        };
        set = set with { Profiles = [warning, .. set.Profiles] };
        var snapshot = At(Afternoon, Doom) with
        {
            Sensors = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["@gpu.temp"] = 90 },
        };

        var decision = NewResolver().Resolve(set, snapshot)!;

        Assert.Equal("hot", decision.Profile.Id);
        Assert.Equal(ProfileTrigger.Sensor, decision.Trigger);
    }

    [Fact]
    public void AMissingSensorDoesNotMatchSoTheGameStillWins()
    {
        var set = DefaultProfiles.Create();
        var warning = new Profile
        {
            Id = "hot",
            Name = "Hot",
            Screens = ["clock"],
            Triggers = new ProfileTriggers { Sensor = "@gpu.temp > 85" },
        };
        set = set with { Profiles = [warning, .. set.Profiles] };

        var decision = NewResolver().Resolve(set, At(Afternoon, Doom))!;

        Assert.Equal("gaming", decision.Profile.Id);
    }

    [Fact]
    public void AStoppedFanMatches()
    {
        var profile = new Profile
        {
            Id = "fan",
            Name = "Fan",
            IsDefault = true,
            Triggers = new ProfileTriggers { Sensor = "@cpu.fan < 100" },
        };
        var set = new ProfileSet { Profiles = [profile] };
        var snapshot = At(Afternoon) with
        {
            Sensors = new Dictionary<string, double> { ["@cpu.fan"] = 0 },
        };

        var decision = NewResolver().Resolve(set, snapshot)!;

        Assert.Equal(ProfileTrigger.Sensor, decision.Trigger);
        Assert.False(ProfileResolver.SensorIsTrue("@cpu.fan < 100", new Dictionary<string, double>()));
    }
}
