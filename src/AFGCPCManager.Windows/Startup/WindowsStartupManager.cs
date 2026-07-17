using Microsoft.Win32;

namespace AFGCPCManager.Windows.Startup;

public sealed class WindowsStartupManager
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AFGC PC Manager";
    public bool IsEnabled()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(KeyPath, false);
        return key?.GetValue(ValueName) is string;
    }
    public void SetEnabled(bool enabled, string executablePath)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(KeyPath, true);
        if (enabled) key.SetValue(ValueName, $"\"{Path.GetFullPath(executablePath)}\" --background", RegistryValueKind.String);
        else key.DeleteValue(ValueName, false);
    }
}
