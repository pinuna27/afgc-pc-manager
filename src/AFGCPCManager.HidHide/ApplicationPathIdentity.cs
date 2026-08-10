using System.Runtime.InteropServices;
using System.Text;

namespace AFGCPCManager.HidHide;

internal static class ApplicationPathIdentity
{
    public static bool Contains(IEnumerable<string> configuredPaths, string applicationPath)
    {
        ArgumentNullException.ThrowIfNull(configuredPaths);
        string fullPath = Path.GetFullPath(applicationPath);
        string? driverPath = TryToDriverPath(fullPath);
        return configuredPaths.Any(configured =>
            configured.Equals(fullPath, StringComparison.OrdinalIgnoreCase)
            || driverPath is not null
            && configured.Equals(driverPath, StringComparison.OrdinalIgnoreCase));
    }

    public static string? TryToDriverPath(string applicationPath)
    {
        string fullPath = Path.GetFullPath(applicationPath);
        string? root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root) || !root.EndsWith(Path.DirectorySeparatorChar))
            return null;
        string drive = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var target = new StringBuilder(1024);
        uint length = QueryDosDevice(drive, target, target.Capacity);
        if (length == 0) return null;
        string suffix = fullPath[root.Length..];
        return target.ToString().TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar + suffix;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint QueryDosDevice(
        string deviceName, StringBuilder targetPath, int maximumLength);
}
