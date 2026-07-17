using Microsoft.Win32;

namespace AFGCPCManager.Setup.Core.Installation;

public static class WindowsInstallationRegistration
{
    private const string KeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\AFGCPCManager";
    public static void Register(string installDirectory, Version version)
    {
        using RegistryKey key = Registry.LocalMachine.CreateSubKey(KeyPath, true);
        key.SetValue("DisplayName", "AFGC PC Manager"); key.SetValue("DisplayVersion", version.ToString());
        key.SetValue("Publisher", "AFGC PC Manager contributors");
        key.SetValue("InstallLocation", installDirectory);
        key.SetValue("DisplayIcon", Path.Combine(installDirectory, "AFGCPCManager.exe"));
        key.SetValue("UninstallString", $"\"{Path.Combine(installDirectory, "AFGCPCManager.Uninstaller.exe")}\"");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord); key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }
    public static void Unregister() => Registry.LocalMachine.DeleteSubKeyTree(KeyPath, false);
}
