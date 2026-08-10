using AFGCPCManager.Setup.Core.Security;

namespace AFGCPCManager.Setup.Core.Installation;

public sealed record UninstallResult(int RemovedFiles, IReadOnlyList<string> PreservedFiles);

public sealed class ApplicationUninstaller(JournalStore journalStore)
{
    public async Task<UninstallResult> UninstallOwnedFilesAsync(string journalPath, CancellationToken cancellationToken = default)
    {
        var journal = await journalStore.LoadAsync(journalPath, cancellationToken) ?? throw new InvalidOperationException("Installation journal is missing or invalid.");
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(journal.InstallDirectory));
        string journalDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetDirectoryName(journalPath)!));
        if (!journalDirectory.Equals(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The installation journal is not stored in its recorded installation directory.");
        string rootPrefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        int removed = 0; List<string> preserved = [];
        foreach (var entry in journal.Files.OrderBy(entry => CriticalFileOrder(entry.RelativePath)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = Path.GetFullPath(Path.Combine(root, entry.RelativePath));
            if (!path.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Journal contains a path outside the installation directory.");
            if (!File.Exists(path)) continue;
            if (!string.Equals(await Hashing.Sha256Async(path, cancellationToken), entry.Sha256, StringComparison.OrdinalIgnoreCase)) { preserved.Add(entry.RelativePath); continue; }
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path); removed++;
        }
        if (Directory.Exists(root))
        {
            foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(root, path);
                if (!relative.Equals("install-journal.json", StringComparison.OrdinalIgnoreCase)
                    && !preserved.Contains(relative, StringComparer.OrdinalIgnoreCase))
                    preserved.Add(relative);
            }
        }
        return new(removed, preserved);
    }

    private static int CriticalFileOrder(string relativePath)
    {
        string name = Path.GetFileName(relativePath);
        if (name.Equals("AFGCPCManager.exe", StringComparison.OrdinalIgnoreCase)) return 1;
        return name.StartsWith("AFGCPCManager.Uninstaller", StringComparison.OrdinalIgnoreCase) ? 2 : 0;
    }
}
