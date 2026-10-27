using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Panelith.Sensors;

namespace Panelith.Monitoring.Providers;

/// <summary>
/// Clock, date, host, uptime, network throughput, system-drive usage and memory
/// from the operating system's own APIs. None of it needs elevation.
/// </summary>
/// <remarks>
/// On Linux the hwmon and /proc sources already report the machine figures, so
/// only the clock, date and OS name are produced there; reporting them twice
/// would give every alias two candidates that always agree.
/// </remarks>
public sealed partial class SystemInfoSensorSource : ISensorSource
{
    private static readonly string[] VirtualAdapterMarkers =
    [
        "virtual", "hyper-v", "vethernet", "vswitch", "wsl", "vmware", "virtualbox",
        "loopback", "bluetooth", "pseudo", "teredo", "isatap", "kernel debug",
    ];

    private readonly bool _machineStats;
    private readonly Dictionary<string, (long Received, long Sent)> _previousCounters = new(StringComparer.Ordinal);
    private readonly string _host = Environment.MachineName;
    private readonly string _os = DescribeOperatingSystem();
    private DateTimeOffset _previousSampleAt;
    private IDisposable? _diskCounters;
    private bool _diskCountersFailed;

    /// <summary>Creates the source.</summary>
    /// <param name="machineStats">
    /// Also report host, uptime, network, drive and memory figures. Defaults to
    /// every platform except Linux.
    /// </param>
    public SystemInfoSensorSource(bool? machineStats = null) =>
        _machineStats = machineStats ?? !OperatingSystem.IsLinux();

    /// <inheritdoc />
    public string Id => "system";

    /// <inheritdoc />
    public string DisplayName => "System";

    /// <inheritdoc />
    public bool IsAvailable => true;

    /// <summary>The text <c>@sys.time</c> shows.</summary>
    public static string FormatTime(DateTime now) => now.ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>The text <c>@sys.date</c> shows.</summary>
    public static string FormatDate(DateTime now) => now.ToString("ddd d MMM", CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public ValueTask InitializeAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<SensorReading>> ReadAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.Now;
        var readings = new List<SensorReading>(16)
        {
            SensorReading.Textual(SensorId.From("system", "0", "clock", "time"), "Time", FormatTime(now), Id),
            SensorReading.Textual(SensorId.From("system", "0", "clock", "date"), "Date", FormatDate(now), Id),
            SensorReading.Textual(SensorId.From("system", "0", "name", "os"), "Operating System", _os, Id),
        };

        if (_machineStats)
        {
            readings.Add(SensorReading.Textual(SensorId.From("system", "0", "name", "host"), "Host Name", _host, Id));
            readings.Add(SensorReading.Numeric(
                SensorId.From("system", "0", "duration", "uptime"),
                "Uptime",
                SensorKind.Duration,
                Environment.TickCount64 / 1000d,
                sourceId: Id));

            AddNetwork(readings);
            AddSystemDrive(readings);
            if (OperatingSystem.IsWindows())
            {
                AddWindowsMemory(readings);
                AddWindowsDisks(readings);
            }
        }

        return ValueTask.FromResult<IReadOnlyList<SensorReading>>(readings);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _diskCounters?.Dispose();
        _diskCounters = null;
        return ValueTask.CompletedTask;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private void AddWindowsDisks(List<SensorReading> readings)
    {
        if (_diskCounters is null && !_diskCountersFailed)
        {
            _diskCounters = WindowsDiskCounters.TryOpen();
            _diskCountersFailed = _diskCounters is null;
        }

        (_diskCounters as WindowsDiskCounters)?.AddReadings(readings, Id);
    }

    private void AddNetwork(List<SensorReading> readings)
    {
        NetworkInterface[] interfaces;
        try
        {
            interfaces = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (NetworkInformationException)
        {
            return;
        }

        var sampledAt = DateTimeOffset.UtcNow;
        var elapsed = (sampledAt - _previousSampleAt).TotalSeconds;
        var haveRate = _previousSampleAt != default && elapsed > 0.05d;
        double down = 0, up = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var adapter in interfaces)
        {
            if (!IsPhysicalAdapter(adapter))
            {
                continue;
            }

            IPInterfaceStatistics stats;
            try
            {
                stats = adapter.GetIPStatistics();
            }
            catch (NetworkInformationException)
            {
                continue;
            }

            seen.Add(adapter.Id);
            var counters = (stats.BytesReceived, stats.BytesSent);
            if (haveRate && _previousCounters.TryGetValue(adapter.Id, out var previous))
            {
                down += Math.Max(0, counters.BytesReceived - previous.Received) / elapsed;
                up += Math.Max(0, counters.BytesSent - previous.Sent) / elapsed;
            }

            _previousCounters[adapter.Id] = counters;
        }

        foreach (var gone in _previousCounters.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            _previousCounters.Remove(gone);
        }

        _previousSampleAt = sampledAt;
        double? downRate = haveRate ? down : null;
        double? upRate = haveRate ? up : null;
        readings.Add(SensorReading.Numeric(SensorId.From("network", "0", "throughput", "down"), "Network Download", SensorKind.Throughput, downRate, 0d, null, Id));
        readings.Add(SensorReading.Numeric(SensorId.From("network", "0", "throughput", "up"), "Network Upload", SensorKind.Throughput, upRate, 0d, null, Id));
        readings.Add(SensorReading.Textual(SensorId.From("network", "0", "address", "ipv4"), "IPv4 address", FirstIPv4(interfaces) ?? string.Empty, Id));
    }

    private static string? FirstIPv4(IEnumerable<NetworkInterface> interfaces)
    {
        foreach (var adapter in interfaces)
        {
            if (!IsPhysicalAdapter(adapter))
            {
                continue;
            }

            IPInterfaceProperties properties;
            try
            {
                properties = adapter.GetIPProperties();
            }
            catch (NetworkInformationException)
            {
                continue;
            }

            foreach (var address in properties.UnicastAddresses)
            {
                if (address.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address.Address))
                {
                    return address.Address.ToString();
                }
            }
        }

        return null;
    }

