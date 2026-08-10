using Microsoft.Win32;

namespace AFGCPCManager.Setup.Core.Installation;

public static class WindowsUninstallResumeRegistration
{
    private const string KeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce";
    private const string ValueName = "AFGCPCManagerUninstallResume";
    private static readonly string ArgumentsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "AFGC PC Manager", "Uninstall", "resume-arguments.json");

    public static void Register(string executable, IEnumerable<string> arguments)
    {
        try
        {
            WindowsResumeArguments.Write(ArgumentsPath, arguments);
            string command = WindowsResumeArguments.BuildCommand(executable, ArgumentsPath);
            if (command.Length > 260) throw new PathTooLongException("The uninstall restart-continuation command exceeds the Windows RunOnce limit.");
            using RegistryKey key = Registry.LocalMachine.CreateSubKey(KeyPath, true);
            key.SetValue(ValueName, command, RegistryValueKind.String);
        }
        catch
        {
            try { Unregister(); } catch { }
            throw;
        }
    }

    public static void Unregister()
    {
        var errors = new List<Exception>();
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(KeyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch (Exception ex) { errors.Add(ex); }
        try { WindowsResumeArguments.Delete(ArgumentsPath); }
        catch (Exception ex) { errors.Add(ex); }
        if (errors.Count > 0) throw new AggregateException("The uninstall restart continuation could not be removed completely.", errors);
    }
}
