using System.Diagnostics;
using Microsoft.Win32;

namespace AFGCPCManager.Setup.Core.Dependencies;

public sealed record RegisteredUninstaller(string DisplayName, string Command);

public sealed class RegisteredDependencyUninstaller
{
    public RegisteredUninstaller? Find(DependencyId dependency)
    {
        foreach (RegistryHive hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
            using RegistryKey? uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall is null) continue;
            foreach (string childName in uninstall.GetSubKeyNames())
            {
                using RegistryKey? child = uninstall.OpenSubKey(childName);
                string? displayName = child?.GetValue("DisplayName")?.ToString();
                string? command = child?.GetValue("UninstallString")?.ToString();
                if (displayName is not null && command is not null && Matches(dependency, displayName))
                    return new(displayName, command);
            }
        }
        return null;
    }

    public async Task<int> UninstallInteractiveAsync(DependencyId dependency, CancellationToken cancellationToken = default)
    {
        RegisteredUninstaller registration = Find(dependency)
            ?? throw new InvalidOperationException($"Windows does not have a registered uninstaller for {dependency}.");
        (string executable, string arguments) = SplitCommand(registration.Command);
        using Process process = Process.Start(new ProcessStartInfo(executable, arguments) { UseShellExecute = true })
            ?? throw new InvalidOperationException($"Could not start the {registration.DisplayName} uninstaller.");
        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }

    public static bool Matches(DependencyId dependency, string displayName) => dependency switch
    {
        DependencyId.VJoy => displayName.Contains("vJoy", StringComparison.OrdinalIgnoreCase),
        DependencyId.HidHide => displayName.Contains("HidHide", StringComparison.OrdinalIgnoreCase),
        _ => false
    };

    public static (string Executable, string Arguments) SplitCommand(string command)
    {
        string value = command.Trim();
        if (value.Length == 0) throw new InvalidDataException("The registered uninstall command is empty.");
        if (value[0] == '"')
        {
            int closing = value.IndexOf('"', 1);
            if (closing < 0) throw new InvalidDataException("The registered uninstall command is malformed.");
            return (value[1..closing], value[(closing + 1)..].TrimStart());
        }
        int separator = value.IndexOf(' ');
        return separator < 0 ? (value, string.Empty) : (value[..separator], value[(separator + 1)..].TrimStart());
    }
}
