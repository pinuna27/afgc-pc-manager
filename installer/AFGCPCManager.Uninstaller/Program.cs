using System.Diagnostics;
using System.Runtime.InteropServices;
using AFGCPCManager.Setup.Core.Dependencies;
using AFGCPCManager.Setup.Core.Installation;
using AFGCPCManager.Setup.Core.Models;

namespace AFGCPCManager.Uninstaller;

internal static class Program
{
    private const string DetachedArgument = "--detached";
    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        try
        {
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
                string temporary = Path.Combine(temporaryDirectory, Path.GetFileName(Environment.ProcessPath!));
                var elevatedArguments = new List<string> { "--wizard-run", DetachedArgument, root };
                if (form.Options.UninstallVJoy) elevatedArguments.Add("--remove-vjoy");
                if (form.Options.UninstallHidHide) elevatedArguments.Add("--remove-hidhide");
                return Elevation.RelaunchAsAdministrator(temporary, elevatedArguments);
            }
            if (!Elevation.IsAdministrator()) return Elevation.RelaunchAsAdministrator(Environment.ProcessPath!, args);
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
            string detachedRoot = Get(args, DetachedArgument) ?? throw new ArgumentException("The installed application path is missing.");
            string continuationExecutable = EnsureDurableContinuationCopy();
            var continuationArguments = args.ToList();
            string application = Path.Combine(detachedRoot, "AFGCPCManager.exe");
            bool hidHideInstalled = new WindowsDependencyDetector().Detect(DependencyId.HidHide).IsInstalled;
            if (File.Exists(application) && hidHideInstalled)
            {
                Report("Restoring physical controller visibility...", progress);
                using Process recovery = Process.Start(new ProcessStartInfo(application, "--recover-hidhide") { UseShellExecute = false }) ?? throw new InvalidOperationException("Could not start physical-controller recovery.");
                await recovery.WaitForExitAsync();
                if (recovery.ExitCode != 0) throw new InvalidOperationException("Physical-controller recovery failed; uninstall was stopped for safety.");
            }
            else if (!hidHideInstalled) Report("HidHide is not installed; physical controllers are already visible.", progress);
            var dependencyUninstaller = new RegisteredDependencyUninstaller();
            bool restartRequired = false;
            if (Has(args, "--remove-vjoy"))
            {
                Report("Removing vJoy... Follow the vendor uninstaller prompts.", progress);
                WindowsUninstallResumeRegistration.Register(continuationExecutable, continuationArguments);
                DependencyRemovalResult removal = await RemoveDependencyAsync(dependencyUninstaller, DependencyId.VJoy);
                restartRequired |= removal.RestartRequired;
                if (removal.RestartInitiated) return 3010;
            }
            if (Has(args, "--remove-hidhide"))
            {
                Report("Removing HidHide... Follow the vendor uninstaller prompts.", progress);
                WindowsUninstallResumeRegistration.Register(continuationExecutable, continuationArguments);
                DependencyRemovalResult removal = await RemoveDependencyAsync(dependencyUninstaller, DependencyId.HidHide);
                restartRequired |= removal.RestartRequired;
                if (removal.RestartInitiated) return 3010;
            }
            string journalPath = Path.Combine(detachedRoot, "install-journal.json");
            Report("Removing AFGC PC Manager application files...", progress);
            UninstallResult result = await new ApplicationUninstaller(new JournalStore()).UninstallOwnedFilesAsync(journalPath);
            WindowsInstallationRegistration.Unregister();
            File.Delete(journalPath);
            foreach (string directory in Directory.EnumerateDirectories(detachedRoot, "*", SearchOption.AllDirectories).OrderByDescending(x => x.Length))
                if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
            if (!Directory.EnumerateFileSystemEntries(detachedRoot).Any()) Directory.Delete(detachedRoot);
            WindowsUninstallResumeRegistration.Unregister();
            CleanupDurableContinuation();
            Report($"Removed {result.RemovedFiles} files.", progress);
            if (result.PreservedModifiedFiles.Count > 0) Report($"Preserved {result.PreservedModifiedFiles.Count} modified files.", progress);
            return restartRequired ? 3010 : 0;
        }
        catch { try { WindowsUninstallResumeRegistration.Unregister(); } catch { } throw; }
    }
    private static async Task<DependencyRemovalResult> RemoveDependencyAsync(RegisteredDependencyUninstaller uninstaller, DependencyId dependency)
    {
        if (uninstaller.Find(dependency) is null) return new(false, false);
        int exitCode = await uninstaller.UninstallInteractiveAsync(dependency);
        if (exitCode is not (0 or 1641 or 3010))
            throw new InvalidOperationException($"The {dependency} uninstaller exited with code {exitCode}.");
        return new(exitCode is 1641 or 3010, exitCode == 1641);
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
        return Path.Combine(directory, Path.GetFileName(Environment.ProcessPath!));
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
    private static bool Has(string[] args, string key) => args.Contains(key, StringComparer.OrdinalIgnoreCase);
    private static string? Get(string[] args, string key) { int index = Array.IndexOf(args, key); return index >= 0 && index + 1 < args.Length ? args[index + 1] : null; }
    private static void Report(string message, Action<string>? progress) { Console.WriteLine(message); progress?.Invoke(message); }
    private sealed record DependencyRemovalResult(bool RestartRequired, bool RestartInitiated);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool MoveFileEx(string existingFileName, string? newFileName, int flags);
}
