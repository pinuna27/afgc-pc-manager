using System.Diagnostics;

namespace AFGCPCManager.Setup.Core.Dependencies;

public sealed record DependencyInstallResult(
    bool Succeeded,
    bool RestartRequired,
    int ExitCode,
    bool RestartInitiated = false);

public interface IDependencyInstaller
{
    Task<DependencyInstallResult> RunInteractiveAsync(string installerPath, CancellationToken cancellationToken = default);
}

public sealed class DependencyInstaller : IDependencyInstaller
{
    // Windows terminates a process with DBG_TERMINATE_PROCESS while completing
    // an interactive vendor-requested system restart. The process that happened
    // to be starting next is not necessarily installed.
    internal const int ShutdownTerminationExitCode = 0x40010004;

    public async Task<DependencyInstallResult> RunInteractiveAsync(string installerPath, CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo(installerPath) { UseShellExecute = true };
        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("The dependency installer could not be started.");
        await process.WaitForExitAsync(cancellationToken);
        return InterpretExitCode(process.ExitCode);
    }

    public static DependencyInstallResult InterpretExitCode(int code) => code switch
    {
        1641 => new(true, true, code, RestartInitiated: true),
        ShutdownTerminationExitCode => new(false, true, code, RestartInitiated: true),
        8 or 3010 => new(true, true, code),
        0 => new(true, false, code),
        _ => new(false, false, code)
    };
}
