using AFGCPCManager.Core.Updates;
using System.Diagnostics;

namespace AFGCPCManager.App;

internal static class ManagerUpdateLauncher
{
    private const string SetupFileName = "AFGCPCManager.Setup.exe";

    public static ProcessStartInfo CreateStartInfo(string applicationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDirectory);
        string setupPath = Path.GetFullPath(Path.Combine(applicationDirectory, SetupFileName));
        if (!File.Exists(setupPath))
            throw new FileNotFoundException(
                "The installed update helper is missing. Repair AFGC PC Manager, then try again.",
                setupPath);

        var start = new ProcessStartInfo(setupPath)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(setupPath)!
        };
        start.ArgumentList.Add("--update");
        start.ArgumentList.Add("--wizard-run");
        start.ArgumentList.Add("--install-dir");
        start.ArgumentList.Add(Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(applicationDirectory)));
        return start;
    }

    public static bool PromptAndStart(IWin32Window owner,
        UpdateCheckResult.Available update, string applicationDirectory)
    {
        if (update.Component != ReleaseComponent.AfgcPcManager)
            throw new ArgumentException("The selected release is not a Manager update.", nameof(update));

        DialogResult choice = MessageBox.Show(owner,
            $"AFGC PC Manager {update.Latest} is available.\n\n" +
            "Download, verify, and install it now? The Manager will close and reopen when setup finishes. " +
            "Windows may request administrator approval.",
            "Update AFGC PC Manager", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
        if (choice != DialogResult.Yes) return false;

        _ = Process.Start(CreateStartInfo(applicationDirectory))
            ?? throw new InvalidOperationException("Windows did not start the update helper.");
        return true;
    }
}
