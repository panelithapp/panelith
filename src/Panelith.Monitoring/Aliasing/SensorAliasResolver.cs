using System.Collections.Frozen;
using Panelith.Monitoring.Hub;
using Panelith.Monitoring.Providers;
using Panelith.Sensors;

namespace Panelith.Monitoring.Aliasing;

/// <summary>
/// Declarative mapping from raw backend sensors onto
/// <see cref="WellKnownSensors"/> aliases. Priority rules make
/// <c>@cpu.temp</c> prefer package/Tctl over per-core maxima over ACPI, and
/// resolution is deterministic so a theme cannot silently repoint after reboot.
/// </summary>
public sealed class SensorAliasResolver
{
    private readonly Dictionary<string, SensorId> _overrides =
        new(StringComparer.OrdinalIgnoreCase);

    private FrozenDictionary<string, SensorId> _lastResolved =
        FrozenDictionary<string, SensorId>.Empty;

    /// <summary>
    /// User-supplied overrides. An override always wins over the built-in
    /// priority rules so an odd motherboard can repoint <c>@cpu.temp</c>.
    /// </summary>
    public IReadOnlyDictionary<string, SensorId> Overrides => _overrides;

    /// <summary>The mapping produced by the most recent <see cref="Resolve"/>.</summary>
    public IReadOnlyDictionary<string, SensorId> LastResolved => _lastResolved;

    /// <summary>Pins <paramref name="alias"/> to a concrete sensor id.</summary>
    public void SetOverride(string alias, SensorId target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);
        if (!WellKnownSensors.IsAlias(alias))
        {
            throw new ArgumentException("Overrides must use an @alias name.", nameof(alias));
        }

        if (target.IsEmpty)
        {
            throw new ArgumentException("Override target must be a non-empty sensor id.", nameof(target));
        }

