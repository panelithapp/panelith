namespace Panelith.Sensors;

/// <summary>
/// Canonical aliases for the handful of sensors every theme wants.
/// </summary>
/// <remarks>
/// <para>
/// Sensor ids are necessarily machine-specific -- a Ryzen reports
/// <c>cpu/0/temperature/tctl_tdie</c> where an Intel part reports
/// <c>cpu/0/temperature/package</c> -- which makes a theme written against raw
/// ids non-portable. That is the single biggest reason panel themes do not move
/// between machines today.
/// </para>
/// <para>
/// Panelith solves it with aliases: the sensor registry resolves each alias to
/// the best available concrete sensor on this machine, and themes bind to the
/// alias. A theme that uses only aliases renders correctly on any supported
/// machine, and a theme that needs something exotic can still bind to a raw id.
/// </para>
/// </remarks>
public static class WellKnownSensors
{
    /// <summary>Alias namespace prefix. Aliases look like <c>@cpu.temp</c>.</summary>
    public const char AliasPrefix = '@';

    /// <summary>Overall CPU package temperature.</summary>
    public const string CpuTemperature = "@cpu.temp";

    /// <summary>Total CPU utilisation.</summary>
    public const string CpuLoad = "@cpu.load";

    /// <summary>Highest current core clock.</summary>
    public const string CpuClock = "@cpu.clock";

    /// <summary>CPU package power draw.</summary>
    public const string CpuPower = "@cpu.power";

    /// <summary>CPU fan speed.</summary>
    public const string CpuFan = "@cpu.fan";

    /// <summary>Marketing name of the CPU.</summary>
    public const string CpuName = "@cpu.name";

    /// <summary>GPU core temperature.</summary>
    public const string GpuTemperature = "@gpu.temp";

    /// <summary>GPU core utilisation.</summary>
    public const string GpuLoad = "@gpu.load";

    /// <summary>GPU core clock.</summary>
    public const string GpuClock = "@gpu.clock";

    /// <summary>GPU board power draw.</summary>
    public const string GpuPower = "@gpu.power";

    /// <summary>GPU fan speed.</summary>
    public const string GpuFan = "@gpu.fan";

    /// <summary>Video memory in use.</summary>
    public const string GpuMemoryUsed = "@gpu.vram.used";

    /// <summary>Total video memory.</summary>
    public const string GpuMemoryTotal = "@gpu.vram.total";

    /// <summary>Video memory utilisation.</summary>
    public const string GpuMemoryLoad = "@gpu.vram.load";

    /// <summary>Marketing name of the GPU.</summary>
    public const string GpuName = "@gpu.name";

    /// <summary>System memory in use.</summary>
    public const string MemoryUsed = "@mem.used";

    /// <summary>Total system memory.</summary>
    public const string MemoryTotal = "@mem.total";

    /// <summary>System memory utilisation.</summary>
    public const string MemoryLoad = "@mem.load";

    /// <summary>Aggregate network download rate.</summary>
    public const string NetworkDown = "@net.down";

    /// <summary>Aggregate network upload rate.</summary>
    public const string NetworkUp = "@net.up";

    /// <summary>Busiest disk's utilisation.</summary>
    public const string DiskLoad = "@disk.load";

    /// <summary>Hottest drive temperature.</summary>
    public const string DiskTemperature = "@disk.temp";

    /// <summary>How full the system drive is, as a percentage.</summary>
    public const string DiskUsed = "@disk.used";

    /// <summary>Free space on the system drive.</summary>
    public const string DiskFree = "@disk.free";

    /// <summary>Time since boot.</summary>
    public const string Uptime = "@sys.uptime";

    /// <summary>Host name.</summary>
    public const string HostName = "@sys.host";

    /// <summary>Local wall-clock time, as 24-hour <c>HH:mm</c> text.</summary>
    public const string Time = "@sys.time";

    /// <summary>Local date, as short text such as <c>Thu 15 Oct</c>.</summary>
    public const string Date = "@sys.date";

    /// <summary>Operating system name.</summary>
    public const string OperatingSystem = "@sys.os";

    /// <summary>Rendered frames per second, reported by Panelith itself.</summary>
    public const string PanelFps = "@panel.fps";

    /// <summary>Disk read rate across all physical disks.</summary>
    public const string DiskRead = "@disk.read";

    /// <summary>Disk write rate across all physical disks.</summary>
    public const string DiskWrite = "@disk.write";

    /// <summary>Load on the GPU's video encode/decode engine (NVENC and friends).</summary>
    public const string GpuVideo = "@gpu.video";

    /// <summary>The game's frame rate, from RivaTuner Statistics Server.</summary>
    public const string GameFps = "@game.fps";

    /// <summary>The game's frame time in milliseconds, from RivaTuner Statistics Server.</summary>
    public const string GameFrameTime = "@game.frametime";

    /// <summary>The game's 1% low frame rate over the last 30 seconds.</summary>
    public const string GameLow1 = "@game.low1";

    /// <summary>The executable name of the game RivaTuner is measuring.</summary>
    public const string GameName = "@game.name";

