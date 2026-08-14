using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace AFGCPCManager.Windows.Devices;

public sealed class FireControllerLightWriter
{
    private readonly Func<string, byte, bool> _send;

    public FireControllerLightWriter() : this(Send) { }

    internal FireControllerLightWriter(Func<string, byte, bool> send) =>
        _send = send ?? throw new ArgumentNullException(nameof(send));

    public bool TrySetIdentificationLight(IEnumerable<string> devicePaths, byte mask)
    {
        ArgumentNullException.ThrowIfNull(devicePaths);
        if (mask > 0x0f) throw new ArgumentOutOfRangeException(nameof(mask));

        foreach (string path in devicePaths.Where(path => !string.IsNullOrWhiteSpace(path))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (_send(path, mask)) return true;
            }
            catch (Exception ex) when (ex is IOException
                or UnauthorizedAccessException or Win32Exception)
            {
                // A composite endpoint can disappear between discovery and this write.
                // Continue trying the controller's remaining HID collections.
            }
        }

        return false;
    }

    private static bool Send(string path, byte mask)
    {
        const uint genericWrite = 0x40000000, shareRead = 1, shareWrite = 2,
            openExisting = 3;
        using SafeFileHandle handle = CreateFile(path, genericWrite,
            shareRead | shareWrite, nint.Zero, openExisting, 0, nint.Zero);
        return !handle.IsInvalid && HidD_SetOutputReport(handle, [0x01, mask], 2);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share,
        nint security, uint creation, uint flags, nint template);

    [DllImport("hid.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_SetOutputReport(
        SafeFileHandle handle, byte[] report, int reportLength);
}
