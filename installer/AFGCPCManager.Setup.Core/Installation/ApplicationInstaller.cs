using AFGCPCManager.Setup.Core.Models;
using AFGCPCManager.Setup.Core.Security;
using System.Text.Json;

namespace AFGCPCManager.Setup.Core.Installation;

public sealed class ApplicationInstaller(JournalStore journalStore)
{
    public async Task<InstallationJournal> InstallAsync(string payloadDirectory, string installDirectory, Version version, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(payloadDirectory)) throw new DirectoryNotFoundException("The setup payload is missing.");
        InstallationJournal? previous = null;
        try { previous = await journalStore.LoadAsync(Path.Combine(installDirectory, "install-journal.json"), cancellationToken); } catch (Exception ex) when (ex is InvalidDataException or JsonException) { }
        string staging = installDirectory + ".staging";
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        Directory.CreateDirectory(staging);
        var files = new List<InstalledFile>();
        try
        {
            foreach (string source in Directory.EnumerateFiles(payloadDirectory, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string relative = Path.GetRelativePath(payloadDirectory, source);
                string destination = Path.Combine(staging, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination, true);
                files.Add(new(relative, await Hashing.Sha256Async(destination, cancellationToken)));
            }
            string? backup = null;
            if (Directory.Exists(installDirectory)) { backup = installDirectory + ".previous"; if (Directory.Exists(backup)) Directory.Delete(backup, true); Directory.Move(installDirectory, backup); }
            try { Directory.Move(staging, installDirectory); if (backup is not null) Directory.Delete(backup, true); }
            catch { if (backup is not null && !Directory.Exists(installDirectory)) Directory.Move(backup, installDirectory); throw; }
            var journal = new InstallationJournal { InstallDirectory = installDirectory, Version = version.ToString(), Files = files,
                DependenciesInstalledBySetup = previous?.DependenciesInstalledBySetup ?? [], DependenciesPresentBeforeSetup = previous?.DependenciesPresentBeforeSetup ?? [] };
            await journalStore.SaveAsync(Path.Combine(installDirectory, "install-journal.json"), journal, cancellationToken);
            return journal;
        }
        catch { if (Directory.Exists(staging)) Directory.Delete(staging, true); throw; }
    }
}
