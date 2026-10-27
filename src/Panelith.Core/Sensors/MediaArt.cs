namespace Panelith.Sensors;

/// <summary>
/// The album picture for whatever Windows is playing. The file is written by the
/// media sensor source and read by image widgets whose source is <see cref="Source"/>.
/// </summary>
public static class MediaArt
{
    /// <summary>Image-widget source that means "the current album picture".</summary>
    public const string Source = "@media.art";

    /// <summary>Where the current album picture is stored, or null when there is nowhere to put it.</summary>
    public static string? FilePath
    {
        get
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return string.IsNullOrEmpty(root) ? null : Path.Combine(root, "Panelith", "media-art.jpg");
        }
    }
}
