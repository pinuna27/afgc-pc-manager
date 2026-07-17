using System.Diagnostics;
using AFGCPCManager.Setup.Core.Dependencies;
using AFGCPCManager.Setup.Core.Installation;
using AFGCPCManager.Setup.Core.Models;

namespace AFGCPCManager.Uninstaller;

internal static class Program
{
    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        try
        {
            string? detachedRoot = Get(args, "--detached");
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
                var elevatedArguments = new List<string> { "--detached", root };
                if (form.Options.UninstallVJoy) elevatedArguments.Add("--remove-vjoy");
                if (form.Options.UninstallHidHide) elevatedArguments.Add("--remove-hidhide");
                return Elevation.RelaunchAsAdministrator(temporary, elevatedArguments);
            }
            if (!Elevation.IsAdministrator()) return Elevation.RelaunchAsAdministrator(Environment.ProcessPath!, args);
            string application = Path.Combine(detachedRoot, "AFGCPCManager.exe");
            if (File.Exists(application))
            {
                using Process recovery = Process.Start(new ProcessStartInfo(application, "--recover-hidhide") { UseShellExecute = false }) ?? throw new InvalidOperationException("Could not start physical-controller recovery.");
                await recovery.WaitForExitAsync();
                if (recovery.ExitCode != 0) throw new InvalidOperationException("Physical-controller recovery failed; uninstall was stopped for safety.");
            }
            var dependencyUninstaller = new RegisteredDependencyUninstaller();
            bool restartRequired = false;
            if (Has(args, "--remove-vjoy")) restartRequired |= await RemoveDependencyAsync(dependencyUninstaller, DependencyId.VJoy);
            if (Has(args, "--remove-hidhide")) restartRequired |= await RemoveDependencyAsync(dependencyUninstaller, DependencyId.HidHide);
            string journalPath = Path.Combine(detachedRoot, "install-journal.json");
            UninstallResult result = await new ApplicationUninstaller(new JournalStore()).UninstallOwnedFilesAsync(journalPath);
            WindowsInstallationRegistration.Unregister();
            File.Delete(journalPath);
            foreach (string directory in Directory.EnumerateDirectories(detachedRoot, "*", SearchOption.AllDirectories).OrderByDescending(x => x.Length))
                if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
            if (!Directory.EnumerateFileSystemEntries(detachedRoot).Any()) Directory.Delete(detachedRoot);
            Console.WriteLine($"Removed {result.RemovedFiles} files.");
            if (result.PreservedModifiedFiles.Count > 0) Console.WriteLine($"Preserved {result.PreservedModifiedFiles.Count} modified files.");
            return restartRequired ? 3010 : 0;
        }
        catch (Exception ex) { MessageBox.Show($"Uninstall failed: {ex.Message}", "AFGC PC Manager", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
    }
    private static async Task<bool> RemoveDependencyAsync(RegisteredDependencyUninstaller uninstaller, DependencyId dependency)
    {
        int exitCode = await uninstaller.UninstallInteractiveAsync(dependency);
        if (exitCode is not (0 or 1641 or 3010))
            throw new InvalidOperationException($"The {dependency} uninstaller exited with code {exitCode}.");
        return exitCode is 1641 or 3010;
    }
    private static bool Has(string[] args, string key) => args.Contains(key, StringComparer.OrdinalIgnoreCase);
    private static string? Get(string[] args, string key) { int index = Array.IndexOf(args, key); return index >= 0 && index + 1 < args.Length ? args[index + 1] : null; }
}