    private static bool IsPhysicalAdapter(NetworkInterface adapter)
    {
        if (adapter.OperationalStatus != OperationalStatus.Up
            || adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel or NetworkInterfaceType.Unknown)
        {
            return false;
        }

        var label = adapter.Name + " " + adapter.Description;
        foreach (var marker in VirtualAdapterMarkers)
        {
            if (label.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private void AddSystemDrive(List<SensorReading> readings)
    {
        try
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory);
            if (string.IsNullOrEmpty(root))
            {
                root = "/";
            }

            var drive = new DriveInfo(root);
            if (!drive.IsReady || drive.TotalSize <= 0)
            {
                return;
            }

            double total = drive.TotalSize;
            double free = drive.AvailableFreeSpace;
            var used = total - free;
            readings.Add(SensorReading.Numeric(SensorId.From("volume", "0", "data", "used"), "System Drive Used", SensorKind.Data, used, 0d, total, Id));
            readings.Add(SensorReading.Numeric(SensorId.From("volume", "0", "data", "free"), "System Drive Free", SensorKind.Data, free, 0d, total, Id));
            readings.Add(SensorReading.Numeric(SensorId.From("volume", "0", "data", "total"), "System Drive Total", SensorKind.Data, total, sourceId: Id));
            readings.Add(SensorReading.Numeric(SensorId.From("volume", "0", "load", "used"), "System Drive Usage", SensorKind.Load, used / total * 100d, 0d, 100d, Id));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // A drive that cannot be queried this tick simply has no reading.
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private void AddWindowsMemory(List<SensorReading> readings)
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status) || status.TotalPhysical == 0)
        {
            return;
        }

        double total = status.TotalPhysical;
        double used = status.TotalPhysical - status.AvailablePhysical;
        readings.Add(SensorReading.Numeric(SensorId.From("memory", "0", "data", "used"), "Memory Used", SensorKind.Data, used, 0d, total, Id));
        readings.Add(SensorReading.Numeric(SensorId.From("memory", "0", "data", "total"), "Memory Total", SensorKind.Data, total, sourceId: Id));
        readings.Add(SensorReading.Numeric(SensorId.From("memory", "0", "load", "used"), "Memory Load", SensorKind.Load, used / total * 100d, 0d, 100d, Id));
    }

    private static string DescribeOperatingSystem()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                if (key?.GetValue("ProductName") is string product && product.Length > 0)
                {
                    // Windows 11 still reports "Windows 10" here; the build number is what tells them apart.
                    return Environment.OSVersion.Version.Build >= 22000
                        ? product.Replace("Windows 10", "Windows 11", StringComparison.Ordinal)
                        : product;
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                // Fall through to the runtime's description.
            }
        }

        return RuntimeInformation.OSDescription;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
