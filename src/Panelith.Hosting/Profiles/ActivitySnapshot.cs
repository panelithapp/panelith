namespace Panelith.Hosting.Profiles;

/// <summary>A running program.</summary>
/// <param name="Id">The process id.</param>
/// <param name="Name">The executable name without ".exe".</param>
/// <param name="Path">The full executable path, when Windows lets us read it.</param>
public sealed record ProcessInfo(int Id, string Name, string? Path);

/// <summary>What the PC is doing at one moment, as far as profile triggers care.</summary>
public sealed record ActivitySnapshot
{
    /// <summary>Local time.</summary>
    public DateTime Now { get; init; } = DateTime.Now;

    /// <summary>Every running program.</summary>
    public IReadOnlyList<ProcessInfo> Processes { get; init; } = [];

    /// <summary>The program that owns the foreground window.</summary>
    public ProcessInfo? Foreground { get; init; }

    /// <summary>Whether the foreground window covers its whole monitor.</summary>
    public bool ForegroundFullscreen { get; init; }

    /// <summary>Seconds since the last keyboard or mouse input.</summary>
    public double IdleSeconds { get; init; }

    /// <summary>Whether Windows is locked.</summary>
    public bool Locked { get; init; }

    /// <summary>The process RivaTuner is drawing frames for, if any.</summary>
    public int? RtssProcessId { get; init; }

    /// <summary>The GPU load in percent, if known.</summary>
    public double? GpuLoad { get; init; }

    /// <summary>Numeric sensor values, keyed by alias, for sensor triggers.</summary>
    public IReadOnlyDictionary<string, double> Sensors { get; init; } =
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
}
