namespace AFGCPCManager.Windows.RawInput;

internal static class FireDevicePathMatcher
{
    public static bool IsMatch(string path) =>
        path.Contains("VID&00021949_PID&0402", StringComparison.OrdinalIgnoreCase) ||
        path.Contains("VID_1949&PID_0402", StringComparison.OrdinalIgnoreCase);
}
