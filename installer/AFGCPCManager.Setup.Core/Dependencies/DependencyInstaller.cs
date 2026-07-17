using System.Diagnostics;

namespace AFGCPCManager.Setup.Core.Dependencies;

public sealed record DependencyInstallResult(bool Succeeded, bool RestartRequired, int ExitCode);

public interface IDependencyInstaller
{
    Task<DependencyInstallResult> RunInteractiveAsync(string installerPath, CancellationToken cancellationToken = default);
}

public sealed class DependencyInstaller : IDependencyInstaller
{
    public async Task<DependencyInstallResult> RunInteractiveAsync(string installerPath, CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo(installerPath) { UseShellExecute = true };
        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("The dependency installer could not be started.");
        await process.WaitForExitAsync(cancellationToken);
        return InterpretExitCode(process.ExitCode);
    }

    public static DependencyInstallResult InterpretExitCode(int code) =>
        new(code is 0 or 8 or 1641 or 3010, code is 8 or 1641 or 3010, code);
}
