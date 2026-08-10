using System.Diagnostics;
using System.Runtime.InteropServices;
using AFGCPCManager.Setup.Core.Dependencies;
using AFGCPCManager.Setup.Core.Installation;
using AFGCPCManager.Setup.Core.Models;

namespace AFGCPCManager.Uninstaller;

internal static class Program
{
    private const string DetachedArgument = "--detached";
    private const string BootstrapTempArgument = "--bootstrap-temp-dir";
    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        try
        {
            args = WindowsResumeArguments.Expand(args);
            string? detachedRoot = Get(args, DetachedArgument);
            if (detachedRoot is null)
            {
                string root = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
                InstallationJournal journal = new JournalStore().LoadAsync(Path.Combine(root, "install-journal.json")).GetAwaiter().GetResult()
                    ?? throw new InvalidOperationException("Installation journal is missing or invalid.");
                ApplicationConfiguration.Initialize();
                using var form = new UninstallForm(DependencyUninstallOptions.FromJournal(journal));
                if (form.ShowDialog() != DialogResult.OK) return 0;
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
                if (form.Options.UninstallHidHide) elevatedArguments.Add("--remove-hidhide");
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
            if (!Elevation.IsAdministrator()) return Elevation.RelaunchAsAdministrator(CurrentExecutable(), args);
            if (Has(args, "--wizard-run"))
            {
                ApplicationConfiguration.Initialize();
                using var progress = new UninstallProgressForm(args, ExecuteDetachedAsync);
                Application.Run(progress); return progress.ResultCode;
            }
            return await ExecuteDetachedAsync(args);
        }
        catch (Exception ex) { try { WindowsUninstallResumeRegistration.Unregister(); } catch { } MessageBox.Show($"Uninstall failed: {ex.Message}", "AFGC PC Manager", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
    }

    internal static async Task<int> ExecuteDetachedAsync(string[] args, Action<string>? progress = null)
    {
        try
        {
            using LifecycleFileLock lifecycle = LifecycleFileLock.Acquire();
            string detachedRoot = Get(args, DetachedArgument) ?? throw new ArgumentException("The installed application path is missing.");
            var continuationArguments = args.ToList();
            bool removeVJoy = Has(args, "--remove-vjoy");
            bool removeHidHide = Has(args, "--remove-hidhide");
            string? continuationExecutable = removeVJoy || removeHidHide
                ? EnsureDurableContinuationCopy()
                : null;
            string application = Path.Combine(detachedRoot, "AFGCPCManager.exe");
            bool hidHideInstalled = new WindowsDependencyDetector().Detect(DependencyId.HidHide).IsInstalled;
            if (File.Exists(application) && hidHideInstalled)
            {
                Report("Restoring physical controller visibility...", progress);
                using Process recovery = Process.Start(new ProcessStartInfo(application, "--recover-hidhide") { UseShellExecute = false }) ?? throw new InvalidOperationException("Could not start physical-controller recovery.");
                using var recoveryTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                try { await recovery.WaitForExitAsync(recoveryTimeout.Token); }
                catch (OperationCanceledException)
                { throw new TimeoutException("Physical-controller recovery did not finish within 30 seconds; uninstall was stopped for safety."); }
                if (recovery.ExitCode != 0) throw new InvalidOperationException("Physical-controller recovery failed; uninstall was stopped for safety.");
            }
            else if (!hidHideInstalled) Report("HidHide is not installed; physical controllers are already visible.", progress);
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
                if (removal.RestartInitiated) return 3010;
            }
            if (removeHidHide)
            {
                Report("Removing HidHide... Follow the vendor uninstaller prompts.", progress);
                DependencyRemovalExecutionResult removal = await removalCoordinator.RemoveAsync(
                    DependencyId.HidHide, continuationArguments);
                restartRequired |= removal.RestartRequired;
                continuationArguments = removal.ContinuationArguments;
                if (removal.RestartInitiated) return 3010;
            }
            string journalPath = Path.Combine(detachedRoot, "install-journal.json");
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
            return restartRequired ? 3010 : 0;
        }
        catch { try { WindowsUninstallResumeRegistration.Unregister(); } catch { } throw; }
        finally { CleanupBootstrapCopy(Get(args, BootstrapTempArgument)); }
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
    private static bool Has(string[] args, string key) => args.Contains(key, StringComparer.OrdinalIgnoreCase);
    private static string? Get(string[] args, string key) { int index = Array.FindIndex(args, value => value.Equals(key, StringComparison.OrdinalIgnoreCase)); return index >= 0 && index + 1 < args.Length ? args[index + 1] : null; }
    private static string CurrentExecutable() => Environment.ProcessPath
        ?? throw new InvalidOperationException("The uninstaller executable path is unavailable.");
    private static void Report(string message, Action<string>? progress) { Console.WriteLine(message); progress?.Invoke(message); }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool MoveFileEx(string existingFileName, string? newFileName, int flags);
}
