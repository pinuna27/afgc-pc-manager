using AFGCPCManager.Setup.Core.Security;

namespace AFGCPCManager.Setup.Core.Installation;

public sealed record UninstallResult(int RemovedFiles, IReadOnlyList<string> PreservedModifiedFiles);

public sealed class ApplicationUninstaller(JournalStore journalStore)
{
    public async Task<UninstallResult> UninstallOwnedFilesAsync(string journalPath, CancellationToken cancellationToken = default)
    {
        var journal = await journalStore.LoadAsync(journalPath, cancellationToken) ?? throw new InvalidOperationException("Installation journal is missing or invalid.");
        string root = Path.GetFullPath(journal.InstallDirectory);
        int removed = 0; List<string> preserved = [];
        foreach (var entry in journal.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = Path.GetFullPath(Path.Combine(root, entry.RelativePath));
            if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Journal contains a path outside the installation directory.");
            if (!File.Exists(path)) continue;
            if (!string.Equals(await Hashing.Sha256Async(path, cancellationToken), entry.Sha256, StringComparison.OrdinalIgnoreCase)) { preserved.Add(entry.RelativePath); continue; }
            File.Delete(path); removed++;
        }
        return new(removed, preserved);
    }
}
