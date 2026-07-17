using Microsoft.Win32;

namespace AFGCPCManager.Setup.Core.Installation;

public static class WindowsSetupResumeRegistration
{
    private const string KeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce";
    private const string ValueName = "AFGCPCManagerSetupResume";

    public static void Register(string executable, IEnumerable<string> arguments)
    {
        string command = Quote(executable) + " " + string.Join(" ", arguments.Select(Quote));
        using RegistryKey key = Registry.LocalMachine.CreateSubKey(KeyPath, true);
        key.SetValue(ValueName, command, RegistryValueKind.String);
    }

    public static void Unregister()
    {
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(KeyPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    internal static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";
}
