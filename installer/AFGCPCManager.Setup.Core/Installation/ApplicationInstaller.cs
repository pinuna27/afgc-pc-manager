using AFGCPCManager.Setup.Core.Models;
using AFGCPCManager.Setup.Core.Security;

namespace AFGCPCManager.Setup.Core.Installation;

public sealed class ApplicationInstaller(JournalStore journalStore)
{
    public async Task<InstallationJournal> InstallAsync(string payloadDirectory, string installDirectory, Version version, CancellationToken cancellationToken = default)
    {
        payloadDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(payloadDirectory));
        installDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installDirectory));
        if (!Directory.Exists(payloadDirectory)) throw new DirectoryNotFoundException("The setup payload is missing.");
        if (installDirectory.Equals(Path.GetPathRoot(installDirectory), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The application cannot be installed at the root of a drive.");
        if (DirectoriesOverlap(payloadDirectory, installDirectory))
            throw new InvalidOperationException("The setup payload and installation directories must not overlap.");

        string staging = installDirectory + ".staging";
        string backup = installDirectory + ".previous";
        RecoverInterruptedSwap(installDirectory, backup);
        InstallationJournal? previous = await journalStore.LoadAsync(
            Path.Combine(installDirectory, SetupProductIdentity.InstallJournalFileName),
            cancellationToken);
        if (previous is null && Directory.Exists(installDirectory)
            && Directory.EnumerateFileSystemEntries(installDirectory).Any())
            throw new InvalidOperationException(
                "The installation directory is not empty and is not owned by AFGC PC Manager.");
        if (previous is not null
            && !Path.TrimEndingDirectorySeparator(Path.GetFullPath(previous.InstallDirectory))
                .Equals(installDirectory, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                "The existing installation journal belongs to a different directory.");
        if (previous?.PendingDependencyOperation is not null)
            throw new InvalidOperationException(
                "The existing driver installation must be completed before application files can be replaced.");
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
                File.SetAttributes(destination, FileAttributes.Normal);
                files.Add(new(relative, await Hashing.Sha256Async(destination, cancellationToken)));
            }

            if (previous is not null)
                await PreserveUnownedAndModifiedRetiredFilesAsync(
                    installDirectory, staging, previous, files, cancellationToken);

            var journal = new InstallationJournal
            {
                InstallDirectory = installDirectory,
                Version = version.ToString(),
                Files = files,
                DependenciesInstalledBySetup = previous is null
                    ? [] : new(previous.DependenciesInstalledBySetup, StringComparer.OrdinalIgnoreCase),
                DependenciesPresentBeforeSetup = previous is null
                    ? [] : new(previous.DependenciesPresentBeforeSetup, StringComparer.OrdinalIgnoreCase)
            };
            await journalStore.SaveAsync(Path.Combine(
                staging, SetupProductIdentity.InstallJournalFileName), journal, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            bool movedExistingInstall = false;
            if (Directory.Exists(installDirectory))
            {
                if (Directory.Exists(backup)) Directory.Delete(backup, true);
                Directory.Move(installDirectory, backup);
                movedExistingInstall = true;
            }
            try { Directory.Move(staging, installDirectory); }
            catch
            {
                if (movedExistingInstall && !Directory.Exists(installDirectory)) Directory.Move(backup, installDirectory);
                throw;
            }
            if (movedExistingInstall) TryDeleteDirectory(backup);
            return journal;
        }
        catch { if (Directory.Exists(staging)) TryDeleteDirectory(staging); throw; }
    }

    private static async Task PreserveUnownedAndModifiedRetiredFilesAsync(
        string installDirectory,
        string staging,
        InstallationJournal previous,
        IReadOnlyCollection<InstalledFile> newFiles,
        CancellationToken cancellationToken)
    {
        var newPaths = new HashSet<string>(newFiles.Select(file => NormalizeRelative(file.RelativePath)), StringComparer.OrdinalIgnoreCase);
        var oldFiles = previous.Files.ToDictionary(file => NormalizeRelative(file.RelativePath), StringComparer.OrdinalIgnoreCase);
        foreach (string source in Directory.EnumerateFiles(installDirectory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relative = NormalizeRelative(Path.GetRelativePath(installDirectory, source));
            if (relative.Equals(SetupProductIdentity.InstallJournalFileName,
                    StringComparison.OrdinalIgnoreCase) || newPaths.Contains(relative)) continue;

            bool preserve = !oldFiles.TryGetValue(relative, out InstalledFile? oldEntry)
                || !string.Equals(await Hashing.Sha256Async(source, cancellationToken), oldEntry.Sha256,
                    StringComparison.OrdinalIgnoreCase);
            if (!preserve) continue;

            string destination = Path.Combine(staging, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: false);
        }
    }

    private static void RecoverInterruptedSwap(string installDirectory, string backup)
    {
        if (!Directory.Exists(installDirectory) && Directory.Exists(backup))
            Directory.Move(backup, installDirectory);
    }

    private static bool DirectoriesOverlap(string left, string right) =>
        left.Equals(right, StringComparison.OrdinalIgnoreCase)
        || left.StartsWith(right + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
        || right.StartsWith(left + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeRelative(string path) => path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

    private static void TryDeleteDirectory(string path)
    {
        try { Directory.Delete(path, true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
