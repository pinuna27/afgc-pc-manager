using Microsoft.Win32;

namespace AFGCPCManager.Setup.Core.Installation;

public static class WindowsInstallationRegistration
{
    private const string KeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\AFGCPCManager";
    private const string StartupKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupValueName = "AFGC PC Manager";
    public static void Register(string installDirectory, Version version)
    {
        installDirectory = Path.GetFullPath(installDirectory);
        if (!File.Exists(Path.Combine(installDirectory, "AFGCPCManager.exe"))
            || !File.Exists(Path.Combine(installDirectory, "AFGCPCManager.Uninstaller.exe")))
            throw new InvalidDataException("The installed application payload is incomplete.");
        using RegistryKey key = Registry.LocalMachine.CreateSubKey(KeyPath, true);
        key.SetValue("DisplayName", "AFGC PC Manager"); key.SetValue("DisplayVersion", version.ToString());
        key.SetValue("Publisher", "AFGC PC Manager contributors");
        key.SetValue("InstallLocation", installDirectory);
        key.SetValue("DisplayIcon", Path.Combine(installDirectory, "AFGCPCManager.exe"));
        key.SetValue("UninstallString", $"\"{Path.Combine(installDirectory, "AFGCPCManager.Uninstaller.exe")}\"");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord); key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        WindowsShortcutManager.Create(installDirectory);
    }
    public static void Unregister()
    {
        var errors = new List<Exception>();
        try
        {
            using RegistryKey? startup = Registry.CurrentUser.OpenSubKey(StartupKeyPath, writable: true);
            startup?.DeleteValue(StartupValueName, throwOnMissingValue: false);
        }
        catch (Exception ex) { errors.Add(ex); }
        try { WindowsShortcutManager.Remove(); }
        catch (Exception ex) { errors.Add(ex); }
        try { Registry.LocalMachine.DeleteSubKeyTree(KeyPath, false); }
        catch (Exception ex) { errors.Add(ex); }
        if (errors.Count > 0) throw new AggregateException("The Windows installation registration could not be removed completely.", errors);
    }
}
