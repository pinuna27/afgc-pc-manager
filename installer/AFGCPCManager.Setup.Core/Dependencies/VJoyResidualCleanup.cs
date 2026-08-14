using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AFGCPCManager.Setup.Core.Security;
using Microsoft.Win32;

namespace AFGCPCManager.Setup.Core.Dependencies;

public sealed record VJoyResidualCleanupResult(bool Cleaned, bool RestartRequired);

/// <summary>
/// Removes the narrow orphan left when vJoy's registered Inno uninstaller
/// deletes the application and driver-store package but its bundled cleanup
/// helper fails to delete the stopped service and driver binary.
/// </summary>
public static class VJoyResidualCleanup
{
    private const string ServiceName = "vjoy";
    private const string ExpectedPublisher = "Brunner Elektronik AG";
    private const string ExpectedProduct = "vJoy";

    public static bool CanClean(DependencyState state)
    {
        if (state.Id != DependencyId.VJoy || state.EffectiveReadiness != DependencyReadiness.Unhealthy
            || state.Evidence is not { Count: > 0 } evidence)
            return false;

        return Present(evidence, "driver service")
            && Absent(evidence, "registered application")
            && Absent(evidence, "runtime library")
            && evidence.Where(item => item.Source == "operational API")
                .All(item => item.Present == false);
    }

    public static async Task<VJoyResidualCleanupResult> CleanupAsync(
        DependencyState state, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows() || !CanClean(state)) return new(false, false);

        string expectedDriver = Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "vjoy.sys"));
        using RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using RegistryKey? service = machine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{ServiceName}");
        if (service is null) return new(false, false);

        string imagePath = service.GetValue("ImagePath")?.ToString()
            ?? throw new InvalidOperationException("The orphaned vJoy service has no driver path.");
        string actualDriver = NormalizeDriverPath(imagePath);
        if (!actualDriver.Equals(expectedDriver, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"The orphaned vJoy service points to an unexpected driver path: {actualDriver}");
        if (!File.Exists(actualDriver))
            throw new InvalidOperationException(
                "The orphaned vJoy service remains, but its signed driver file is missing. Remove or repair vJoy manually.");
        VerifyDriverIdentity(actualDriver);

        (int exitCode, string output) = await RunScDeleteAsync(cancellationToken);
        if (exitCode != 0)
            throw new InvalidOperationException(
                $"Windows could not delete the orphaned vJoy service (sc.exe exit {exitCode}). {output}".Trim());

        bool restartRequired = false;
        try { File.Delete(actualDriver); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!MoveFileEx(actualDriver, null, MoveFileDelayUntilReboot))
                throw new InvalidOperationException(
                    "Windows deleted the orphaned vJoy service but could not schedule its driver file for removal.", ex);
            restartRequired = true;
        }

        return new(true, restartRequired);
    }

    private static bool Present(IEnumerable<DependencyEvidence> evidence, string source) =>
        evidence.Any(item => item.Source == source && item.Present == true);

    private static bool Absent(IEnumerable<DependencyEvidence> evidence, string source) =>
        evidence.Any(item => item.Source == source && item.Present == false);

    private static string NormalizeDriverPath(string imagePath)
    {
        string value = Environment.ExpandEnvironmentVariables(imagePath.Trim().Trim('"'));
        const string systemRootPrefix = @"\SystemRoot\";
        if (value.StartsWith(systemRootPrefix, StringComparison.OrdinalIgnoreCase))
            value = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                value[systemRootPrefix.Length..]);
        return Path.GetFullPath(value);
    }

    private static void VerifyDriverIdentity(string path)
    {
        if (!AuthenticodeTrust.IsTrusted(path))
            throw new InvalidOperationException(
                "The orphaned vJoy driver is not Authenticode-trusted, so setup will not delete it.");
        try
        {
#pragma warning disable SYSLIB0057
            using X509Certificate2 certificate = new(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
            FileVersionInfo version = FileVersionInfo.GetVersionInfo(path);
            if (!certificate.Subject.Contains(ExpectedPublisher, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(version.ProductName, ExpectedProduct, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(version.OriginalFilename, "vJoy.sys", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "The orphaned service driver is not the expected Brunner-signed vJoy driver, so setup will not delete it.");
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            throw new InvalidOperationException(
                "Setup could not verify the orphaned vJoy driver signer, so it will not delete it.", ex);
        }
    }

    private static async Task<(int ExitCode, string Output)> RunScDeleteAsync(
        CancellationToken cancellationToken)
    {
        string sc = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "sc.exe");
        using Process process = Process.Start(new ProcessStartInfo(sc, $"delete {ServiceName}")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        }) ?? throw new InvalidOperationException("Windows Service Control could not be started.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return (process.ExitCode, string.Join(" ", await stdout, await stderr).Trim());
    }

    private const int MoveFileDelayUntilReboot = 4;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileEx(string existingFileName, string? newFileName, int flags);
}
