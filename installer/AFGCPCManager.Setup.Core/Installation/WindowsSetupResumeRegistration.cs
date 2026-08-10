using Microsoft.Win32;

namespace AFGCPCManager.Setup.Core.Installation;

public static class WindowsSetupResumeRegistration
{
    private const string KeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string LegacyKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce";
    private const string ValueName = "AFGCPCManagerSetupResume";
    private static readonly string ArgumentsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "AFGC PC Manager", "Setup", "resume-arguments.json");

    public static void Register(string executable, IEnumerable<string> arguments)
    {
        try
        {
            WindowsResumeArguments.Write(ArgumentsPath, arguments);
            string command = WindowsResumeArguments.BuildCommand(executable, ArgumentsPath);
            if (command.Length > 260) throw new PathTooLongException("The setup restart-continuation command exceeds the Windows startup-command limit.");
            using RegistryKey key = Registry.LocalMachine.CreateSubKey(KeyPath, true);
            key.SetValue(ValueName, command, RegistryValueKind.String);
            using RegistryKey? legacyKey = Registry.LocalMachine.OpenSubKey(LegacyKeyPath, writable: true);
            legacyKey?.DeleteValue(ValueName, throwOnMissingValue: false);
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
        Try(() =>
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(KeyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        });
        Try(() =>
        {
            using RegistryKey? legacyKey = Registry.LocalMachine.OpenSubKey(LegacyKeyPath, writable: true);
            legacyKey?.DeleteValue(ValueName, throwOnMissingValue: false);
        });
        Try(() => WindowsResumeArguments.Delete(ArgumentsPath));
        if (errors.Count > 0) throw new AggregateException("The setup restart continuation could not be removed completely.", errors);

        void Try(Action action) { try { action(); } catch (Exception ex) { errors.Add(ex); } }
    }

    internal static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";
}