    /// <summary>Highest CPU temperature since Panelith started.</summary>
    public const string CpuTemperaturePeak = "@cpu.temp.peak";

    /// <summary>Highest GPU temperature since Panelith started.</summary>
    public const string GpuTemperaturePeak = "@gpu.temp.peak";

    /// <summary>The machine's first local IPv4 address.</summary>
    public const string NetworkAddress = "@net.ipv4";

    /// <summary>Battery charge, as a percentage. Missing on a desktop with no battery.</summary>
    public const string Battery = "@battery";

    /// <summary>Name of the default speakers or headphones.</summary>
    public const string AudioDevice = "@audio.device";

    /// <summary>Volume of the default playback device, from 0 to 100.</summary>
    public const string AudioVolume = "@audio.volume";

    /// <summary>Title of the song or video Windows is playing.</summary>
    public const string MediaTitle = "@media.title";

    /// <summary>Artist of the song or video Windows is playing.</summary>
    public const string MediaArtist = "@media.artist";

    /// <summary>Album of the song or video Windows is playing.</summary>
    public const string MediaAlbum = "@media.album";

    /// <summary>How many per-core load aliases exist: <c>@cpu.core1</c> to <c>@cpu.core32</c>.</summary>
    public const int MaxCoreAliases = 32;

    /// <summary>The load alias for one logical core, numbered from 1.</summary>
    public static string CpuCore(int number) =>
        number is >= 1 and <= MaxCoreAliases
            ? "@cpu.core" + number.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : throw new ArgumentOutOfRangeException(nameof(number));

    /// <summary>The core number of a per-core alias, or null for any other alias.</summary>
    public static int? CoreNumber(string alias) =>
        alias.StartsWith("@cpu.core", StringComparison.OrdinalIgnoreCase)
        && int.TryParse(alias.AsSpan("@cpu.core".Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n)
        && n is >= 1 and <= MaxCoreAliases
            ? n
            : null;

    /// <summary>Every alias Panelith defines, with the kind it resolves to.</summary>
    public static IReadOnlyDictionary<string, SensorKind> All { get; } = BuildAll();

    private static Dictionary<string, SensorKind> BuildAll()
    {
        var all = new Dictionary<string, SensorKind>(StringComparer.OrdinalIgnoreCase)
        {
            [CpuTemperature] = SensorKind.Temperature,
            [CpuLoad] = SensorKind.Load,
            [CpuClock] = SensorKind.Clock,
            [CpuPower] = SensorKind.Power,
            [CpuFan] = SensorKind.Fan,
            [CpuName] = SensorKind.Text,
            [GpuTemperature] = SensorKind.Temperature,
            [GpuLoad] = SensorKind.Load,
            [GpuClock] = SensorKind.Clock,
            [GpuPower] = SensorKind.Power,
            [GpuFan] = SensorKind.Fan,
            [GpuMemoryUsed] = SensorKind.Data,
            [GpuMemoryTotal] = SensorKind.Data,
            [GpuMemoryLoad] = SensorKind.Load,
            [GpuName] = SensorKind.Text,
            [MemoryUsed] = SensorKind.Data,
            [MemoryTotal] = SensorKind.Data,
            [MemoryLoad] = SensorKind.Load,
            [NetworkDown] = SensorKind.Throughput,
            [NetworkUp] = SensorKind.Throughput,
            [DiskLoad] = SensorKind.Load,
            [DiskTemperature] = SensorKind.Temperature,
            [DiskUsed] = SensorKind.Load,
            [DiskFree] = SensorKind.Data,
            [Uptime] = SensorKind.Duration,
            [HostName] = SensorKind.Text,
            [Time] = SensorKind.Text,
            [Date] = SensorKind.Text,
            [OperatingSystem] = SensorKind.Text,
            [PanelFps] = SensorKind.FrameRate,
            [DiskRead] = SensorKind.Throughput,
            [DiskWrite] = SensorKind.Throughput,
            [GpuVideo] = SensorKind.Load,
            [GameFps] = SensorKind.FrameRate,
            [GameFrameTime] = SensorKind.Unknown,
            [GameLow1] = SensorKind.FrameRate,
            [GameName] = SensorKind.Text,
            [CpuTemperaturePeak] = SensorKind.Temperature,
            [GpuTemperaturePeak] = SensorKind.Temperature,
            [NetworkAddress] = SensorKind.Text,
            [Battery] = SensorKind.Load,
            [AudioDevice] = SensorKind.Text,
            [AudioVolume] = SensorKind.Load,
            [MediaTitle] = SensorKind.Text,
            [MediaArtist] = SensorKind.Text,
            [MediaAlbum] = SensorKind.Text,
        };

        for (var n = 1; n <= MaxCoreAliases; n++)
        {
            all[CpuCore(n)] = SensorKind.Load;
        }

        return all;
    }

    /// <summary>Whether <paramref name="binding"/> names an alias rather than a raw id.</summary>
    public static bool IsAlias(string? binding) =>
        !string.IsNullOrEmpty(binding) && binding[0] == AliasPrefix;
}
