using System.Diagnostics;
using AFGCPCManager.Setup.Core.Installation;
using AFGCPCManager.Setup.Core.Dependencies;
using AFGCPCManager.Setup.Core.Models;
using AFGCPCManager.Setup.Core.Security;
using AFGCPCManager.Setup.Core.Updates;
using AFGCPCManager.Setup.Core;
using AFGCPCManager.HidHide;
using System.Runtime.InteropServices;

namespace AFGCPCManager.Bootstrapper;

internal static class Program
{
    private const string SetupAsset = "AFGCPCManager-Setup-x64.exe", PayloadAsset = "AFGCPCManager-x64.zip";

    [STAThread]
    private static int Main(string[] args)
    {
        if (DriverProcessHost.TryRunInternalCommand(args, out int driverExitCode))
            return ExitAfterVJoyUse(driverExitCode);

        try { args = WindowsResumeArguments.Expand(args); }
        catch (Exception ex)
        {
            try { WindowsSetupResumeRegistration.Unregister(); } catch { }
            WriteFailureDiagnostic(ex);
            if (CommandLineArguments.Has(args, "--cli"))
            {
                _ = AttachConsole(unchecked((uint)-1));
                Console.Error.WriteLine($"Setup failed: {ex.Message}");
            }
            else
            {
                ApplicationConfiguration.Initialize();
                MessageBox.Show($"Setup could not resume. Launch setup again.\n\n{ex.Message}",
                    "AFGC PC Manager Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return 1;
        }
        if (CommandLineArguments.Has(args, "--cli"))
        {
            var execution = new SetupExecutionContext(cliMode: true);
            _ = AttachConsole(unchecked((uint)-1));
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
            Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
            int result = RunCoreAsync(args.Where(x =>
                !x.Equals("--cli", StringComparison.OrdinalIgnoreCase))
                .ToArray(), execution).GetAwaiter().GetResult();
            if (result == 0)
                execution.StartScheduledApplication();
            return ExitAfterVJoyUse(result);
        }
        ApplicationConfiguration.Initialize();
        int wizardResult;
        using (var wizard = new SetupWizardForm(args))
        {
            Application.Run(wizard);
            wizardResult = wizard.ResultCode;
        }
        return ExitAfterVJoyUse(wizardResult);
    }

    private static int ExitAfterVJoyUse(int exitCode)
    {
        // Some vJoyInterface.dll builds leave a native foreground/dummy-window
        // thread alive after their managed wrapper is disposed. At this point
        // every setup task, using scope, and async finally block has completed;
        // terminate the process explicitly so a successful setup cannot retain
        // the global lifecycle mutex indefinitely.
        try { Console.Out.Flush(); Console.Error.Flush(); } catch { }
        // Environment.Exit/ExitProcess invokes DLL detach handlers. The affected
        // vJoyInterface build can hang in that detach path as well, so use the
        // kernel's non-cooperative process termination after durable setup work
        // is complete. Windows then closes the dummy window and mutex handles.
        if (!TerminateProcess(GetCurrentProcess(), unchecked((uint)exitCode)))
            throw new System.ComponentModel.Win32Exception(
                Marshal.GetLastWin32Error(), "Setup could not terminate its completed vJoy host process.");
        return exitCode;
    }

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(nint process, uint exitCode);

    internal static async Task<int> RunCoreAsync(
        string[] args, SetupExecutionContext execution)
    {
        ArgumentNullException.ThrowIfNull(execution);
        execution.ResetResult();
        try
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("AFGC PC Manager supports Windows only.");
            string destination = CommandLineArguments.Get(args, "--install-dir")
                ?? SetupProductIdentity.DefaultInstallDirectory;
            if (CommandLineArguments.Has(args, "--update")
                || CommandLineArguments.Has(args, "--repair"))
                return await DownloadAndLaunchAsync(destination, execution);
            if (CommandLineArguments.Get(args, "--apply-archive") is string archive)
            {
                bool cleanupSource = Elevation.IsAdministrator();
                try
                {
                    return await ApplyArchiveAsync(
                        archive, destination,
                        Version.Parse(CommandLineArguments.Get(args, "--version")
                            ?? throw new ArgumentException(
                                "The signed release version is missing.")),
                        CommandLineArguments.Get(args, "--manifest"),
                        CommandLineArguments.Get(args, "--signature"), execution);
                }
                finally
                {
                    if (cleanupSource)
                        CleanupDownloadedSetupSource(
                            CommandLineArguments.Get(args, "--cleanup-source-dir"));
                }
            }
            string payload = CommandLineArguments.Get(args, "--payload")
                ?? Path.Combine(AppContext.BaseDirectory, "payload");
            bool resumeOnly = CommandLineArguments.Has(args, "--resume-only");
            if (!Directory.Exists(payload))
            {
                if (resumeOnly) throw new DirectoryNotFoundException("The durable setup resume payload is missing.");
                return await DownloadAndLaunchAsync(destination, execution);
            }
            if (!Elevation.IsAdministrator())
                return LaunchElevated(args, execution);
            using LifecycleFileLock lifecycle = LifecycleFileLock.Acquire();
            Version version = typeof(Program).Assembly.GetName().Version ?? new(0, 1, 0);
            await execution.InstalledApplication.StopAsync(destination, execution.Report);
            await InstallPayloadAsync(payload, destination, version, execution, resumeOnly);
            ReleaseManifest? localManifest = await LoadSignedDependencyManifestAsync(
                CommandLineArguments.Get(args, "--manifest"),
                CommandLineArguments.Get(args, "--signature"), version);
            DurableLocalSetupBundle? stagedResume = null;
            string? durableResumeDirectory = CommandLineArguments.Get(args, "--durable-local-dir");
            try
            {
                bool restartRequired = await EnsureDependenciesAsync(destination, version, localManifest,
                    () =>
                    {
                        stagedResume ??= DurableSetupStaging.StageLocal(version, CurrentExecutable(),
                            CommandLineArguments.Get(args, "--manifest"),
                            CommandLineArguments.Get(args, "--signature"));
                        var resume = new List<string>
                        {
                            "--wizard-run", "--resume-only", "--payload", stagedResume.PayloadDirectory,
                            "--install-dir", destination, "--durable-local-dir", stagedResume.Directory
                        };
                        if (stagedResume.ManifestPath is not null)
                            resume.AddRange(["--manifest", stagedResume.ManifestPath, "--signature", stagedResume.SignaturePath!]);
                        WindowsSetupResumeRegistration.Register(stagedResume.SetupPath, resume);
                    }, execution);
                if (restartRequired) return 3010;
                WindowsSetupResumeRegistration.Unregister();
                CleanupDurableLocalResume(durableResumeDirectory ?? stagedResume?.Directory);
                return 0;
            }
            catch
            {
                await RemoveResumeUnlessRestartRequiredAsync(destination);
                if (!await IsRestartRequiredAsync(destination))
                    CleanupDurableLocalResume(durableResumeDirectory ?? stagedResume?.Directory);
                throw;
            }
        }
        catch (Exception ex)
        {
            execution.LastError = ex.Message;
            WriteFailureDiagnostic(ex);
            execution.Report($"Setup failed: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> DownloadAndLaunchAsync(
        string destination, SetupExecutionContext execution)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        var verifier = new ReleaseManifestVerifier(TrustedReleaseKeys.LoadAfgcPublicKey()); var client = new GitHubSignedReleaseClient(http, verifier);
        VerifiedRelease release = await client.GetLatestAsync(
            SetupProductIdentity.GitHubOwner, SetupProductIdentity.GitHubRepository);
        string staging = Path.Combine(SetupProductIdentity.TemporaryDirectory,
            "updates", release.Version.ToString());
        Directory.CreateDirectory(staging);
        string setup = await client.DownloadAssetAsync(release, SetupAsset, staging); string archive = await client.DownloadAssetAsync(release, PayloadAsset, staging);
        string manifest = Path.Combine(staging, "release-manifest.json");
        string signature = Path.Combine(staging, "release-manifest.sig");
        await File.WriteAllBytesAsync(manifest, release.ManifestBytes.ToArray());
        await File.WriteAllBytesAsync(signature, release.SignatureBytes.ToArray());
        string mode = execution.CliMode ? "--cli" : "--wizard-run";
        return LaunchElevated([mode, "--apply-archive", archive, "--version", release.Version.ToString(),
            "--install-dir", destination, "--manifest", manifest, "--signature", signature,
            "--cleanup-source-dir", staging], execution, setup);
    }

    private static async Task<int> ApplyArchiveAsync(
        string archive,
        string destination,
        Version version,
        string? manifestPath,
        string? signaturePath,
        SetupExecutionContext execution)
    {
        if (!Elevation.IsAdministrator())
        {
            IEnumerable<string> elevatedArguments = Environment.GetCommandLineArgs().Skip(1);
            if (!execution.CliMode)
                elevatedArguments = elevatedArguments.Append("--wizard-run");
            return LaunchElevated(elevatedArguments, execution);
        }
        using LifecycleFileLock lifecycle = LifecycleFileLock.Acquire();
        execution.Report("Verifying the AFGC PC Manager release...");
        ReleaseManifest? localManifest = null;
        if (manifestPath is not null || signaturePath is not null)
        {
            if (manifestPath is null || signaturePath is null) throw new ArgumentException("Both the local manifest and signature are required.");
            localManifest = await new LocalReleaseBundleVerifier(new ReleaseManifestVerifier(TrustedReleaseKeys.LoadAfgcPublicKey()))
                .VerifyAsync(manifestPath, signaturePath, archive, version);
        }
        DurableSetupBundle durable = DurableSetupStaging.Stage(version, CurrentExecutable(), archive, manifestPath, signaturePath);
        archive = durable.ArchivePath; manifestPath = durable.ManifestPath; signaturePath = durable.SignaturePath;
        execution.Report("Preparing application files...");
        await execution.InstalledApplication.StopAsync(destination, execution.Report);
        string payload = Path.Combine(SetupProductIdentity.TemporaryDirectory, "payload",
            Guid.NewGuid().ToString("N"));
        try
        {
            new PayloadArchiveExtractor().Extract(archive, payload);
            await InstallPayloadAsync(payload, destination, version, execution);
            bool restartRequired = await EnsureDependenciesAsync(destination, version, localManifest, () =>
            {
                var resume = new List<string> { "--wizard-run", "--apply-archive", archive, "--version", version.ToString(), "--install-dir", destination };
                if (manifestPath is not null) resume.AddRange(["--manifest", manifestPath, "--signature", signaturePath!]);
                WindowsSetupResumeRegistration.Register(durable.SetupPath, resume);
            }, execution);
            if (restartRequired)
            {
                execution.Report("A restart is required before setup can continue.");
                return 3010;
            }
            WindowsSetupResumeRegistration.Unregister();
            TryCleanupDirectory(durable.Directory);
            execution.ScheduleInstalledApplicationStart(destination);
            return 0;
        }
        catch
        {
            await RemoveResumeUnlessRestartRequiredAsync(destination);
            if (!await IsRestartRequiredAsync(destination)) TryCleanupDirectory(durable.Directory);
            throw;
        }
        finally { TryCleanupDirectory(payload); }
    }
    private static async Task<bool> EnsureDependenciesAsync(
        string destination,
        Version version,
        ReleaseManifest? trustedManifest,
        Action registerResume,
        SetupExecutionContext execution)
    {
        string journalPath = Path.Combine(
            destination, SetupProductIdentity.InstallJournalFileName);
        var journalStore = new JournalStore();
        InstallationJournal journal = await journalStore.LoadAsync(journalPath)
            ?? throw new InvalidOperationException("The installation journal is missing before dependency setup.");
        var detector = new WindowsDependencyDetector(id =>
        {
            if (id == DependencyId.HidHide)
            {
                HidHideDependencyStatus status = HidHideDependencyProbe.Detect();
                return new(status.Installed, status.Operational, status.Version);
            }
            return id == DependencyId.VJoy
                ? DriverProcessHost.ProbeVJoy()
                : DriverProcessHost.ProbeViGEmBus();
        });
        DependencyState initialVJoy = detector.Detect(DependencyId.VJoy);
        if (VJoyResidualCleanup.CanClean(initialVJoy))
        {
            execution.Report("Removing residue left by the vJoy vendor uninstaller...");
            VJoyResidualCleanupResult cleanup = await VJoyResidualCleanup.CleanupAsync(initialVJoy);
            if (cleanup.RestartRequired)
            {
                registerResume();
                execution.Report("A restart is required to finish removing the old vJoy driver before setup can continue.");
                return true;
            }
            execution.Report("The old vJoy uninstall residue was removed safely.");
        }
        DependencyState[] installedStates = Enum.GetValues<DependencyId>()
            .Select(detector.Detect).ToArray();
        if (trustedManifest is null
            && DependencyManifestPolicy.CanUseInstalledDependenciesWithoutManifest(
                installedStates, journal))
        {
            execution.Report("Found operational vJoy, ViGEmBus, and HidHide installations; no dependency download is needed.");
            if (journal.DependenciesInstalledBySetup.Contains(DependencyId.VJoy.ToString()))
            {
                execution.Report("Preparing the virtual controller output...");
                DriverProcessHost.ProvisionVJoy(1);
                execution.Report("Virtual controller output is ready.");
            }
            return false;
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        var client = new GitHubSignedReleaseClient(http, new ReleaseManifestVerifier(TrustedReleaseKeys.LoadAfgcPublicKey()));
        ReleaseManifest manifest;
        if (trustedManifest is null)
        {
            VerifiedRelease release = await client.GetLatestAsync(
                SetupProductIdentity.GitHubOwner, SetupProductIdentity.GitHubRepository);
            if (!SameRelease(release.Version, version)) throw new InvalidDataException("The dependency manifest no longer matches this application release.");
            manifest = release.Manifest;
        }
        else manifest = trustedManifest;
        string staging = Path.Combine(SetupProductIdentity.TemporaryDirectory,
            "dependencies", version.ToString());
        try
        {
            var packages = new Dictionary<DependencyId, Version>();
            var releases = new Dictionary<DependencyId, DependencyRelease>();
            bool hasInstallerActions = false;
            if (manifest.VJoy is null || manifest.ViGEmBus is null || manifest.HidHide is null)
                throw new InvalidDataException(
                    "The signed release manifest is missing a required controller driver.");
            if (manifest.VJoy is DependencyRelease vjoy) AddPackage(DependencyId.VJoy, vjoy);
            if (manifest.ViGEmBus is DependencyRelease viGEmBus) AddPackage(DependencyId.ViGEmBus, viGEmBus);
            if (manifest.HidHide is DependencyRelease hidHide) AddPackage(DependencyId.HidHide, hidHide);
            if (packages.Count == 0) return false;
            if (hasInstallerActions) registerResume();
            var coordinator = new DependencyCoordinator(
                detector, new DependencyInstaller(), journalStore, execution.Report);
            DependencyExecutionResult result = await coordinator.EnsureAsync(
                journalPath,
                packages,
                allowUpdates: true,
                async (id, cancellationToken) =>
                {
                    string name = DependencyNames.DisplayName(id);
                    execution.Report($"Downloading the verified {name} installer...");
                    return await client.DownloadDependencyAsync(
                        releases[id], staging, cancellationToken: cancellationToken);
                });
            if (!result.RestartRequired)
            {
                InstallationJournal updatedJournal = await new JournalStore().LoadAsync(
                    Path.Combine(destination, SetupProductIdentity.InstallJournalFileName))
                    ?? throw new InvalidOperationException("The installation journal is missing after dependency setup.");
                if (updatedJournal.DependenciesInstalledBySetup.Contains(DependencyId.VJoy.ToString()))
                {
                    execution.Report("Preparing the virtual controller output...");
                    DriverProcessHost.ProvisionVJoy(1);
                    execution.Report("Virtual controller output is ready.");
                }
            }
            return result.RestartRequired;

            void AddPackage(DependencyId id, DependencyRelease dependency)
            {
                string name = DependencyNames.DisplayName(id);
                execution.Report($"Checking for {name}...");
                Version target = Version.Parse(dependency.Version.TrimStart('v', 'V'));
                DependencyState state = detector.Detect(id);
                string detectionMessage = state.EffectiveReadiness == DependencyReadiness.Ready
                    ? $"Found {name}{(state.Version is null ? "." : $" {state.Version}.")}"
                    : state.EffectiveReadiness == DependencyReadiness.Absent ? $"{name} is not installed."
                    : $"{name} was detected, but it is not operational yet.";
                execution.Report(detectionMessage);
                DependencyPlan plan = DependencyPlanBuilder.Build(state, target, journal);
                bool needsInstaller = plan.Action is DependencyAction.Install or DependencyAction.Repair or DependencyAction.Update;
                if (plan.Action == DependencyAction.Block)
                    execution.Report($"{name} cannot be changed safely: {plan.Reason}");
                else if (!needsInstaller)
                    execution.Report($"{name} is ready; no installation is needed.");
                hasInstallerActions |= needsInstaller;
                packages[id] = target;
                releases[id] = dependency;
            }
        }
        finally { TryCleanupDirectory(staging); }
    }

    private static async Task RemoveResumeUnlessRestartRequiredAsync(string destination)
    {
        InstallationJournal? journal;
        try
        {
            journal = await new JournalStore().LoadAsync(Path.Combine(
                destination, SetupProductIdentity.InstallJournalFileName));
        }
        catch { return; }
        if (journal?.PendingDependencyOperation?.Phase is not
            (DependencyOperationPhase.RestartRequired or DependencyOperationPhase.DeferredUntilRestart))
        {
            try { WindowsSetupResumeRegistration.Unregister(); }
            catch { }
        }
    }
    private static async Task<bool> IsRestartRequiredAsync(string destination)
    {
        try
        {
            return (await new JournalStore().LoadAsync(Path.Combine(
                destination, SetupProductIdentity.InstallJournalFileName)))?
                .PendingDependencyOperation?.Phase is DependencyOperationPhase.RestartRequired
                    or DependencyOperationPhase.DeferredUntilRestart;
        }
        catch { return true; }
    }

    private static async Task<ReleaseManifest?> LoadSignedDependencyManifestAsync(
        string? manifestPath, string? signaturePath, Version expectedVersion)
    {
        if (manifestPath is null && signaturePath is null) return null;
        if (manifestPath is null || signaturePath is null)
            throw new ArgumentException("Both the local manifest and signature are required.");
        byte[] manifestBytes = await File.ReadAllBytesAsync(manifestPath);
        byte[] signatureBytes = await File.ReadAllBytesAsync(signaturePath);
        ManifestVerificationResult result = new ReleaseManifestVerifier(TrustedReleaseKeys.LoadAfgcPublicKey())
            .Verify(manifestBytes, signatureBytes);
        if (!result.IsValid) throw new InvalidDataException(result.FailureReason);
        ReleaseManifest manifest = result.Manifest!;
        if (!Version.TryParse(manifest.Version.TrimStart('v', 'V'), out Version? signedVersion)
            || signedVersion.Major != expectedVersion.Major
            || signedVersion.Minor != expectedVersion.Minor
            || Math.Max(signedVersion.Build, 0) != Math.Max(expectedVersion.Build, 0))
            throw new InvalidDataException("The signed dependency manifest version does not match setup.");
        return manifest;
    }

    private static int LaunchElevated(
        IEnumerable<string> arguments,
        SetupExecutionContext execution,
        string? executable = null)
    {
        executable ??= CurrentExecutable();
        string[] elevatedArguments = WindowsSetupElevationArguments.Prepare(
            arguments, execution.CliMode);
        if (execution.CliMode)
            return Elevation.RelaunchAsAdministrator(executable, elevatedArguments);
        using Process process = Elevation.StartAsAdministrator(executable, elevatedArguments);
        execution.DelegatedToElevatedWizard = true;
        return 0;
    }
    private static async Task InstallPayloadAsync(
        string payload,
        string destination,
        Version version,
        SetupExecutionContext execution,
        bool resumeOnly = false)
    {
        InstallationJournal? existing = await new JournalStore().LoadAsync(Path.Combine(
            destination, SetupProductIdentity.InstallJournalFileName));
        if (existing?.PendingDependencyOperation is
            {
                Phase: DependencyOperationPhase.InstallerStarted or DependencyOperationPhase.RestartRequired
                or DependencyOperationPhase.DeferredUntilRestart
            })
        {
            if (!Version.TryParse(existing.Version, out Version? installedVersion)
                || !SameRelease(installedVersion, version))
                throw new InvalidOperationException(
                    $"Setup {version} cannot resume the unfinished {existing.Version} installation. Finish or repair the existing installation first.");
            execution.Report("Resuming controller component setup after restart...");
            return;
        }
        if (resumeOnly)
            throw new InvalidOperationException("The setup resume state is no longer pending. Launch the original setup package to install or repair the application.");
        execution.Report("Installing AFGC PC Manager...");
        await new ApplicationInstaller(new JournalStore())
            .InstallAsync(payload, destination, version);
        WindowsInstallationRegistration.Register(destination, version);
        execution.Report($"Installed successfully to {destination}");
    }
    private static bool SameRelease(Version left, Version right) => left.Major == right.Major
        && left.Minor == right.Minor && Math.Max(left.Build, 0) == Math.Max(right.Build, 0);
    private static string CurrentExecutable() => Environment.ProcessPath
        ?? throw new InvalidOperationException("The setup executable path is unavailable.");
    private static void CleanupDurableLocalResume(string? directory)
    {
        if (!string.IsNullOrWhiteSpace(directory)) TryCleanupDirectory(directory);
    }
    private static void TryCleanupDirectory(string directory)
    {
        try { DurableSetupStaging.Cleanup(directory); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
    }
    private static void CleanupDownloadedSetupSource(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return;
        try
        {
            string fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
            string expectedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(
                Path.Combine(SetupProductIdentity.TemporaryDirectory, "updates")));
            if (fullPath.StartsWith(expectedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                TryCleanupDirectory(fullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
    }
    private static void WriteFailureDiagnostic(Exception error)
    {
        try
        {
            string directory = SetupProductIdentity.TemporaryDirectory;
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "setup-error.log"),
                $"{DateTimeOffset.Now:O}{Environment.NewLine}{Environment.CommandLine}{Environment.NewLine}{error}");
        }
        catch { }
    }
    [DllImport("kernel32.dll")] private static extern bool AttachConsole(uint processId);
}
