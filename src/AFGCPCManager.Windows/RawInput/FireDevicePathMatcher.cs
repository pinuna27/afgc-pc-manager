using AFGCPCManager.Core.Devices;

namespace AFGCPCManager.Windows.RawInput;

internal static class FireDevicePathMatcher
{
    public static bool IsMatch(string path) => FireControllerPathIdentity.IsMatch(path);
}
