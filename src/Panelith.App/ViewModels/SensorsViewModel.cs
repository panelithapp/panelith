using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Panelith.App.Services;
using Panelith.Expressions;
using Panelith.Sensors;

namespace Panelith.App.ViewModels;

/// <summary>One choice in the sensor pin list. A null id means the automatic pick.</summary>
public sealed record SensorPin(string? Id, string Label);

/// <summary>One sensor or alias in the browser.</summary>
public sealed partial class SensorRowViewModel(string key, string name, string reference, string source) : ObservableObject
{
    public string Key { get; } = key;

    /// <summary>What to call it.</summary>
    public string Name { get; } = name;

    /// <summary>What a theme writes to use it: an alias like <c>@cpu.temp</c> or a bracketed id.</summary>
    public string Reference { get; } = reference;

    /// <summary>Where the value comes from. Aliases resolve a moment after start-up, so this can change.</summary>
    [ObservableProperty]
    public partial string Source { get; set; } = source;

    [ObservableProperty]
    public partial string Value { get; set; } = "--";

    [ObservableProperty]
    public partial bool HasValue { get; set; }

    internal bool Matches(string filter) =>
        filter.Length == 0
        || Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || Reference.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || Source.Contains(filter, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Every value Panelith can put on a screen.</summary>
public sealed partial class SensorsViewModel(PanelithService service) : ObservableObject
{
    private readonly Dictionary<string, SensorRowViewModel> _all = new(StringComparer.Ordinal);

    /// <summary>The well-known values themes use, resolved to this machine's sensors.</summary>
    public ObservableCollection<SensorRowViewModel> Aliases { get; } = [];

    /// <summary>Every raw sensor, filtered.</summary>
    public ObservableCollection<SensorRowViewModel> Sensors { get; } = [];

    /// <summary>Theme names the chooser can pin.</summary>
    public ObservableCollection<string> AliasNames { get; } = [];

    /// <summary>Raw sensors of the same kind as <see cref="ChosenAlias"/>, plus Automatic.</summary>
    public ObservableCollection<SensorPin> PinChoices { get; } = [];

    [ObservableProperty]
    public partial string? ChosenAlias { get; set; }

    [ObservableProperty]
    public partial SensorPin? ChosenPin { get; set; }

    private bool _applyingPin;
    private string _pinSignature = string.Empty;

    [ObservableProperty]
    public partial string Filter { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Summary { get; set; } = string.Empty;

    /// <summary>Whether CPU sensors are missing for want of administrator rights.</summary>
    [ObservableProperty]
    public partial bool NeedsElevation { get; set; }

    partial void OnFilterChanged(string value) => ApplyFilter();

    partial void OnChosenAliasChanged(string? value)
    {
        _pinSignature = string.Empty;
        RebuildPins();
    }

    partial void OnChosenPinChanged(SensorPin? value)
    {
        if (_applyingPin || value is null || ChosenAlias is null)
        {
            return;
        }

        service.SetAliasOverride(ChosenAlias, value.Id);
    }

    /// <summary>Rereads every value. Called about once a second while the page is visible.</summary>
    public void Refresh()
    {
        var hub = service.Runtime.Sensors;
        var snapshot = hub.Current;
        var structureChanged = false;

        foreach (var alias in WellKnownSensors.All.Keys.Order(StringComparer.Ordinal))
        {
            var key = "alias:" + alias;
            var resolved = hub.Aliases.TryGetValue(alias, out var id);
            if (!resolved && WellKnownSensors.CoreNumber(alias) is not null)
            {
                // Listing 32 core slots on an 8-core CPU is noise.
                continue;
            }

            if (!_all.TryGetValue(key, out var row))
            {
                row = new SensorRowViewModel(key, Describe(alias), alias, resolved ? id.Value : "not on this machine");
                _all[key] = row;
                structureChanged = true;
            }

            row.Source = resolved ? id.Value : "not on this machine";
            var reading = resolved && snapshot.TryGet(id, out var r) ? r : (SensorReading?)null;
            row.HasValue = reading is { HasValue: true };
            row.Value = reading is { HasValue: true } value ? Format(value) : "--";
        }

        foreach (var reading in snapshot)
        {
            var key = "id:" + reading.Id.Value;
            if (!_all.TryGetValue(key, out var row))
            {
                row = new SensorRowViewModel(key, reading.DisplayName, $"[{reading.Id.Value}]", reading.SourceId ?? string.Empty);
                _all[key] = row;
                structureChanged = true;
            }

            row.HasValue = reading.HasValue;
            row.Value = reading.HasValue ? Format(reading) : "--";
        }

        NeedsElevation = WindowsIntegration.IsSupported
            && !WindowsIntegration.IsElevated
            && SensorAccess.Read(hub, WellKnownSensors.CpuTemperature) is null;

        var available = _all.Values.Count(r => r.Key.StartsWith("id:", StringComparison.Ordinal) && r.HasValue);
        Summary = $"{available} live sensors from {snapshot.Select(r => r.SourceId).Distinct().Count()} sources";

        if (structureChanged)
        {
            ApplyFilter();
            SyncAliasNames();
        }

        RebuildPins();
    }

    private void SyncAliasNames()
    {
        var names = _all.Values
            .Where(r => r.Key.StartsWith("alias:", StringComparison.Ordinal))
            .Select(r => r.Reference)
            .Order(StringComparer.Ordinal)
            .ToList();
        if (AliasNames.SequenceEqual(names))
        {
            return;
        }

        var keep = ChosenAlias;
        AliasNames.Clear();
        foreach (var name in names)
        {
            AliasNames.Add(name);
        }

        if (keep is not null && AliasNames.Contains(keep))
        {
            ChosenAlias = keep;
        }
    }

    private void RebuildPins()
    {
        if (ChosenAlias is null || !WellKnownSensors.All.TryGetValue(ChosenAlias, out var kind))
        {
            if (PinChoices.Count > 0)
            {
                _applyingPin = true;
                PinChoices.Clear();
                ChosenPin = null;
                _applyingPin = false;
            }

            _pinSignature = string.Empty;
            return;
        }

        var readings = service.Runtime.Sensors.Current
            .Where(r => r.Kind == kind)
            .OrderBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Id.Value, StringComparer.Ordinal)
            .ToList();
        var signature = kind + "|" + string.Join("|", readings.Select(r => r.Id.Value));
        var pinned = service.Options.Sensors.AliasOverrides is { } map && map.TryGetValue(ChosenAlias, out var id)
            ? id
            : null;
        if (signature == _pinSignature)
        {
            return;
        }

        _pinSignature = signature;
        _applyingPin = true;
        PinChoices.Clear();
        PinChoices.Add(new SensorPin(null, "Automatic"));
        foreach (var reading in readings)
        {
            PinChoices.Add(new SensorPin(reading.Id.Value, $"{reading.DisplayName}  [{reading.Id.Value}]"));
        }

        ChosenPin = PinChoices.FirstOrDefault(p => string.Equals(p.Id, pinned, StringComparison.OrdinalIgnoreCase)) ?? PinChoices[0];
        _applyingPin = false;
    }

    private void ApplyFilter()
    {
        var filter = Filter.Trim();
        Sync(Aliases, _all.Values.Where(r => r.Key.StartsWith("alias:", StringComparison.Ordinal) && r.Matches(filter)).OrderBy(r => r.Reference, StringComparer.Ordinal));
        Sync(Sensors, _all.Values.Where(r => r.Key.StartsWith("id:", StringComparison.Ordinal) && r.Matches(filter)).OrderBy(r => r.Reference, StringComparer.Ordinal));
    }

    private static void Sync(ObservableCollection<SensorRowViewModel> target, IEnumerable<SensorRowViewModel> rows)
    {
        var wanted = rows.ToList();
        if (target.SequenceEqual(wanted))
        {
            return;
        }

        target.Clear();
        foreach (var row in wanted)
        {
            target.Add(row);
        }
    }

    private static string Format(SensorReading reading) =>
        reading.Kind == SensorKind.Text ? reading.Text ?? "--" : ValueFormatter.FormatWithUnit(reading.Value!.Value, reading.Kind);

    private static string Describe(string alias) => alias switch
    {
        WellKnownSensors.CpuTemperature => "CPU temperature",
        WellKnownSensors.CpuLoad => "CPU load",
        WellKnownSensors.CpuClock => "CPU clock",
        WellKnownSensors.CpuPower => "CPU power",
        WellKnownSensors.CpuFan => "CPU fan",
        WellKnownSensors.CpuName => "CPU model",
        WellKnownSensors.GpuTemperature => "GPU temperature",
        WellKnownSensors.GpuLoad => "GPU load",
        WellKnownSensors.GpuClock => "GPU clock",
        WellKnownSensors.GpuPower => "GPU power",
        WellKnownSensors.GpuFan => "GPU fan",
        WellKnownSensors.GpuMemoryUsed => "GPU memory used",
        WellKnownSensors.GpuMemoryTotal => "GPU memory total",
        WellKnownSensors.GpuMemoryLoad => "GPU memory load",
        WellKnownSensors.GpuName => "GPU model",
        WellKnownSensors.MemoryUsed => "Memory used",
        WellKnownSensors.MemoryTotal => "Memory total",
        WellKnownSensors.MemoryLoad => "Memory load",
        WellKnownSensors.NetworkDown => "Download",
        WellKnownSensors.NetworkUp => "Upload",
        WellKnownSensors.DiskLoad => "Disk activity",
        WellKnownSensors.DiskTemperature => "Disk temperature",
        WellKnownSensors.DiskUsed => "System drive used",
        WellKnownSensors.DiskFree => "System drive free",
        WellKnownSensors.Uptime => "Uptime",
        WellKnownSensors.HostName => "Computer name",
        WellKnownSensors.Time => "Time",
        WellKnownSensors.Date => "Date",
        WellKnownSensors.OperatingSystem => "Operating system",
        WellKnownSensors.PanelFps => "Panel frame rate",
        WellKnownSensors.DiskRead => "Disk read",
        WellKnownSensors.DiskWrite => "Disk write",
        WellKnownSensors.GpuVideo => "GPU video engine",
        WellKnownSensors.GameFps => "Game FPS (RivaTuner)",
        WellKnownSensors.GameFrameTime => "Game frame time, ms",
        WellKnownSensors.GameLow1 => "Game 1% low",
        WellKnownSensors.GameName => "Game",
        WellKnownSensors.CpuTemperaturePeak => "Highest CPU temperature since start",
        WellKnownSensors.GpuTemperaturePeak => "Highest GPU temperature since start",
        WellKnownSensors.NetworkAddress => "IPv4 address",
        WellKnownSensors.Battery => "Battery",
        WellKnownSensors.AudioDevice => "Playback device",
        WellKnownSensors.AudioVolume => "Volume",
        WellKnownSensors.MediaTitle => "Now playing",
        WellKnownSensors.MediaArtist => "Artist",
        WellKnownSensors.MediaAlbum => "Album",
        _ when WellKnownSensors.CoreNumber(alias) is { } core => $"CPU core {core} load",
        _ => alias,
    };
}
