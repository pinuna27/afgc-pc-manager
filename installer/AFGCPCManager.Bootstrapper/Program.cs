using System.Diagnostics;
using AFGCPCManager.Setup.Core.Installation;
using AFGCPCManager.Setup.Core.Dependencies;
using AFGCPCManager.Setup.Core.Models;
using AFGCPCManager.Setup.Core.Security;
using AFGCPCManager.Setup.Core.Updates;
using AFGCPCManager.VJoy;

namespace AFGCPCManager.Bootstrapper;

internal static class Program
{
    private const string SetupAsset = "AFGCPCManager-Setup-x64.exe", PayloadAsset = "AFGCPCManager-x64.zip";
    private static async Task<int> Main(string[] args)
    {
        try
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("AFGC PC Manager supports Windows only.");
            string destination = Get(args, "--install-dir") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "AFGC PC Manager");
            if (Has(args, "--update") || Has(args, "--repair")) return await DownloadAndLaunchAsync(destination, Has(args, "--repair"));
            if (Get(args, "--apply-archive") is string archive) return await ApplyArchiveAsync(
                archive, destination,
                Version.Parse(Get(args, "--version") ?? throw new ArgumentException("The signed release version is missing.")),
                Get(args, "--manifest"), Get(args, "--signature"));
            string payload = Get(args, "--payload") ?? Path.Combine(AppContext.BaseDirectory, "payload");
            if (!Directory.Exists(payload)) return await DownloadAndLaunchAsync(destination, repair: true);
            if (!Elevation.IsAdministrator()) return Elevation.RelaunchAsAdministrator(Environment.ProcessPath!, args);
            Version version = typeof(Program).Assembly.GetName().Version ?? new(0, 1, 0);
            await InstallPayloadAsync(payload, destination, version);
            try
            {
                bool restartRequired = await EnsureDependenciesAsync(destination, version, null,
                    () => WindowsSetupResumeRegistration.Register(Environment.ProcessPath!, ["--payload", payload, "--install-dir", destination]));
                if (restartRequired) return 3010;
                WindowsSetupResumeRegistration.Unregister(); return 0;
            }
            catch { await RemoveResumeUnlessRestartRequiredAsync(destination); throw; }
        }
        catch (Exception ex) { Console.Error.WriteLine($"Setup failed: {ex.Message}"); return 1; }
    }

    private static async Task<int> DownloadAndLaunchAsync(string destination, bool repair)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        var verifier = new ReleaseManifestVerifier(TrustedReleaseKeys.LoadAfgcPublicKey()); var client = new GitHubSignedReleaseClient(http, verifier);
        VerifiedRelease release = await client.GetLatestAsync("pinuna27", "afgc-pc-manager");
        string staging = Path.Combine(Path.GetTempPath(), "AFGC PC Manager", "updates", release.Version.ToString()); Directory.CreateDirectory(staging);
        string setup = await client.DownloadAssetAsync(release, SetupAsset, staging); string archive = await client.DownloadAssetAsync(release, PayloadAsset, staging);
        return Elevation.RelaunchAsAdministrator(setup, ["--apply-archive", archive, "--version", release.Version.ToString(), "--install-dir", destination]);
    }

    private static async Task<int> ApplyArchiveAsync(string archive, string destination, Version version, string? manifestPath, string? signaturePath)
    {
        if (!Elevation.IsAdministrator()) return Elevation.RelaunchAsAdministrator(Environment.ProcessPath!, Environment.GetCommandLineArgs().Skip(1));
        ReleaseManifest? localManifest = null;
        if (manifestPath is not null || signaturePath is not null)
        {
            if (manifestPath is null || signaturePath is null) throw new ArgumentException("Both the local manifest and signature are required.");
            localManifest = await new LocalReleaseBundleVerifier(new ReleaseManifestVerifier(TrustedReleaseKeys.LoadAfgcPublicKey()))
                .VerifyAsync(manifestPath, signaturePath, archive, version);
        }
        await StopRunningApplicationAsync(destination); string payload = Path.Combine(Path.GetTempPath(), "AFGC PC Manager", "payload", Guid.NewGuid().ToString("N"));
        try
        {
            new PayloadArchiveExtractor().Extract(archive, payload);
            await InstallPayloadAsync(payload, destination, version);
            bool restartRequired = await EnsureDependenciesAsync(destination, version, localManifest, () =>
            {
                var resume = new List<string> { "--apply-archive", archive, "--version", version.ToString(), "--install-dir", destination };
                if (manifestPath is not null) resume.AddRange(["--manifest", manifestPath, "--signature", signaturePath!]);
                WindowsSetupResumeRegistration.Register(Environment.ProcessPath!, resume);
            });
            if (restartRequired) { Console.WriteLine("A restart is required before setup can continue."); return 3010; }
            WindowsSetupResumeRegistration.Unregister();
            StartApplicationUnelevated(destination); return 0;
        }
        catch
        {
            await RemoveResumeUnlessRestartRequiredAsync(destination);
            throw;
        }
        finally { if (Directory.Exists(payload)) Directory.Delete(payload, true); }
    }
    private static async Task<bool> EnsureDependenciesAsync(string destination, Version version, ReleaseManifest? trustedManifest, Action registerResume)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        var client = new GitHubSignedReleaseClient(http, new ReleaseManifestVerifier(TrustedReleaseKeys.LoadAfgcPublicKey()));
        ReleaseManifest manifest;
        if (trustedManifest is null)
        {
            VerifiedRelease release = await client.GetLatestAsync("pinuna27", "afgc-pc-manager");
            if (release.Version != version) throw new InvalidDataException("The dependency manifest no longer matches this application release.");
            manifest = release.Manifest;
        }
        else manifest = trustedManifest;
        string staging = Path.Combine(Path.GetTempPath(), "AFGC PC Manager", "dependencies", version.ToString());
        var packages = new Dictionary<DependencyId, (Version Target, string InstallerPath)>();
        if (manifest.VJoy is DependencyRelease vjoy)
            packages[DependencyId.VJoy] = (Version.Parse(vjoy.Version), await client.DownloadDependencyAsync(vjoy, staging));
        if (manifest.HidHide is DependencyRelease hidHide)
            packages[DependencyId.HidHide] = (Version.Parse(hidHide.Version), await client.DownloadDependencyAsync(hidHide, staging));
        if (packages.Count == 0) return false;
        registerResume();
        var coordinator = new DependencyCoordinator(new WindowsDependencyDetector(), new DependencyInstaller(), new JournalStore());
        DependencyExecutionResult result = await coordinator.EnsureAsync(Path.Combine(destination, "install-journal.json"), packages, allowUpdates: true);
        if (!result.RestartRequired)
        {
            InstallationJournal journal = await new JournalStore().LoadAsync(Path.Combine(destination, "install-journal.json"))
                ?? throw new InvalidOperationException("The installation journal is missing after dependency setup.");
            if (journal.DependenciesInstalledBySetup.Contains(DependencyId.VJoy.ToString()))
                await new VJoyDeviceProvisioner().EnsureOneCompatibleDeviceAsync();
        }
        return result.RestartRequired;
    }
    private static async Task RemoveResumeUnlessRestartRequiredAsync(string destination)
    {
        InstallationJournal? journal = await new JournalStore().LoadAsync(Path.Combine(destination, "install-journal.json"));
        if (journal?.PendingDependencyOperation?.Phase != DependencyOperationPhase.RestartRequired)
            WindowsSetupResumeRegistration.Unregister();
    }
    private static async Task InstallPayloadAsync(string payload, string destination, Version version)
    {
        Console.WriteLine("Installing AFGC PC Manager..."); await new ApplicationInstaller(new JournalStore()).InstallAsync(payload, destination, version); WindowsInstallationRegistration.Register(destination, version); Console.WriteLine($"Installed successfully to {destination}");
    }
    private static async Task<Version?> InstalledVersionAsync(string destination)
    {
        try { return Version.TryParse((await new JournalStore().LoadAsync(Path.Combine(destination, "install-journal.json")))?.Version, out Version? value) ? value : null; } catch { return null; }
    }
    private static async Task StopRunningApplicationAsync(string destination)
    {
        string app = Path.Combine(destination, "AFGCPCManager.exe"); if (!File.Exists(app)) return;
        using Process? process = Process.Start(new ProcessStartInfo(app, "--exit") { UseShellExecute = false }); if (process is not null) await process.WaitForExitAsync();
        DateTime deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            bool running = Process.GetProcessesByName("AFGCPCManager").Any(candidate =>
            {
                try { using (candidate) return string.Equals(candidate.MainModule?.FileName, app, StringComparison.OrdinalIgnoreCase); } catch { candidate.Dispose(); return true; }
            });
            if (!running) return; await Task.Delay(200);
        }
        throw new TimeoutException("AFGC PC Manager did not exit in time for the update.");
    }
    private static void StartApplicationUnelevated(string destination)
    {
        string app = Path.Combine(destination, "AFGCPCManager.exe");
        if (!File.Exists(app)) return;
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")) { UseShellExecute = false };
        start.ArgumentList.Add(app); Process.Start(start);
    }
    private static bool Has(string[] args, string key) => args.Contains(key, StringComparer.OrdinalIgnoreCase);
    private static string? Get(string[] args, string key) { int index = Array.FindIndex(args, x => x.Equals(key, StringComparison.OrdinalIgnoreCase)); return index >= 0 && index + 1 < args.Length ? args[index + 1] : null; }
}
