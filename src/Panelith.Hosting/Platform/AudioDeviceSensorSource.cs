using System.Runtime.InteropServices;
using Panelith.Sensors;

namespace Panelith.Hosting;

/// <summary>
/// The default playback device's name and volume, from Windows Core Audio.
/// </summary>
public sealed class AudioDeviceSensorSource : ISensorSource
{
    /// <inheritdoc />
    public string Id => "audio";

    /// <inheritdoc />
    public string DisplayName => "Audio";

    /// <inheritdoc />
    public bool IsAvailable => OperatingSystem.IsWindows();

    /// <inheritdoc />
    public ValueTask InitializeAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<SensorReading>> ReadAsync(CancellationToken cancellationToken = default)
    {
        var (name, volume) = WindowsAudio.ReadDefault();
        IReadOnlyList<SensorReading> readings =
        [
            SensorReading.Textual(SensorId.From("audio", "0", "name", "device"), "Playback device", name ?? string.Empty, Id),
            SensorReading.Numeric(SensorId.From("audio", "0", "load", "volume"), "Volume", SensorKind.Load, volume, 0d, 100d, Id),
        ];
        return ValueTask.FromResult(readings);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>Reads the default render endpoint. Failures become a blank name and no volume.</summary>
internal static class WindowsAudio
{
    private const int Render = 0;
    private const int Console = 0;
    private const int InProcess = 1;

    public static (string? Name, double? Volume) ReadDefault()
    {
        if (!OperatingSystem.IsWindows())
        {
            return (null, null);
        }

        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        IPropertyStore? store = null;
        object? volumeObject = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            if (enumerator.GetDefaultAudioEndpoint(Render, Console, out device) != 0 || device is null)
            {
                return (null, null);
            }

            string? name = null;
            if (device.OpenPropertyStore(0, out store) == 0 && store is not null)
            {
                var key = new PropertyKey
                {
                    FormatId = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"),
                    PropertyId = 14,
                };
                if (store.GetValue(ref key, out var value) == 0 && value.Pointer != IntPtr.Zero)
                {
                    name = Marshal.PtrToStringUni(value.Pointer);
                    PropVariantClear(ref value);
                }
            }

            var iid = new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");
            double? volume = null;
            if (device.Activate(ref iid, InProcess, IntPtr.Zero, out volumeObject) == 0
                && volumeObject is IAudioEndpointVolume endpoint
                && endpoint.GetMasterVolumeLevelScalar(out var scalar) == 0)
            {
                volume = Math.Clamp(scalar, 0f, 1f) * 100d;
            }

            return (name, volume);
        }
        catch (COMException)
        {
            return (null, null);
        }
        catch (InvalidCastException)
        {
            return (null, null);
        }
        finally
        {
            if (volumeObject is not null)
            {
                Marshal.ReleaseComObject(volumeObject);
            }

            if (store is not null)
            {
                Marshal.ReleaseComObject(store);
            }

            if (device is not null)
            {
                Marshal.ReleaseComObject(device);
            }

            if (enumerator is not null)
            {
                Marshal.ReleaseComObject(enumerator);
            }
        }
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid FormatId;
        public uint PropertyId;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)]
        public ushort VariantType;
        [FieldOffset(8)]
        public IntPtr Pointer;
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject
    {
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, int classContext, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object instance);

        [PreserveSig]
        int OpenPropertyStore(int access, out IPropertyStore store);

        [PreserveSig]
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

        [PreserveSig]
        int GetState(out int state);
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig]
        int GetCount(out uint count);

        [PreserveSig]
        int GetAt(uint index, out PropertyKey key);

        [PreserveSig]
        int GetValue(ref PropertyKey key, out PropVariant value);
    }

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig]
        int RegisterControlChangeNotify(IntPtr client);

        [PreserveSig]
        int UnregisterControlChangeNotify(IntPtr client);

        [PreserveSig]
        int GetChannelCount(out uint count);

        [PreserveSig]
        int SetMasterVolumeLevel(float level, ref Guid context);

        [PreserveSig]
        int SetMasterVolumeLevelScalar(float level, ref Guid context);

        [PreserveSig]
        int GetMasterVolumeLevel(out float level);

        [PreserveSig]
        int GetMasterVolumeLevelScalar(out float level);
    }
}