        _overrides[alias] = target;
    }

    /// <summary>Removes a previously set override.</summary>
    public bool ClearOverride(string alias) => _overrides.Remove(alias);

    /// <summary>
    /// Resolves every well-known alias against <paramref name="readings"/>.
    /// Missing aliases are simply omitted from the result.
    /// </summary>
    public IReadOnlyDictionary<string, SensorId> Resolve(IEnumerable<SensorReading> readings)
    {
        ArgumentNullException.ThrowIfNull(readings);
        var list = readings as IList<SensorReading> ?? readings.ToList();
        var map = new Dictionary<string, SensorId>(StringComparer.OrdinalIgnoreCase);

        foreach (var alias in WellKnownSensors.All.Keys.OrderBy(static a => a, StringComparer.Ordinal))
        {
            if (_overrides.TryGetValue(alias, out var forced))
            {
                // Overrides win even when the target is currently absent — the
                // binding context then returns Missing, which is what the user
                // asked for by pinning that id.
                map[alias] = forced;
                continue;
            }

            var match = FindBest(alias, list);
            if (match is { } id)
            {
                map[alias] = id;
            }
        }

        _lastResolved = map.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        return _lastResolved;
    }

    /// <summary>Looks up a single alias against the last resolved map, or resolves fresh.</summary>
    public bool TryResolve(string alias, IEnumerable<SensorReading> readings, out SensorId id)
    {
        if (_overrides.TryGetValue(alias, out id))
        {
            return true;
        }

        if (_lastResolved.TryGetValue(alias, out id))
        {
            return true;
        }

        var resolved = Resolve(readings);
        return resolved.TryGetValue(alias, out id);
    }

    private static SensorId? FindBest(string alias, IList<SensorReading> readings)
    {
        SensorId? best = null;
        var bestScore = int.MinValue;
        var bestHasValue = false;

        foreach (var reading in readings)
        {
            var score = Score(alias, reading);
            if (score < 0)
            {
                continue;
            }

            // A sensor that is reading beats a better-named one that is locked,
            // such as a CPU package temperature that needs elevation.
            var hasValue = reading.HasValue;
            if (best is null
                || (hasValue && !bestHasValue)
                || (hasValue == bestHasValue && score > bestScore)
                || (hasValue == bestHasValue && score == bestScore && best is { } current && reading.Id.CompareTo(current) < 0))
            {
                // Tie-break on ordinal id so the same machine always picks the
                // same sensor across restarts when scores are equal.
                bestScore = score;
                bestHasValue = hasValue;
                best = reading.Id;
            }
        }

        return best;
    }

    private static int Score(string alias, SensorReading reading)
    {
        var id = reading.Id.Value;
        var name = reading.DisplayName;
        var kind = reading.Kind;

        // Synthetic source: exact path match under synthetic/.
        var syntheticId = SyntheticSensorSource.IdForAlias(alias);
        if (id.Equals(syntheticId.Value, StringComparison.Ordinal))
        {
            return 10_000;
        }

        if (WellKnownSensors.CoreNumber(alias) is { } core)
        {
            return ScoreCpuCore(id, kind, core);
        }

        return alias switch
        {
            WellKnownSensors.CpuTemperature => id.StartsWith("peak/", StringComparison.Ordinal) ? -1 : ScoreCpuTemp(id, name, kind),
            WellKnownSensors.CpuLoad => ScoreCpuLoad(id, name, kind),
            WellKnownSensors.CpuClock => ScoreCpuClock(id, name, kind),
            WellKnownSensors.CpuPower => ScoreCpuPower(id, name, kind),
            WellKnownSensors.CpuFan => ScoreSimple(id, name, kind, SensorKind.Fan, "cpu", ["cpu", "processor"], exclude: ["gpu"]),
            WellKnownSensors.CpuName => kind == SensorKind.Text && id.StartsWith("cpu/", StringComparison.Ordinal) ? 100 : -1,
            WellKnownSensors.GpuTemperature => id.StartsWith("peak/", StringComparison.Ordinal)
                ? -1
                : ScoreGpu(id, name, kind, SensorKind.Temperature, ["core", "edge", "gpu_temperature", "temperature"], exclude: ["memory", "vr_", "soc", "junction", "hot_spot", "hotspot"]),
            WellKnownSensors.GpuLoad => ScoreGpu(id, name, kind, SensorKind.Load, ["core", "util", "graphics"], exclude: ["memory", "bus", "power", "board", "video", "controller", "copy", "encode", "decode", "jpeg", "security", "compute", "timer", "optical", "vr"]),
            WellKnownSensors.GpuClock => ScoreGpu(id, name, kind, SensorKind.Clock, ["core", "graphics", "shader"], exclude: ["memory", "soc", "video"]),
            WellKnownSensors.GpuPower => ScoreGpu(id, name, kind, SensorKind.Power, ["package", "board", "total", "ppt"], exclude: ["soc", "memory"]),
            WellKnownSensors.GpuFan => ScoreGpu(id, name, kind, SensorKind.Fan, ["fan"]),
            WellKnownSensors.GpuMemoryUsed => ScoreGpu(id, name, kind, SensorKind.Data, ["memory_used", "vram_used", "memory used"], exclude: ["free", "total", "shared"], requireKeyword: true),
            WellKnownSensors.GpuMemoryTotal => ScoreGpu(id, name, kind, SensorKind.Data, ["memory_total", "vram_total", "memory total"], exclude: ["free", "used", "shared"], requireKeyword: true),
            WellKnownSensors.GpuMemoryLoad => ScoreGpu(id, name, kind, SensorKind.Load, ["memory", "vram"], exclude: ["controller", "bus"], requireKeyword: true),
            WellKnownSensors.GpuName => kind == SensorKind.Text && id.StartsWith("gpu/", StringComparison.Ordinal) ? ScoreGpuName(id, name) : -1,
            WellKnownSensors.MemoryUsed => ScoreMem(id, name, kind, SensorKind.Data, ["used"], reject: ["swap", "vram", "virtual", "available", "free"]),
            WellKnownSensors.MemoryTotal => ScoreMem(id, name, kind, SensorKind.Data, ["total"], reject: ["swap", "vram", "virtual", "available", "free"]),
            WellKnownSensors.MemoryLoad => ScoreMem(id, name, kind, SensorKind.Load, ["load", "used", "memory"], reject: ["swap", "virtual"]),
            WellKnownSensors.DiskUsed => kind == SensorKind.Load && id.StartsWith("volume/", StringComparison.Ordinal) && id.Contains("used", StringComparison.Ordinal)
                ? (id.StartsWith("volume/0/", StringComparison.Ordinal) ? 100 : 50)
                : -1,
            WellKnownSensors.DiskFree => kind == SensorKind.Data && id.StartsWith("volume/", StringComparison.Ordinal) && id.Contains("free", StringComparison.Ordinal)
                ? (id.StartsWith("volume/0/", StringComparison.Ordinal) ? 100 : 50)
                : -1,
            WellKnownSensors.Time => kind == SensorKind.Text && id.EndsWith("clock/time", StringComparison.Ordinal) ? 100 : -1,
            WellKnownSensors.Date => kind == SensorKind.Text && id.EndsWith("clock/date", StringComparison.Ordinal) ? 100 : -1,
            WellKnownSensors.OperatingSystem => kind == SensorKind.Text && id.EndsWith("name/os", StringComparison.Ordinal) ? 100 : -1,
            WellKnownSensors.NetworkDown => ScoreNet(id, name, kind, down: true),
            WellKnownSensors.NetworkUp => ScoreNet(id, name, kind, down: false),
            WellKnownSensors.DiskLoad => kind == SensorKind.Load && id.Contains("disk", StringComparison.Ordinal)
                ? (id.Contains("/0/", StringComparison.Ordinal) ? 80 : 40)
                : -1,
            WellKnownSensors.DiskTemperature => kind == SensorKind.Temperature && (id.StartsWith("disk/", StringComparison.Ordinal) || ContainsAny(name, ["drive", "nvme", "ssd", "hdd"]))
                ? 50 + (ContainsAny(name, ["composite", "sensor 1"]) ? 20 : 0)
                : -1,
            WellKnownSensors.Uptime => kind == SensorKind.Duration && (id.Contains("uptime", StringComparison.Ordinal) || ContainsAny(name, ["uptime"]))
                ? 100
                : -1,
            WellKnownSensors.HostName => kind == SensorKind.Text && (id.Contains("host", StringComparison.Ordinal) || ContainsAny(name, ["host"]))
                ? 100
                : -1,
            WellKnownSensors.PanelFps => kind == SensorKind.FrameRate && id.StartsWith("panel/", StringComparison.Ordinal)
                ? 100
                : -1,
            WellKnownSensors.DiskRead => ScoreDiskRate(id, name, kind, read: true),
            WellKnownSensors.DiskWrite => ScoreDiskRate(id, name, kind, read: false),
            WellKnownSensors.GpuVideo => ScoreGpuVideo(id, name, kind),
            WellKnownSensors.GameFps => kind == SensorKind.FrameRate && id.Equals(RtssSensorSource.FpsId.Value, StringComparison.Ordinal) ? 100 : -1,
            WellKnownSensors.GameLow1 => kind == SensorKind.FrameRate && id.Equals(RtssSensorSource.Low1Id.Value, StringComparison.Ordinal) ? 100 : -1,
            WellKnownSensors.GameFrameTime => id.Equals(RtssSensorSource.FrameTimeId.Value, StringComparison.Ordinal) ? 100 : -1,
            WellKnownSensors.GameName => kind == SensorKind.Text && id.Equals(RtssSensorSource.NameId.Value, StringComparison.Ordinal) ? 100 : -1,
            WellKnownSensors.CpuTemperaturePeak => id.Equals(PeakTracker.CpuId.Value, StringComparison.Ordinal) ? 20_000 : -1,
            WellKnownSensors.GpuTemperaturePeak => id.Equals(PeakTracker.GpuId.Value, StringComparison.Ordinal) ? 20_000 : -1,
            WellKnownSensors.NetworkAddress => kind == SensorKind.Text && id.EndsWith("address/ipv4", StringComparison.Ordinal) ? 100 : -1,
            WellKnownSensors.Battery => ScoreBattery(id, name, kind),
            WellKnownSensors.AudioDevice => kind == SensorKind.Text && id.EndsWith("name/device", StringComparison.Ordinal) ? 100 : -1,
            WellKnownSensors.AudioVolume => kind == SensorKind.Load && id.EndsWith("load/volume", StringComparison.Ordinal) ? 100 : -1,
            WellKnownSensors.MediaTitle => kind == SensorKind.Text && id.EndsWith("text/title", StringComparison.Ordinal) ? 100 : -1,
            WellKnownSensors.MediaArtist => kind == SensorKind.Text && id.EndsWith("text/artist", StringComparison.Ordinal) ? 100 : -1,
            WellKnownSensors.MediaAlbum => kind == SensorKind.Text && id.EndsWith("text/album", StringComparison.Ordinal) ? 100 : -1,
            _ => -1,
        };
    }

    private static int ScoreBattery(string id, string name, SensorKind kind)
    {
        if (kind != SensorKind.Load || !id.Contains("battery", StringComparison.OrdinalIgnoreCase))
        {
            return -1;
        }

        var score = 40;
        if (id.Contains("capacity", StringComparison.OrdinalIgnoreCase) || ContainsAny(name, ["capacity", "charge", "level", "remaining"]))
        {
            score += 40;
        }

        return score;
    }

    private static int ScoreGpuVideo(string id, string name, SensorKind kind)
    {
        var score = ScoreGpu(id, name, kind, SensorKind.Load, ["video_engine", "video engine", "video_encode", "encode", "vcn", "uvd", "vce"], exclude: ["decode", "jpeg"], requireKeyword: true);
        return score >= 0 && id.Contains("video_engine", StringComparison.Ordinal) ? score + 30 : score;
    }

    private static int ScoreCpuCore(string id, SensorKind kind, int core)
    {
        if (kind != SensorKind.Load || !id.StartsWith("cpu/", StringComparison.Ordinal))
        {
            return -1;
        }

        var leaf = id[(id.LastIndexOf('/') + 1)..];
        var number = leaf.StartsWith("cpu_core_", StringComparison.Ordinal) ? leaf["cpu_core_".Length..]
            : leaf.StartsWith("core_", StringComparison.Ordinal) ? leaf["core_".Length..]
            : null;
        if (number is null && id.Contains("/core/", StringComparison.Ordinal))
        {
            number = leaf;
        }

        return number is not null
            && int.TryParse(number, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n)
            && n == core
            && id.StartsWith("cpu/0/", StringComparison.Ordinal)
                ? 100
                : -1;
    }

    private static int ScoreDiskRate(string id, string name, SensorKind kind, bool read)
    {
        if (kind != SensorKind.Throughput || !(id.StartsWith("disk/", StringComparison.Ordinal) || id.StartsWith("storage/", StringComparison.Ordinal)))
        {
            return -1;
        }

        var direction = read
            ? ContainsAny(id, ["read"]) || ContainsAny(name, ["read"])
            : ContainsAny(id, ["write"]) || ContainsAny(name, ["write"]);
        if (!direction)
        {
            return -1;
        }

        // The all-disk total beats any single drive.
        return id.StartsWith("disk/all/", StringComparison.Ordinal) ? 150
            : id.StartsWith("disk/0/", StringComparison.Ordinal) ? 100
            : 50;
    }

    private static int ScoreCpuTemp(string id, string name, SensorKind kind)
    {
        if (kind != SensorKind.Temperature)
        {
            return -1;
        }

        var inCpu = id.StartsWith("cpu/", StringComparison.Ordinal)
                    || ContainsAny(name, ["cpu", "ryzen", "tctl", "tdie", "package", "coretemp", "k10temp"]);
        var isAcpi = id.StartsWith("acpi/", StringComparison.Ordinal) || ContainsAny(name, ["acpitz", "acpi"]);
        if (!inCpu && !isAcpi)
        {
            return -1;
        }

        if (ContainsAny(id, ["package", "tctl", "tdie"]) || ContainsAny(name, ["package", "tctl", "tdie", "tctl/tdie"]))
        {
            return 300;
        }

        if (ContainsAny(name, ["cpu", "processor"]) && !ContainsAny(name, ["core"]))
        {
            return 250;
        }

        if (inCpu && ContainsAny(name, ["core", "ccd"]))
        {
            return 150;
        }

        if (inCpu)
        {
            return 100;
        }

        // acpitz is a last resort — often ambient or chipset, not the package.
        return isAcpi ? 10 : -1;
    }

    private static int ScoreCpuLoad(string id, string name, SensorKind kind)
    {
        if (kind != SensorKind.Load || !id.StartsWith("cpu/", StringComparison.Ordinal))
        {
            return -1;
        }

        if (id.Contains("/total", StringComparison.Ordinal) || ContainsAny(name, ["total", "cpu load"]))
        {
            return 100;
        }

        if (id.Contains("/core/", StringComparison.Ordinal))
        {
            return 20;
        }

        return 50;
    }

    private static int ScoreCpuClock(string id, string name, SensorKind kind)
    {
        if (kind != SensorKind.Clock || !id.StartsWith("cpu/", StringComparison.Ordinal)
            || ContainsAny(id, ["bus", "uncore", "memory", "fabric"]))
        {
            return -1;
        }

        var score = 50;
        if (ContainsAny(id, ["average", "max"]) || ContainsAny(name, ["average"]))
        {
            score += 50;
        }

        if (id.Contains("effective", StringComparison.Ordinal))
        {
            score -= 20;
        }

        _ = name;
        return score;
    }

    private static int ScoreCpuPower(string id, string name, SensorKind kind)
    {
        if (kind != SensorKind.Power || !(id.StartsWith("cpu/", StringComparison.Ordinal) || ContainsAny(name, ["cpu"])))
        {
            return -1;
        }

        if (ContainsAny(id, ["package", "ppt"]) || ContainsAny(name, ["package", "ppt"]))
        {
            return 100;
        }

        return ContainsAny(id, ["core"]) ? 20 : 50;
    }

    private static int ScoreGpu(
        string id,
        string name,
        SensorKind kind,
        SensorKind expected,
        string[] keywords,
        string[]? exclude = null,
        bool requireKeyword = false)
    {
        if (kind != expected)
        {
            return -1;
        }

        var isGpu = id.StartsWith("gpu/", StringComparison.Ordinal)
                    || ContainsAny(name, ["gpu", "geforce", "radeon", "nvidia", "amdgpu", "graphics"]);
        if (!isGpu)
        {
            return -1;
        }

        // Exclusions look at the id only: hardware names such as
        // "NVIDIA GeForce RTX 5070 Ti" are shared by every sensor on the card.
        if (exclude is not null && ContainsAny(id, exclude))
        {
            return -1;
        }

        var matched = ContainsAny(id, keywords) || ContainsAny(name, keywords);
        if (requireKeyword && !matched)
        {
            return -1;
        }

        var score = 50;
        if (matched)
        {
            score += 40;
        }

        if (id.Contains("d3d", StringComparison.Ordinal))
        {
            score -= 20;
        }

        if (id.StartsWith("gpu/0/", StringComparison.Ordinal))
        {
            score += 10;
        }

        // "AMD Radeon(TM) Graphics" is the integrated GPU on a Ryzen; it must not
        // outrank a discrete card just because its name says Radeon.
        if (ContainsAny(name, ["radeon(tm) graphics", "radeon graphics"]))
        {
            score -= 150;
        }

        // Discrete GPUs outrank integrated ones for the default aliases.
        if (ContainsAny(name, ["nvidia", "geforce", "radeon", "amdgpu", "rtx", "gtx", "rx "])
            || id.Contains("nvidia", StringComparison.Ordinal) || id.Contains("amd", StringComparison.Ordinal))
        {
            score += 100;
        }

        if (ContainsAny(name, ["intel", "uhd", "iris", "arc"]) && !ContainsAny(name, ["nvidia", "radeon"]))
        {
            score -= 50;
        }

        return score;
    }

    private static int ScoreGpuName(string id, string name)
    {
        var score = 50;
        if (ContainsAny(name, ["nvidia", "geforce", "radeon", "rtx", "gtx"]))
        {
            score += 100;
        }

        if (ContainsAny(name, ["intel", "uhd", "iris"]) && !ContainsAny(name, ["nvidia", "radeon"]))
        {
            score -= 40;
        }

        if (ContainsAny(name, ["radeon(tm) graphics", "radeon graphics"]))
        {
            score -= 150;
        }

        _ = id;
        return score;
    }

    private static int ScoreMem(string id, string name, SensorKind kind, SensorKind expected, string[] keywords, string[] reject)
    {
        if (kind != expected)
        {
            return -1;
        }

        // GPU memory sensors are named "GPU Memory ..." and must never stand in for system RAM.
        if (!id.StartsWith("memory/", StringComparison.Ordinal))
        {
            return -1;
        }

        if (ContainsAny(id, reject) || ContainsAny(name, reject))
        {
            return -1;
        }

        // Keywords are matched on the id alone: LHM names the RAM device
        // "Total Memory", which would make every one of its sensors look like a total.
        return ContainsAny(id[(id.IndexOf('/', "memory/".Length) + 1)..], keywords) ? 100 : 40;
    }

    private static int ScoreNet(string id, string name, SensorKind kind, bool down)
    {
        if (kind != SensorKind.Throughput)
        {
            return -1;
        }

        if (!id.StartsWith("network/", StringComparison.Ordinal) && !ContainsAny(name, ["network", "download", "upload", "nic"]))
        {
            return -1;
        }

        var direction = down
            ? (id.Contains("down", StringComparison.Ordinal) || ContainsAny(name, ["down", "download", "receive", "rx"]))
            : (id.Contains("up", StringComparison.Ordinal) || ContainsAny(name, ["up", "upload", "transmit", "tx"]));
        if (!direction)
        {
            return -1;
        }

        // Aggregate row wins over a specific interface.
        if (id.StartsWith("network/0/", StringComparison.Ordinal))
        {
            return 200;
        }

        // Virtual interfaces are excluded by the Linux source already, but
        // defend against other backends that do not filter.
        if (IsVirtualInterfaceId(id, name))
        {
            return -1;
        }

        if (ContainsAny(id, ["/eth", "/en", "/wl", "/wlan", "/wifi"]) || ContainsAny(name, ["ethernet", "wi-fi", "wifi", "wlan"]))
        {
            return 120;
        }

        return 60;
    }

    private static int ScoreSimple(
        string id,
        string name,
        SensorKind kind,
        SensorKind expected,
        string devicePrefix,
        string[] keywords,
        string[] exclude)
    {
        if (kind != expected)
        {
            return -1;
        }

        if (!id.StartsWith(devicePrefix + "/", StringComparison.Ordinal)
            && !ContainsAny(name, [devicePrefix]))
        {
            return -1;
        }

        if (ContainsAny(id, exclude) || ContainsAny(name, exclude))
        {
            return -1;
        }

        return ContainsAny(id, keywords) || ContainsAny(name, keywords) ? 100 : 40;
    }

    private static bool IsVirtualInterfaceId(string id, string name)
    {
        foreach (var token in new[] { "/lo", "docker", "veth", "br-", "virbr", "/tun", "/tap", "/wg", "vnet" })
        {
            if (id.Contains(token, StringComparison.OrdinalIgnoreCase)
                || name.Contains(token.Trim('/'), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsAny(string haystack, string[] needles)
    {
        foreach (var needle in needles)
        {
            if (haystack.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
