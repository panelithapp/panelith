using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using NetSparkleUpdater;
using NetSparkleUpdater.Configurations;
using NetSparkleUpdater.Enums;
using NetSparkleUpdater.SignatureVerifiers;
using Panelith.Engine;
using IAssemblyAccessor = NetSparkleUpdater.Interfaces.IAssemblyAccessor;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Panelith.App.Services;

/// <summary>
/// Finds, downloads and installs new versions through NetSparkle, the Windows
/// port of Sparkle. The feed and every installer are signed with Ed25519, and
/// nothing is installed unless both signatures check out against
/// <see cref="PublicKey"/>.
/// </summary>
public sealed partial class UpdateService : ObservableObject, IDisposable
{
    /// <summary>The signed appcast published with every GitHub release.</summary>
    public const string AppcastUrl = "https://github.com/panelithapp/panelith/releases/latest/download/appcast.xml";

    /// <summary>The key release builds sign the appcast and installers with. Rotating it strands older installs.</summary>
    private const string PublicKey = "8yKZfMbW4dFvM6oNzqkGC1Wqxs6Gax3somUOJp4CFO8=";

    /// <summary>Passed to the Inno Setup installer: no wizard, then start Panelith again.</summary>
    internal const string InstallerArguments = "/SILENT /SUPPRESSMSGBOXES /NORESTART /RELAUNCH=1";

    /// <summary>Win32 ERROR_CANCELLED. Windows returns it when the elevation prompt is declined.</summary>
    private const int ErrorCancelled = 1223;

    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    private readonly ILogger _logger;
    private readonly SparkleUpdater? _sparkle;
    private readonly DispatcherTimer _timer;
    private string? _downloadedInstaller;
    private AppCastItem? _installingItem;
    private bool _installing;

    public UpdateService(AppPaths paths, AppLog log, bool checkAutomatically)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(log);
        _logger = log.CreateLogger("Panelith.App.Updates");
        CurrentVersion = typeof(UpdateService).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        IsSupported = IsInstalledCopy();
        CheckAutomatically = checkAutomatically;
        _timer = new DispatcherTimer(FirstCheckDelay, DispatcherPriority.Background, (_, _) => OnTimer());

        if (!IsSupported)
        {
            Status = "Updates are installed through the Panelith installer. This copy was not installed with it, so it does not update itself.";
            return;
        }

        var accessor = new VersionAccessor(CurrentVersion);
        _sparkle = new SparkleUpdater(AppcastUrl, new Ed25519Checker(SecurityMode.Strict, PublicKey))
        {
            UIFactory = null,
            UserInteractionMode = UserInteractionMode.DownloadNoInstall,
            Configuration = new JSONConfiguration(accessor, Path.Combine(paths.Root, "updates.json")),
            RelaunchAfterUpdate = false,
            LogWriter = new LogAdapter(_logger),
            TmpDownloadFilePath = Path.Combine(Path.GetTempPath(), "Panelith", "updates"),
        };
        _sparkle.DownloadMadeProgress += (_, _, e) => Dispatcher.UIThread.Post(() => DownloadPercent = e.ProgressPercentage);
        _sparkle.DownloadFinished += (_, path) => Dispatcher.UIThread.Post(() => OnDownloaded(path));
        _sparkle.DownloadHadError += (_, _, ex) => Dispatcher.UIThread.Post(() => OnDownloadFailed(ex.Message));
        _sparkle.DownloadedFileIsCorrupt += (_, _) => Dispatcher.UIThread.Post(() => OnDownloadFailed("the download's signature did not match, so it was thrown away"));
        _sparkle.CloseApplicationAsync += () => QuitRequested?.Invoke() ?? Task.CompletedTask;

