using System.Runtime.Versioning;
using System.Security.Cryptography;
using Panelith.Sensors;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Panelith.Hosting;

/// <summary>
/// Title, artist and album of whatever Windows is playing, plus its album
/// picture. Read from the local media session. Nothing is sent anywhere.
/// </summary>
[SupportedOSPlatform("windows10.0.17763.0")]
public sealed class MediaSessionSensorSource : ISensorSource
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private string? _artKey;

    /// <inheritdoc />
    public string Id => "media";

    /// <inheritdoc />
    public string DisplayName => "Now playing";

    /// <inheritdoc />
    public bool IsAvailable => OperatingSystem.IsWindows();

    /// <inheritdoc />
    public async ValueTask InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _manager = null;
        }
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<SensorReading>> ReadAsync(CancellationToken cancellationToken = default)
    {
        string title = string.Empty;
        string artist = string.Empty;
        string album = string.Empty;
        try
        {
            var session = _manager?.GetCurrentSession();
            if (session is not null)
            {
                var props = await session.TryGetMediaPropertiesAsync();
                title = props.Title ?? string.Empty;
                artist = props.Artist ?? string.Empty;
                album = props.AlbumTitle ?? string.Empty;
                await UpdateArtAsync(title, artist, props.Thumbnail);
            }
            else
            {
                ClearArt();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A player that has just closed throws. The screen shows a blank line
            // rather than the hub quarantining this source.
        }

        return
        [
            SensorReading.Textual(SensorId.From("media", "0", "text", "title"), "Now playing", title, Id),
            SensorReading.Textual(SensorId.From("media", "0", "text", "artist"), "Artist", artist, Id),
            SensorReading.Textual(SensorId.From("media", "0", "text", "album"), "Album", album, Id),
        ];
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _manager = null;
        return ValueTask.CompletedTask;
    }

    private async Task UpdateArtAsync(string title, string artist, IRandomAccessStreamReference? thumbnail)
    {
        var key = title + "\n" + artist;
        if (string.Equals(key, _artKey, StringComparison.Ordinal))
        {
            return;
        }

        _artKey = key;
        var path = MediaArt.FilePath;
        if (path is null)
        {
            return;
        }

        if (thumbnail is null || (title.Length == 0 && artist.Length == 0))
        {
            TryDelete(path);
            return;
        }

        try
        {
            using var stream = await thumbnail.OpenReadAsync();
            if (stream.Size is 0 or > 8 * 1024 * 1024)
            {
                TryDelete(path);
                return;
            }

            var length = (uint)stream.Size;
            var reader = new DataReader(stream);
            await reader.LoadAsync(length);
            var bytes = new byte[length];
            reader.ReadBytes(bytes);
            reader.Dispose();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + "." + Convert.ToHexString(SHA256.HashData(bytes))[..12] + ".tmp";
            await File.WriteAllBytesAsync(temporary, bytes);
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            TryDelete(path);
        }
    }

    private void ClearArt()
    {
        if (_artKey is null)
        {
            return;
        }

        _artKey = null;
        if (MediaArt.FilePath is { } path)
        {
            TryDelete(path);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
