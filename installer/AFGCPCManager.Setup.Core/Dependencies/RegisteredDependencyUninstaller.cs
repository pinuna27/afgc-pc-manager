using System.Diagnostics;
using Microsoft.Win32;

namespace AFGCPCManager.Setup.Core.Dependencies;

public sealed record RegisteredUninstaller(string DisplayName, string Command);

public interface IRegisteredDependencyUninstaller
{
    RegisteredUninstaller? Find(DependencyId dependency);
    Task<int> UninstallInteractiveAsync(RegisteredUninstaller registration, CancellationToken cancellationToken = default);
}

public sealed class RegisteredDependencyUninstaller : IRegisteredDependencyUninstaller
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

    public async Task<int> UninstallInteractiveAsync(RegisteredUninstaller registration, CancellationToken cancellationToken = default)
    {
        (string executable, string arguments) = SplitCommand(registration.Command);
        using Process process = Process.Start(new ProcessStartInfo(executable, arguments) { UseShellExecute = true })
            ?? throw new InvalidOperationException($"Could not start the {registration.DisplayName} uninstaller.");
        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }

    public static bool Matches(DependencyId dependency, string displayName) => dependency switch
    {
        DependencyId.VJoy => displayName.Equals("vJoy", StringComparison.OrdinalIgnoreCase)
            || displayName.Contains("vJoy Device Driver", StringComparison.OrdinalIgnoreCase),
        DependencyId.ViGEmBus => displayName.Contains("ViGEm Bus Driver", StringComparison.OrdinalIgnoreCase)
            || displayName.Contains("Virtual Gamepad Emulation Bus Driver", StringComparison.OrdinalIgnoreCase),
        DependencyId.HidHide => displayName.Contains("HidHide", StringComparison.OrdinalIgnoreCase),
        _ => false
    };

    public static (string Executable, string Arguments) SplitCommand(string command)
    {
        string value = Environment.ExpandEnvironmentVariables(command.Trim());
        if (value.Length == 0) throw new InvalidDataException("The registered uninstall command is empty.");
        if (value[0] == '"')
        {
            int closing = value.IndexOf('"', 1);
            if (closing < 0) throw new InvalidDataException("The registered uninstall command is malformed.");
            return (value[1..closing], value[(closing + 1)..].TrimStart());
        }
        int separator = FindUnquotedExecutableEnd(value);
        return separator < 0 ? (value, string.Empty) : (value[..separator], value[(separator + 1)..].TrimStart());
    }

    private static int FindUnquotedExecutableEnd(string value)
    {
        foreach (string extension in new[] { ".exe", ".com", ".cmd", ".bat" })
        {
            int searchFrom = 0;
            while (value.IndexOf(extension, searchFrom, StringComparison.OrdinalIgnoreCase) is int index && index >= 0)
            {
                int end = index + extension.Length;
                if (end == value.Length) return -1;
                if (char.IsWhiteSpace(value[end])) return end;
                searchFrom = end;
            }
        }
        return value.IndexOf(' ');
    }
}

public sealed record DependencyRemovalExecutionResult(
    bool RestartRequired,
    bool RestartInitiated,
    List<string> ContinuationArguments);

public sealed class DependencyRemovalCoordinator(
    IRegisteredDependencyUninstaller uninstaller,
    Action<IReadOnlyList<string>> registerContinuation)
{
    public async Task<DependencyRemovalExecutionResult> RemoveAsync(
        DependencyId dependency,
        IEnumerable<string> continuationArguments,
        CancellationToken cancellationToken = default)
    {
        List<string> current = continuationArguments.ToList();
        RegisteredUninstaller? registration = uninstaller.Find(dependency);
        if (registration is null)
            return new(false, false, DependencyUninstallContinuation.AfterCompleted(current, dependency));

        registerContinuation(current);
        int exitCode = await uninstaller.UninstallInteractiveAsync(registration, cancellationToken);
        if (exitCode is not (0 or 1641 or 3010))
            throw new InvalidOperationException($"The {dependency} uninstaller exited with code {exitCode}.");

        List<string> advanced = DependencyUninstallContinuation.AfterCompleted(current, dependency);
        registerContinuation(advanced);
        return new(exitCode is 1641 or 3010, exitCode == 1641, advanced);
    }
}
