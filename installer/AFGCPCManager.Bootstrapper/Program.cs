using System.Diagnostics;
using AFGCPCManager.Setup.Core.Installation;
using AFGCPCManager.Setup.Core.Dependencies;
using AFGCPCManager.Setup.Core.Models;
using AFGCPCManager.Setup.Core.Security;
using AFGCPCManager.Setup.Core.Updates;
using AFGCPCManager.VJoy;
using AFGCPCManager.ViGEm;
using AFGCPCManager.HidHide;
using System.Runtime.InteropServices;

namespace AFGCPCManager.Bootstrapper;

internal static class Program
{
    private const string SetupAsset = "AFGCPCManager-Setup-x64.exe", PayloadAsset = "AFGCPCManager-x64.zip";
    private const string InternalVJoyProbeArgument = "--internal-vjoy-probe";
    private const string InternalViGEmProbeArgument = "--internal-vigembus-probe";
    private const string InternalVJoyProvisionArgument = "--internal-vjoy-provision";
    internal static Action<string>? Progress { get; set; }
    internal static string? LastError { get; private set; }
    internal static bool DelegatedToElevatedWizard { get; private set; }
    private static bool CliMode { get; set; }

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0].Equals(
                InternalVJoyProbeArgument, StringComparison.OrdinalIgnoreCase))
            return ExitAfterVJoyUse(RunInternalVJoyProbe());
        if (args.Length == 1 && args[0].Equals(
                InternalViGEmProbeArgument, StringComparison.OrdinalIgnoreCase))
            return ExitAfterVJoyUse(RunInternalViGEmProbe());
        if (args.Length == 2 && args[0].Equals(
                InternalVJoyProvisionArgument, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(args[1], out int requiredVJoyDevices))
            return ExitAfterVJoyUse(RunInternalVJoyProvision(requiredVJoyDevices));

        try { args = WindowsResumeArguments.Expand(args); }
        catch (Exception ex)
        {
            try { WindowsSetupResumeRegistration.Unregister(); } catch { }
            WriteFailureDiagnostic(ex);
            if (Has(args, "--cli"))
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
        if (Has(args, "--cli"))
        {
            CliMode = true;
            _ = AttachConsole(unchecked((uint)-1));
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
            Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
            int result = RunCoreAsync(args.Where(x =>
                !x.Equals("--cli", StringComparison.OrdinalIgnoreCase))
                .ToArray()).GetAwaiter().GetResult();
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

    internal static async Task<int> RunCoreAsync(string[] args)
    {
        LastError = null;
        DelegatedToElevatedWizard = false;
        try
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("AFGC PC Manager supports Windows only.");
            string destination = Get(args, "--install-dir") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "AFGC PC Manager");
            if (Has(args, "--update") || Has(args, "--repair")) return await DownloadAndLaunchAsync(destination);
            if (Get(args, "--apply-archive") is string archive)
            {
                bool cleanupSource = Elevation.IsAdministrator();
                try
                {
                    return await ApplyArchiveAsync(
                        archive, destination,
                        Version.Parse(Get(args, "--version") ?? throw new ArgumentException("The signed release version is missing.")),
                        Get(args, "--manifest"), Get(args, "--signature"));
                }
                finally
                {
                    if (cleanupSource) CleanupDownloadedSetupSource(Get(args, "--cleanup-source-dir"));
                }
            }
            string payload = Get(args, "--payload") ?? Path.Combine(AppContext.BaseDirectory, "payload");
            bool resumeOnly = Has(args, "--resume-only");
            if (!Directory.Exists(payload))
            {
                if (resumeOnly) throw new DirectoryNotFoundException("The durable setup resume payload is missing.");
                return await DownloadAndLaunchAsync(destination);
            }
            if (!Elevation.IsAdministrator()) return LaunchElevated(args);
            using LifecycleFileLock lifecycle = LifecycleFileLock.Acquire();
            Version version = typeof(Program).Assembly.GetName().Version ?? new(0, 1, 0);
            await StopRunningApplicationAsync(destination);
            await InstallPayloadAsync(payload, destination, version, resumeOnly);
            ReleaseManifest? localManifest = await LoadSignedDependencyManifestAsync(
                Get(args, "--manifest"), Get(args, "--signature"), version);
            DurableLocalSetupBundle? stagedResume = null;
            string? durableResumeDirectory = Get(args, "--durable-local-dir");
            try
            {
                bool restartRequired = await EnsureDependenciesAsync(destination, version, localManifest,
                    () =>
                    {
                        stagedResume ??= DurableSetupStaging.StageLocal(version, CurrentExecutable(),
                            Get(args, "--manifest"), Get(args, "--signature"));
                        var resume = new List<string>
                        {
                            "--wizard-run", "--resume-only", "--payload", stagedResume.PayloadDirectory,
                            "--install-dir", destination, "--durable-local-dir", stagedResume.Directory
                        };
                        if (stagedResume.ManifestPath is not null)
                            resume.AddRange(["--manifest", stagedResume.ManifestPath, "--signature", stagedResume.SignaturePath!]);
                        WindowsSetupResumeRegistration.Register(stagedResume.SetupPath, resume);
                    });
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
            LastError = ex.Message;
            WriteFailureDiagnostic(ex);
            Report($"Setup failed: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> DownloadAndLaunchAsync(string destination)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        var verifier = new ReleaseManifestVerifier(TrustedReleaseKeys.LoadAfgcPublicKey()); var client = new GitHubSignedReleaseClient(http, verifier);
        VerifiedRelease release = await client.GetLatestAsync("pinuna27", "afgc-pc-manager");
        string staging = Path.Combine(Path.GetTempPath(), "AFGC PC Manager", "updates", release.Version.ToString()); Directory.CreateDirectory(staging);
        string setup = await client.DownloadAssetAsync(release, SetupAsset, staging); string archive = await client.DownloadAssetAsync(release, PayloadAsset, staging);
        string manifest = Path.Combine(staging, "release-manifest.json");
        string signature = Path.Combine(staging, "release-manifest.sig");
        await File.WriteAllBytesAsync(manifest, release.ManifestBytes.ToArray());
        await File.WriteAllBytesAsync(signature, release.SignatureBytes.ToArray());
        string mode = CliMode ? "--cli" : "--wizard-run";
        return LaunchElevated([mode, "--apply-archive", archive, "--version", release.Version.ToString(),
            "--install-dir", destination, "--manifest", manifest, "--signature", signature,
            "--cleanup-source-dir", staging], setup);
    }

    private static async Task<int> ApplyArchiveAsync(string archive, string destination, Version version, string? manifestPath, string? signaturePath)
    {
        if (!Elevation.IsAdministrator())
        {
            IEnumerable<string> elevatedArguments = Environment.GetCommandLineArgs().Skip(1);
            if (!CliMode) elevatedArguments = elevatedArguments.Append("--wizard-run");
            return LaunchElevated(elevatedArguments);
        }
        using LifecycleFileLock lifecycle = LifecycleFileLock.Acquire();
        Report("Verifying the AFGC PC Manager release...");
        ReleaseManifest? localManifest = null;
        if (manifestPath is not null || signaturePath is not null)
        {
            if (manifestPath is null || signaturePath is null) throw new ArgumentException("Both the local manifest and signature are required.");
            localManifest = await new LocalReleaseBundleVerifier(new ReleaseManifestVerifier(TrustedReleaseKeys.LoadAfgcPublicKey()))
                .VerifyAsync(manifestPath, signaturePath, archive, version);
        }
        DurableSetupBundle durable = DurableSetupStaging.Stage(version, CurrentExecutable(), archive, manifestPath, signaturePath);
        archive = durable.ArchivePath; manifestPath = durable.ManifestPath; signaturePath = durable.SignaturePath;
        Report("Preparing application files...");
        await StopRunningApplicationAsync(destination); string payload = Path.Combine(Path.GetTempPath(), "AFGC PC Manager", "payload", Guid.NewGuid().ToString("N"));
        try
        {
            new PayloadArchiveExtractor().Extract(archive, payload);
            await InstallPayloadAsync(payload, destination, version);
            bool restartRequired = await EnsureDependenciesAsync(destination, version, localManifest, () =>
            {
                var resume = new List<string> { "--wizard-run", "--apply-archive", archive, "--version", version.ToString(), "--install-dir", destination };
                if (manifestPath is not null) resume.AddRange(["--manifest", manifestPath, "--signature", signaturePath!]);
                WindowsSetupResumeRegistration.Register(durable.SetupPath, resume);
            });
            if (restartRequired) { Report("A restart is required before setup can continue."); return 3010; }
            WindowsSetupResumeRegistration.Unregister();
            TryCleanupDirectory(durable.Directory);
            StartApplicationUnelevated(destination); return 0;
        }
        catch
        {
            await RemoveResumeUnlessRestartRequiredAsync(destination);
            if (!await IsRestartRequiredAsync(destination)) TryCleanupDirectory(durable.Directory);
            throw;
        }
        finally { TryCleanupDirectory(payload); }
    }
    private static async Task<bool> EnsureDependenciesAsync(string destination, Version version, ReleaseManifest? trustedManifest, Action registerResume)
    {
        string journalPath = Path.Combine(destination, "install-journal.json");
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
                ? ProbeVJoyOutOfProcess()
                : ProbeViGEmOutOfProcess();
        });
        DependencyState[] installedStates = Enum.GetValues<DependencyId>()
            .Select(detector.Detect).ToArray();
        if (trustedManifest is null
            && DependencyManifestPolicy.CanUseInstalledDependenciesWithoutManifest(
                installedStates, journal))
        {
            Report("Found operational vJoy, ViGEmBus, and HidHide installations; no dependency download is needed.");
            if (journal.DependenciesInstalledBySetup.Contains(DependencyId.VJoy.ToString()))
            {
                Report("Preparing the virtual controller output...");
                ProvisionVJoyOutOfProcess(1);
                Report("Virtual controller output is ready.");
            }
            return false;
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        var client = new GitHubSignedReleaseClient(http, new ReleaseManifestVerifier(TrustedReleaseKeys.LoadAfgcPublicKey()));
        ReleaseManifest manifest;
        if (trustedManifest is null)
        {
            VerifiedRelease release = await client.GetLatestAsync("pinuna27", "afgc-pc-manager");
            if (!SameRelease(release.Version, version)) throw new InvalidDataException("The dependency manifest no longer matches this application release.");
            manifest = release.Manifest;
        }
        else manifest = trustedManifest;
        string staging = Path.Combine(Path.GetTempPath(), "AFGC PC Manager", "dependencies", version.ToString());
        try
        {
        var packages = new Dictionary<DependencyId, (Version Target, string InstallerPath)>();
        bool hasInstallerActions = false;
        if (manifest.VJoy is null || manifest.ViGEmBus is null || manifest.HidHide is null)
            throw new InvalidDataException(
                "The signed release manifest is missing a required controller driver.");
        if (manifest.VJoy is DependencyRelease vjoy) await AddPackageAsync(DependencyId.VJoy, vjoy);
        if (manifest.ViGEmBus is DependencyRelease viGEmBus) await AddPackageAsync(DependencyId.ViGEmBus, viGEmBus);
        if (manifest.HidHide is DependencyRelease hidHide) await AddPackageAsync(DependencyId.HidHide, hidHide);
        if (packages.Count == 0) return false;
        if (hasInstallerActions) registerResume();
        var coordinator = new DependencyCoordinator(detector, new DependencyInstaller(), journalStore, Report);
        DependencyExecutionResult result = await coordinator.EnsureAsync(journalPath, packages, allowUpdates: true);
        if (!result.RestartRequired)
        {
            InstallationJournal updatedJournal = await new JournalStore().LoadAsync(Path.Combine(destination, "install-journal.json"))
                ?? throw new InvalidOperationException("The installation journal is missing after dependency setup.");
            if (updatedJournal.DependenciesInstalledBySetup.Contains(DependencyId.VJoy.ToString()))
            {
                Report("Preparing the virtual controller output...");
                ProvisionVJoyOutOfProcess(1);
                Report("Virtual controller output is ready.");
            }
        }
        return result.RestartRequired;

        async Task AddPackageAsync(DependencyId id, DependencyRelease dependency)
        {
            string name = DependencyNames.DisplayName(id);
            Report($"Checking for {name}...");
            Version target = Version.Parse(dependency.Version.TrimStart('v', 'V'));
            DependencyState state = detector.Detect(id);
            string detectionMessage = state.EffectiveReadiness == DependencyReadiness.Ready
                ? $"Found {name}{(state.Version is null ? "." : $" {state.Version}.")}"
                : state.EffectiveReadiness == DependencyReadiness.Absent ? $"{name} is not installed."
                : $"{name} was detected, but it is not operational yet.";
            Report(detectionMessage);
            DependencyPlan plan = DependencyPlanBuilder.Build(state, target, journal);
            bool needsInstaller = plan.Action is DependencyAction.Install or DependencyAction.Repair or DependencyAction.Update;
            if (needsInstaller) Report($"Downloading the verified {name} installer...");
            string installerPath = needsInstaller ? await client.DownloadDependencyAsync(dependency, staging) : string.Empty;
            if (plan.Action == DependencyAction.Block) Report($"{name} cannot be changed safely: {plan.Reason}");
            else if (!needsInstaller) Report($"{name} is ready; no installation is needed.");
            hasInstallerActions |= needsInstaller;
            packages[id] = (target, installerPath);
        }
        }
        finally { TryCleanupDirectory(staging); }
    }

    private static int RunInternalVJoyProbe()
    {
        try
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
            Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
            VJoyBackend backend;
            try { backend = new VJoyBackend(); }
            catch (Exception ex) when (ex is VJoyException or DllNotFoundException
                or BadImageFormatException or EntryPointNotFoundException)
            {
                Console.Error.WriteLine(ex.Message);
                return VJoyProbeProtocol.UnavailableExitCode;
            }

            if (!backend.IsDriverEnabled)
            {
                Console.Error.WriteLine("The vJoy driver is installed but disabled.");
                return VJoyProbeProtocol.UnhealthyExitCode;
            }

            int compatible = backend.EnumerateDevices().Count(device =>
                device.Capabilities is not null
                && device.Status is AFGCPCManager.Core.Output.OutputDeviceStatus.Free
                    or AFGCPCManager.Core.Output.OutputDeviceStatus.Owned);
            Console.WriteLine($"vJoy is enabled; compatible outputs: {compatible}.");
            return VJoyProbeProtocol.ReadyExitCode;
        }
        catch (Exception ex)
        {
            try { Console.Error.WriteLine(ex.Message); } catch { }
            return VJoyProbeProtocol.UnhealthyExitCode;
        }
    }

    private static int RunInternalViGEmProbe()
    {
        try
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
            Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
            using var backend = new ViGEmBackend();
            int slots = backend.EnumerateDevices().Count;
            Console.WriteLine($"ViGEmBus is operational; Xbox output slots: {slots}.");
            return VJoyProbeProtocol.ReadyExitCode;
        }
        catch (Exception ex) when (ex is ViGEmException or DllNotFoundException
            or BadImageFormatException or EntryPointNotFoundException)
        {
            try { Console.Error.WriteLine(ex.Message); } catch { }
            return VJoyProbeProtocol.UnavailableExitCode;
        }
        catch (Exception ex)
        {
            try { Console.Error.WriteLine(ex.Message); } catch { }
            return VJoyProbeProtocol.UnhealthyExitCode;
        }
    }

    private static int RunInternalVJoyProvision(int requiredCount)
    {
        try
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
            Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
            new VJoyDeviceProvisioner()
                .EnsureCompatibleDeviceCountForProcessLifetimeAsync(requiredCount)
                .GetAwaiter().GetResult();
            Console.WriteLine($"vJoy exposes at least {requiredCount} compatible output(s).");
            return VJoyProbeProtocol.ReadyExitCode;
        }
        catch (Exception ex)
        {
            try { Console.Error.WriteLine(ex.Message); } catch { }
            return VJoyProbeProtocol.UnhealthyExitCode;
        }
    }

    private static DependencyProbeResult ProbeVJoyOutOfProcess()
    {
        var start = new ProcessStartInfo(CurrentExecutable())
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add(InternalVJoyProbeArgument);
        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException("The isolated vJoy readiness probe could not start.");
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> errorTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try { process.WaitForExitAsync(timeout.Token).GetAwaiter().GetResult(); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("The isolated vJoy readiness probe timed out.");
        }

        string output = outputTask.GetAwaiter().GetResult();
        string error = errorTask.GetAwaiter().GetResult();
        string detail = string.IsNullOrWhiteSpace(error) ? output : error;
        return VJoyProbeProtocol.Interpret(process.ExitCode, detail);
    }

    private static DependencyProbeResult ProbeViGEmOutOfProcess()
    {
        var start = new ProcessStartInfo(CurrentExecutable())
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add(InternalViGEmProbeArgument);
        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException("The isolated ViGEmBus readiness probe could not start.");
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> errorTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try { process.WaitForExitAsync(timeout.Token).GetAwaiter().GetResult(); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("The isolated ViGEmBus readiness probe timed out.");
        }

        string output = outputTask.GetAwaiter().GetResult();
        string error = errorTask.GetAwaiter().GetResult();
        string detail = string.IsNullOrWhiteSpace(error) ? output : error;
        return VJoyProbeProtocol.Interpret(process.ExitCode, detail);
    }

    private static void ProvisionVJoyOutOfProcess(int requiredCount)
    {
        var start = new ProcessStartInfo(CurrentExecutable())
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add(InternalVJoyProvisionArgument);
        start.ArgumentList.Add(requiredCount.ToString());
        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException("The isolated vJoy provisioning process could not start.");
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> errorTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try { process.WaitForExitAsync(timeout.Token).GetAwaiter().GetResult(); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("The isolated vJoy provisioning process timed out.");
        }

        string output = outputTask.GetAwaiter().GetResult();
        string error = errorTask.GetAwaiter().GetResult();
        string detail = string.IsNullOrWhiteSpace(error) ? output : error;
        DependencyProbeResult result = VJoyProbeProtocol.Interpret(
            process.ExitCode, detail);
        if (!result.Operational)
            throw new InvalidOperationException(
                result.Detail ?? "The isolated vJoy provisioning process failed.");
    }
    private static async Task RemoveResumeUnlessRestartRequiredAsync(string destination)
    {
        InstallationJournal? journal;
        try { journal = await new JournalStore().LoadAsync(Path.Combine(destination, "install-journal.json")); }
        catch { return; }
        if (journal?.PendingDependencyOperation?.Phase != DependencyOperationPhase.RestartRequired)
        {
            try { WindowsSetupResumeRegistration.Unregister(); }
            catch { }
        }
    }
    private static async Task<bool> IsRestartRequiredAsync(string destination)
    {
        try
        {
            return (await new JournalStore().LoadAsync(Path.Combine(destination, "install-journal.json")))?
                .PendingDependencyOperation?.Phase == DependencyOperationPhase.RestartRequired;
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

    private static int LaunchElevated(IEnumerable<string> arguments, string? executable = null)
    {
        executable ??= CurrentExecutable();
        string[] elevatedArguments = WindowsSetupElevationArguments.Prepare(arguments, CliMode);
        if (CliMode) return Elevation.RelaunchAsAdministrator(executable, elevatedArguments);
        using Process process = Elevation.StartAsAdministrator(executable, elevatedArguments);
        DelegatedToElevatedWizard = true;
        return 0;
    }
    private static async Task InstallPayloadAsync(string payload, string destination, Version version, bool resumeOnly = false)
    {
        InstallationJournal? existing = await new JournalStore().LoadAsync(Path.Combine(destination, "install-journal.json"));
        if (existing?.PendingDependencyOperation is
            { Phase: DependencyOperationPhase.InstallerStarted or DependencyOperationPhase.RestartRequired })
        {
            if (!Version.TryParse(existing.Version, out Version? installedVersion)
                || !SameRelease(installedVersion, version))
                throw new InvalidOperationException(
                    $"Setup {version} cannot resume the unfinished {existing.Version} installation. Finish or repair the existing installation first.");
            Report("Resuming controller component setup after restart...");
            return;
        }
        if (resumeOnly)
            throw new InvalidOperationException("The setup resume state is no longer pending. Launch the original setup package to install or repair the application.");
        Report("Installing AFGC PC Manager..."); await new ApplicationInstaller(new JournalStore()).InstallAsync(payload, destination, version); WindowsInstallationRegistration.Register(destination, version); Report($"Installed successfully to {destination}");
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
            string expectedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(
                Path.GetTempPath(), "AFGC PC Manager", "updates")));
            if (fullPath.StartsWith(expectedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                TryCleanupDirectory(fullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
    }
    private static async Task StopRunningApplicationAsync(string destination)
    {
        string app = Path.Combine(destination, "AFGCPCManager.exe"); if (!File.Exists(app)) return;
        using Process? exitRequest = Process.Start(new ProcessStartInfo(app, "--exit") { UseShellExecute = false });
        if (exitRequest is not null)
        {
            using var exitTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await exitRequest.WaitForExitAsync(exitTimeout.Token); }
            catch (OperationCanceledException) { }
        }
        DateTime deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            using ProcessCollection running = FindInstalledApplicationProcesses(app);
            if (running.Count == 0) return;
            await Task.Delay(200);
        }

        Report("The previous AFGC PC Manager version did not exit; completing a safe forced shutdown...");
        using (ProcessCollection running = FindInstalledApplicationProcesses(app))
        {
            foreach (Process process in running.Processes)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                { throw new InvalidOperationException($"Could not stop the previous AFGC PC Manager process {process.Id}.", ex); }
            }
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            foreach (Process process in running.Processes)
                try { await process.WaitForExitAsync(timeout.Token); }
                catch (OperationCanceledException)
                { throw new TimeoutException("The previous AFGC PC Manager process could not be stopped safely."); }
        }

        string journal = Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData),
            "AFGC PC Manager", "hidhide-journal.json");
        await new HidHideService(new DeviceInstanceResolver(),
            new HidHideJournalStore(journal)).RecoverOwnedEntriesAsync();
        DateTime finalDeadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < finalDeadline)
        {
            using ProcessCollection remaining = FindInstalledApplicationProcesses(app);
            if (remaining.Count == 0) return;
            await Task.Delay(100);
        }
        throw new TimeoutException("AFGC PC Manager remained active after forced shutdown.");
    }

    private static ProcessCollection FindInstalledApplicationProcesses(string applicationPath)
    {
        var matches = new List<Process>();
        foreach (Process candidate in Process.GetProcessesByName("AFGCPCManager"))
        {
            try
            {
                if (string.Equals(candidate.MainModule?.FileName, applicationPath,
                        StringComparison.OrdinalIgnoreCase))
                    matches.Add(candidate);
                else candidate.Dispose();
            }
            catch
            {
                // The process can exit between enumeration and reading MainModule
                // after the cooperative --exit request. That is success, not an
                // unverifiable foreign process.
                try
                {
                    if (candidate.HasExited)
                    {
                        candidate.Dispose();
                        continue;
                    }
                }
                catch (InvalidOperationException)
                {
                    candidate.Dispose();
                    continue;
                }
                candidate.Dispose();
                throw new InvalidOperationException(
                    "Setup could not verify the path of a running AFGC PC Manager process.");
            }
        }
        return new(matches);
    }

    private sealed class ProcessCollection(List<Process> processes) : IDisposable
    {
        public IReadOnlyList<Process> Processes { get; } = processes;
        public int Count => Processes.Count;
        public void Dispose() { foreach (Process process in Processes) process.Dispose(); }
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
    private static void Report(string message) { Console.WriteLine(message); Progress?.Invoke(message); }
    private static void WriteFailureDiagnostic(Exception error)
    {
        try
        {
            string directory = Path.Combine(Path.GetTempPath(), "AFGC PC Manager");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "setup-error.log"),
                $"{DateTimeOffset.Now:O}{Environment.NewLine}{Environment.CommandLine}{Environment.NewLine}{error}");
        }
        catch { }
    }
    [DllImport("kernel32.dll")] private static extern bool AttachConsole(uint processId);
}