        // NetSparkle's own installer launch runs the installer through a plain
        // background script, which Windows silently refuses for an installer
        // that needs administrator rights: no prompt appears, nothing is
        // installed, and nothing is logged. Returning false here stops that
        // launch; LaunchElevatedInstallerAsync runs the installer properly
        // elevated instead, which does show the prompt.
        _sparkle.InstallerProcessAboutToStart += (notUsed, downloadFilePath) =>
        {
            _ = LaunchElevatedInstallerAsync(downloadFilePath);
            return false;
        };
        Status = "Panelith checks for updates in the background.";
        if (checkAutomatically)
        {
            _timer.Start();
        }
    }

    /// <summary>Raised once the installer is about to run; the app must exit so its files can be replaced.</summary>
    public event Func<Task>? QuitRequested;

    /// <summary>Raised on the UI thread when a newer version is found.</summary>
    public event EventHandler? UpdateFound;

    /// <summary>The version that is running.</summary>
    public string CurrentVersion { get; }

    /// <summary>Whether this copy can update itself: only installed copies can.</summary>
    public bool IsSupported { get; }

    [ObservableProperty]
    public partial bool CheckAutomatically { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    public partial string Status { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate), nameof(AvailableVersion), nameof(ReleaseNotesUrl))]
    public partial AppCastItem? Available { get; private set; }

    [ObservableProperty]
    public partial int DownloadPercent { get; private set; }

    public bool IsIdle => !IsBusy;

    public bool HasUpdate => Available is not null;

    public string? AvailableVersion => Available?.Version;

    public string ReleaseNotesUrl => Available is { } item
        ? $"https://github.com/panelithapp/panelith/releases/tag/v{item.Version}"
        : "https://github.com/panelithapp/panelith/releases/latest";

    partial void OnCheckAutomaticallyChanged(bool value)
    {
        if (_sparkle is null)
        {
            return;
        }

        if (value)
        {
            _timer.Interval = FirstCheckDelay;
            _timer.Start();
        }
        else
        {
            _timer.Stop();
        }
    }

    /// <summary>Asks the feed whether a newer version exists.</summary>
    public async Task CheckAsync(bool userAsked)
    {
        if (_sparkle is null || IsBusy)
        {
            return;
        }

        IsBusy = true;
        Status = "Checking for updates…";
        try
        {
            var info = await _sparkle.CheckForUpdatesQuietly(ignoreSkippedVersions: true);
            var newest = info.Status == UpdateStatus.UpdateAvailable
                ? info.Updates.Where(u => u.IsWindowsUpdate).OrderByDescending(u => u.SemVerLikeVersion).FirstOrDefault()
                : null;
            Available = newest;
            Status = info.Status switch
            {
                UpdateStatus.UpdateAvailable when newest is not null => $"Panelith {newest.Version} is available. You have {CurrentVersion}.",
                UpdateStatus.CouldNotDetermine => "The update server could not be reached, or its answer was not signed correctly. Panelith will try again later.",
                _ => $"Panelith {CurrentVersion} is the latest version. Last checked {DateTime.Now:t}.",
            };
            if (newest is not null)
            {
                _logger.LogInformation("Update available: {Version}", newest.Version);
                if (!userAsked)
                {
                    UpdateFound?.Invoke(this, EventArgs.Empty);
                }
            }
        }
        catch (Exception ex)
        {
            // A failed check must never take the app down; the next one may work.
            _logger.LogWarning("Could not check for updates: {Message}", ex.Message);
            Status = $"Could not check for updates: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Downloads the available version, checks its signature and runs its installer.</summary>
    public async Task InstallAsync()
    {
        if (_sparkle is null || Available is not { } item || IsBusy)
        {
            return;
        }

        if (_downloadedInstaller is { } ready && File.Exists(ready))
        {
            await RunInstallerAsync(item, ready);
            return;
        }

        IsBusy = true;
        DownloadPercent = 0;
        Status = $"Downloading Panelith {item.Version}…";
        try
        {
            await _sparkle.InitAndBeginDownload(item);
        }
        catch (Exception ex)
        {
            OnDownloadFailed(ex.Message);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _timer.Stop();
        if (!_installing)
        {
            // While installing, NetSparkle is the caller that asked the app to quit.
            _sparkle?.Dispose();
        }
    }

    private void OnTimer()
    {
        _timer.Interval = CheckInterval;
        _ = CheckAsync(userAsked: false);
    }

    private void OnDownloaded(string path)
    {
        _downloadedInstaller = path;
        if (Available is { } item)
        {
            _ = RunInstallerAsync(item, path);
        }
    }

    private async Task RunInstallerAsync(AppCastItem item, string path)
    {
        IsBusy = true;
        _installing = true;
        _installingItem = item;
        Status = $"Installing Panelith {item.Version}. Panelith closes and opens again when it is done.";
        _logger.LogInformation("Installing update {Version} from {Path}", item.Version, path);
        try
        {
            await _sparkle!.InstallUpdate(item, path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("The update could not be installed: {Message}", ex.Message);
            Status = $"The update could not be installed: {ex.Message}";
            _installing = false;
            IsBusy = false;
        }
    }

    /// <summary>
    /// Runs the downloaded installer with an explicit administrator request,
    /// since it needs to write to Program Files. Only Windows showing its own
    /// elevation prompt can grant that; a plain background process cannot.
    /// </summary>
    private async Task LaunchElevatedInstallerAsync(string installerPath)
    {
        var launchPath = installerPath;
        try
        {
            // The installer waits in a folder this account can write to, and the
            // prompt that follows grants administrator rights to whatever the file
            // holds by then. So hold it open against writers and deletion for as
            // long as it takes to check the signature and hand it to Windows:
            // nothing can swap the bytes in between. A file open for reading can
            // still be run.
            using var locked = new FileStream(installerPath, FileMode.Open, FileAccess.Read, FileShare.Read);

            // NetSparkle names the download with a bare id and no extension.
            // Asking Windows to run that looks the name up by extension, finds
            // nothing, and fails — which is not the permission prompt being
            // declined. A hard link is the same file, so the lock above still
            // covers the bytes that are about to run.
            launchPath = ExecutablePath(installerPath);
            using var launchLock = string.Equals(launchPath, installerPath, StringComparison.OrdinalIgnoreCase)
                ? null
                : new FileStream(launchPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            VerifyInstaller(launchPath);

            var start = new ProcessStartInfo(launchPath, InstallerArguments) { UseShellExecute = true, Verb = "runas" };
            using var process = Process.Start(start) ?? throw new InvalidOperationException("The installer did not start.");
        }
        catch (InvalidDataException ex)
        {
            // Either the file changed after NetSparkle checked it, or it was never
            // signed with the key this build trusts. Do not ask for administrator
            // rights for it, and throw it away so Install downloads a fresh copy.
            _logger.LogError("The update was not installed because {Reason}.", ex.Message);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Status = $"The update was not installed because {ex.Message}. Install downloads it again; or get it from the releases page.";
                _downloadedInstaller = null;
                _installing = false;
                IsBusy = false;
            });

            TryDelete(launchPath);
            if (!string.Equals(launchPath, installerPath, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(installerPath);
            }

            return;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            // Windows' own prompt, declined. Panelith keeps running; the next Install tries again.
            _logger.LogInformation("Installing the update needs administrator permission, which was not given.");
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Status = "Installing needs administrator permission, which was not given. Try Install again.";
                _installing = false;
                IsBusy = false;
            });
            return;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("The update could not be installed: {Message}", ex.Message);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Status = $"The update could not be installed: {ex.Message}";
                _installing = false;
                IsBusy = false;
            });
            return;
        }

        _logger.LogInformation("Administrator permission granted; closing Panelith so the update can install.");
        await _sparkle!.QuitApplication();
    }

    /// <summary>
    /// Checks the installer against the public key built into Panelith once more,
    /// immediately before it runs. NetSparkle already checked it when the download
    /// finished, but that was earlier and elsewhere; this is the check that decides
    /// whether these exact bytes get administrator rights.
    /// </summary>
    /// <exception cref="InvalidDataException">The signature is missing or does not match.</exception>
    private void VerifyInstaller(string installerPath)
    {
        if (_installingItem?.DownloadSignature is not { Length: > 0 } signature)
        {
            throw new InvalidDataException("the update did not come with a signature to check");
        }

        if (_sparkle!.SignatureVerifier.VerifySignatureOfFile(signature, installerPath) != ValidationResult.Valid)
        {
            throw new InvalidDataException("its signature does not match the key built into Panelith");
        }
    }

    /// <summary>
    /// A path Windows will agree to run. A download that already ends in
    /// <c>.exe</c> is used as it is. Otherwise a hard link with that extension
    /// is added beside it, falling back to a copy when a link cannot be made.
    /// </summary>
    internal static string ExecutablePath(string installerPath)
    {
        if (installerPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return installerPath;
        }

        var exePath = installerPath + ".exe";
        try
        {
            File.Delete(exePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A previous attempt may still be running. A new name avoids it.
            exePath = installerPath + "-" + Guid.NewGuid().ToString("N") + ".exe";
        }

        if (OperatingSystem.IsWindows() && CreateHardLink(exePath, installerPath, IntPtr.Zero))
        {
            return exePath;
        }

        File.Copy(installerPath, exePath, overwrite: true);
        return exePath;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateHardLinkW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateHardLink(string fileName, string existingFileName, IntPtr securityAttributes);

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("Could not delete {Path}: {Message}", path, ex.Message);
        }
    }

    private void OnDownloadFailed(string message)
    {
        _logger.LogWarning("The update could not be downloaded: {Message}", message);
        _downloadedInstaller = null;
        Status = $"The update could not be downloaded: {message}.";
        IsBusy = false;
    }

    private static bool IsInstalledCopy() =>
        OperatingSystem.IsWindows()
        && Path.GetDirectoryName(Environment.ProcessPath) is { } folder
        && File.Exists(Path.Combine(folder, "unins000.exe"));

    /// <summary>Tells NetSparkle which version is installed, without it reading the executable's resources.</summary>
    private sealed class VersionAccessor(string version) : IAssemblyAccessor
    {
        public string AssemblyCompany => "Bharath Vadlamannati";

        public string AssemblyCopyright => "Copyright (c) 2026 Bharath Vadlamannati";

        public string AssemblyDescription => "Panelith";

        public string AssemblyTitle => "Panelith";

        public string AssemblyProduct => "Panelith";

        public string AssemblyVersion => version;
    }

    private sealed class LogAdapter(ILogger logger) : NetSparkleUpdater.Interfaces.ILogger
    {
        public void PrintMessage(string message, params object[]? arguments)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("{Message}", arguments is { Length: > 0 } ? string.Format(System.Globalization.CultureInfo.InvariantCulture, message, arguments) : message);
            }
        }
    }
}
