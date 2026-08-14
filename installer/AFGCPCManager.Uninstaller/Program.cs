using System.Diagnostics;
using System.Runtime.InteropServices;
using AFGCPCManager.Setup.Core.Dependencies;
using AFGCPCManager.Setup.Core.Installation;
using AFGCPCManager.Setup.Core;
using AFGCPCManager.Setup.Core.Models;

namespace AFGCPCManager.Uninstaller;

internal static class Program
{
    private const string DetachedArgument = "--detached";
    private const string BootstrapTempArgument = "--bootstrap-temp-dir";
    private const int HidHideRecoveryNoChangesExitCode = 2;
    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        try
        {
            args = WindowsResumeArguments.Expand(args);
            string? detachedRoot = CommandLineArguments.Get(args, DetachedArgument);
            if (detachedRoot is null)
            {
                string root = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
                InstallationJournal journal = new JournalStore().LoadAsync(Path.Combine(
                    root, SetupProductIdentity.InstallJournalFileName)).GetAwaiter().GetResult()
                    ?? throw new InvalidOperationException("Installation journal is missing or invalid.");
                ApplicationConfiguration.Initialize();
                using var form = new UninstallForm(DependencyUninstallOptions.FromJournal(journal));
                if (form.ShowDialog() != DialogResult.OK)
                    return 0;
                string temporaryDirectory = Path.Combine(Path.GetTempPath(), $"AFGCPCManager.Uninstaller.{Guid.NewGuid():N}");
                Directory.CreateDirectory(temporaryDirectory);
                foreach (string source in Directory.EnumerateFiles(AppContext.BaseDirectory))
                {
                    string name = Path.GetFileName(source);
                    if (name.StartsWith("AFGCPCManager.Uninstaller", StringComparison.OrdinalIgnoreCase) ||
                        name.StartsWith("AFGCPCManager.Setup.Core", StringComparison.OrdinalIgnoreCase))
                        File.Copy(source, Path.Combine(temporaryDirectory, name));
                }
                string temporary = Path.Combine(temporaryDirectory, Path.GetFileName(CurrentExecutable()));
                var elevatedArguments = new List<string>
                {
                    "--wizard-run", DetachedArgument, root, BootstrapTempArgument, temporaryDirectory
                };
                if (form.Options.UninstallVJoy) elevatedArguments.Add("--remove-vjoy");
                if (form.Options.UninstallViGEmBus) elevatedArguments.Add("--remove-vigembus");
                if (form.Options.UninstallHidHide) elevatedArguments.Add("--remove-hidhide");
                if (form.Options.RemoveApplicationData) elevatedArguments.Add("--remove-data");
                try
                {
                    using Process elevated = Elevation.StartAsAdministrator(temporary, elevatedArguments);
                    return 0;
                }
                catch
                {
                    try { Directory.Delete(temporaryDirectory, recursive: true); } catch { }
                    throw;
                }
            }
            if (!Elevation.IsAdministrator())
                return Elevation.RelaunchAsAdministrator(CurrentExecutable(), args);
            if (CommandLineArguments.Has(args, "--wizard-run"))
            {
                ApplicationConfiguration.Initialize();
                using var progress = new UninstallProgressForm(args, ExecuteDetachedAsync);
                Application.Run(progress);
                return progress.ResultCode;
            }
            return (await ExecuteDetachedAsync(args)).ExitCode;
        }
        catch (Exception ex)
        {
            try { WindowsUninstallResumeRegistration.Unregister(); }
            catch (Exception cleanupError) when (cleanupError is IOException
                                                  or UnauthorizedAccessException
                                                  or System.Security.SecurityException)
            { }
            MessageBox.Show($"Uninstall failed: {ex.Message}", "AFGC PC Manager",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    internal static async Task<UninstallExecutionResult> ExecuteDetachedAsync(
        string[] args,
        Action<string>? progress = null)
    {
        try
        {
            using LifecycleFileLock lifecycle = LifecycleFileLock.Acquire();
            string detachedRoot = CommandLineArguments.Get(args, DetachedArgument)
                ?? throw new ArgumentException("The installed application path is missing.");
            var continuationArguments = args.ToList();
            bool removeVJoy = CommandLineArguments.Has(args, "--remove-vjoy");
            bool removeViGEmBus = CommandLineArguments.Has(args, "--remove-vigembus");
            bool removeHidHide = CommandLineArguments.Has(args, "--remove-hidhide");
            bool removeApplicationData = CommandLineArguments.Has(args, "--remove-data");
            string? continuationExecutable = removeVJoy || removeViGEmBus || removeHidHide
                ? EnsureDurableContinuationCopy()
                : null;
            string application = Path.Combine(detachedRoot, "AFGCPCManager.exe");
            bool hidHideInstalled = new WindowsDependencyDetector().Detect(DependencyId.HidHide).IsInstalled;
            ControllerVisibilityOutcome controllerVisibility;
            if (File.Exists(application))
            {
                Report("Restoring controller defaults...", progress);
                string preparationArguments = hidHideInstalled
                    ? "--reset-lights --recover-hidhide"
                    : "--reset-lights";
                using Process recovery = Process.Start(new ProcessStartInfo(
                    application, preparationArguments)
                { UseShellExecute = false }) ?? throw new InvalidOperationException(
                    "Could not start controller cleanup.");
                using var recoveryTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                try { await recovery.WaitForExitAsync(recoveryTimeout.Token); }
                catch (OperationCanceledException)
                { throw new TimeoutException("Controller cleanup did not finish within 30 seconds; uninstall was stopped for safety."); }
                controllerVisibility = !hidHideInstalled
                    ? recovery.ExitCode == 0
                        ? ControllerVisibilityOutcome.HidHideNotInstalled
                        : throw new InvalidOperationException(
                            "Controller cleanup failed; uninstall was stopped for safety.")
                    : recovery.ExitCode switch
                    {
                        0 => ControllerVisibilityOutcome.Restored,
                        HidHideRecoveryNoChangesExitCode => ControllerVisibilityOutcome.NoOwnedEntries,
                        _ => throw new InvalidOperationException(
                            "Physical-controller recovery failed; uninstall was stopped for safety.")
                    };
            }
            else if (!hidHideInstalled)
            {
                controllerVisibility = ControllerVisibilityOutcome.HidHideNotInstalled;
            }
            else throw new InvalidOperationException(
                "Physical-controller visibility cannot be restored because AFGCPCManager.exe is missing. Repair the application before uninstalling.");
            var dependencyUninstaller = new RegisteredDependencyUninstaller();
            var removalCoordinator = new DependencyRemovalCoordinator(dependencyUninstaller,
                arguments => WindowsUninstallResumeRegistration.Register(continuationExecutable!, arguments));
            bool restartRequired = false;
            if (removeVJoy)
            {
                Report("Removing vJoy... Follow the vendor uninstaller prompts.", progress);
                DependencyRemovalExecutionResult removal = await removalCoordinator.RemoveAsync(
                    DependencyId.VJoy, continuationArguments);
                restartRequired |= removal.RestartRequired;
                continuationArguments = removal.ContinuationArguments;
                if (removal.RestartInitiated) return new(3010, controllerVisibility, true);
                DependencyState remainingVJoy = new WindowsDependencyDetector().Detect(DependencyId.VJoy);
                if (VJoyResidualCleanup.CanClean(remainingVJoy))
                {
                    Report("Removing residue left by the vJoy vendor uninstaller...", progress);
                    VJoyResidualCleanupResult cleanup = await VJoyResidualCleanup.CleanupAsync(remainingVJoy);
                    restartRequired |= cleanup.RestartRequired;
                    Report(cleanup.RestartRequired
                        ? "The remaining vJoy driver will be removed after restart."
                        : "The remaining vJoy service and driver were removed.", progress);
                }
            }
            if (removeViGEmBus)
            {
                Report("Removing ViGEmBus... Follow the vendor uninstaller prompts.", progress);
                DependencyRemovalExecutionResult removal = await removalCoordinator.RemoveAsync(
                    DependencyId.ViGEmBus, continuationArguments);
                restartRequired |= removal.RestartRequired;
                continuationArguments = removal.ContinuationArguments;
                if (removal.RestartInitiated) return new(3010, controllerVisibility, true);
            }
            if (removeHidHide)
            {
                Report("Removing HidHide... Follow the vendor uninstaller prompts.", progress);
                DependencyRemovalExecutionResult removal = await removalCoordinator.RemoveAsync(
                    DependencyId.HidHide, continuationArguments);
                restartRequired |= removal.RestartRequired;
                continuationArguments = removal.ContinuationArguments;
                if (removal.RestartInitiated) return new(3010, controllerVisibility, true);
            }
            string journalPath = Path.Combine(
                detachedRoot, SetupProductIdentity.InstallJournalFileName);
            Report("Removing AFGC PC Manager application files...", progress);
            var journalStore = new JournalStore();
            InstallationJournal installedJournal = await journalStore.LoadAsync(journalPath)
                ?? throw new InvalidOperationException("Installation journal is missing or invalid.");
            UninstallResult result;
            try
            {
                WindowsInstallationRegistration.Unregister();
                result = await new ApplicationUninstaller(journalStore).UninstallOwnedFilesAsync(journalPath);
            }
            catch
            {
                if (Version.TryParse(installedJournal.Version, out Version? installedVersion))
                    try { WindowsInstallationRegistration.Register(detachedRoot, installedVersion); } catch { }
                throw;
            }
            try
            {
                File.SetAttributes(journalPath, FileAttributes.Normal);
                File.Delete(journalPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { Report($"The obsolete installation journal could not be removed: {ex.Message}", progress); }
            TryRemoveEmptyDirectories(detachedRoot);
            WindowsUninstallResumeRegistration.Unregister();
            CleanupDurableContinuation();
            Report($"Removed {result.RemovedFiles} files.", progress);
            if (result.PreservedFiles.Count > 0) Report($"Preserved {result.PreservedFiles.Count} modified or user-owned files.", progress);
            if (removeApplicationData)
            {
                Report("Removing settings, saved controllers, and diagnostic logs...", progress);
                RemoveApplicationData();
                Report("Removed AFGC PC Manager application data.", progress);
            }
            return new(restartRequired ? 3010 : 0, controllerVisibility);
        }
        catch { try { WindowsUninstallResumeRegistration.Unregister(); } catch { } throw; }
        finally
        {
            CleanupBootstrapCopy(CommandLineArguments.Get(args, BootstrapTempArgument));
        }
    }
    private static void RemoveApplicationData()
    {
        string localData = Path.TrimEndingDirectorySeparator(Path.GetFullPath(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)));
        string directory = Path.GetFullPath(Path.Combine(localData, "AFGC PC Manager"));
        if (!directory.StartsWith(localData + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(directory).Equals("AFGC PC Manager",
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The AFGC application-data directory could not be resolved safely.");
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
    private static string EnsureDurableContinuationCopy()
    {
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AFGC PC Manager", "Uninstall");
        Directory.CreateDirectory(directory);
        foreach (string source in Directory.EnumerateFiles(AppContext.BaseDirectory))
        {
            string name = Path.GetFileName(source);
            if (name.StartsWith("AFGCPCManager.Uninstaller", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("AFGCPCManager.Setup.Core", StringComparison.OrdinalIgnoreCase))
            {
                string destination = Path.Combine(directory, name);
                if (!Path.GetFullPath(source).Equals(Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
                    File.Copy(source, destination, overwrite: true);
            }
        }
        return Path.Combine(directory, Path.GetFileName(CurrentExecutable()));
    }
    private static void CleanupDurableContinuation()
    {
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AFGC PC Manager", "Uninstall");
        if (!Directory.Exists(directory)) return;
        foreach (string file in Directory.EnumerateFiles(directory))
        {
            try { File.Delete(file); }
            catch (IOException) { _ = MoveFileEx(file, null, 4); }
            catch (UnauthorizedAccessException) { _ = MoveFileEx(file, null, 4); }
        }
        try { Directory.Delete(directory); }
        catch (IOException) { _ = MoveFileEx(directory, null, 4); }
        catch (UnauthorizedAccessException) { _ = MoveFileEx(directory, null, 4); }
    }
    private static void TryRemoveEmptyDirectories(string root)
    {
        try
        {
            foreach (string directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                         .OrderByDescending(path => path.Length))
                if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
            if (Directory.Exists(root) && !Directory.EnumerateFileSystemEntries(root).Any()) Directory.Delete(root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
    private static void CleanupBootstrapCopy(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return;
        try
        {
            string fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
            string tempRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
            if (!fullPath.StartsWith(tempRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(fullPath).StartsWith("AFGCPCManager.Uninstaller.", StringComparison.OrdinalIgnoreCase)
                || !Directory.Exists(fullPath)) return;
            foreach (string file in Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories))
            {
                try { File.Delete(file); }
                catch (IOException) { _ = MoveFileEx(file, null, 4); }
                catch (UnauthorizedAccessException) { _ = MoveFileEx(file, null, 4); }
            }
            foreach (string child in Directory.EnumerateDirectories(fullPath, "*", SearchOption.AllDirectories)
                         .OrderByDescending(path => path.Length))
            {
                try { Directory.Delete(child); }
                catch (IOException) { _ = MoveFileEx(child, null, 4); }
                catch (UnauthorizedAccessException) { _ = MoveFileEx(child, null, 4); }
            }
            try { Directory.Delete(fullPath); }
            catch (IOException) { _ = MoveFileEx(fullPath, null, 4); }
            catch (UnauthorizedAccessException) { _ = MoveFileEx(fullPath, null, 4); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
    }
    private static string CurrentExecutable() => Environment.ProcessPath
        ?? throw new InvalidOperationException("The uninstaller executable path is unavailable.");
    private static void Report(string message, Action<string>? progress) { Console.WriteLine(message); progress?.Invoke(message); }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool MoveFileEx(string existingFileName, string? newFileName, int flags);
}
