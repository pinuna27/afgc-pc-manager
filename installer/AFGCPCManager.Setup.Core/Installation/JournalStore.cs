using System.Text.Json;
using AFGCPCManager.Setup.Core.Models;

namespace AFGCPCManager.Setup.Core.Installation;

public sealed class JournalStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public async Task SaveAsync(string path, InstallationJournal journal, CancellationToken cancellationToken = default)
    {
        journal = ValidateAndNormalize(journal);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        await using (FileStream stream = new(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(stream, journal, Options, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        File.Move(temporary, path, true);
    }
    public async Task<InstallationJournal?> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) return null;
        await using FileStream stream = File.OpenRead(path);
        InstallationJournal? journal = await JsonSerializer.DeserializeAsync<InstallationJournal>(stream, Options, cancellationToken);
        return ValidateAndNormalize(journal);
    }

    private static InstallationJournal ValidateAndNormalize(InstallationJournal? journal)
    {
        if (journal is null || journal.SchemaVersion != 2 || string.IsNullOrWhiteSpace(journal.InstallDirectory)
            || !Path.IsPathFullyQualified(journal.InstallDirectory) || !Version.TryParse(journal.Version, out _)
            || journal.Files is null || journal.DependenciesInstalledBySetup is null
            || journal.DependenciesPresentBeforeSetup is null)
            throw new InvalidDataException("The installation journal is invalid.");

        string root;
        try { root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(journal.InstallDirectory)); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        { throw new InvalidDataException("The installation journal contains an invalid install directory.", ex); }
        if (root.Equals(Path.GetPathRoot(root), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The installation journal cannot own the root of a drive.");

        if (journal.Files.Any(file => file is null || string.IsNullOrWhiteSpace(file.RelativePath)
                || string.IsNullOrWhiteSpace(file.Sha256) || file.Sha256.Length != 64
                || file.Sha256.Any(character => !Uri.IsHexDigit(character))
                || !IsOwnedFilePath(root, file.RelativePath))
            || journal.Files.GroupBy(file => NormalizeOwnedFilePath(root, file.RelativePath), StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Count() > 1)
            || journal.DependenciesInstalledBySetup.Any(name => !IsKnownDependency(name))
            || journal.DependenciesPresentBeforeSetup.Any(name => !IsKnownDependency(name))
            || journal.DependenciesInstalledBySetup.Intersect(
                journal.DependenciesPresentBeforeSetup, StringComparer.OrdinalIgnoreCase).Any())
            throw new InvalidDataException("The installation journal contains invalid ownership data.");

        if (journal.PendingDependencyOperation is { } pending
            && (string.IsNullOrWhiteSpace(pending.Dependency) || string.IsNullOrWhiteSpace(pending.TargetVersion)
                || string.IsNullOrWhiteSpace(pending.InstallerPath) || !Version.TryParse(pending.TargetVersion, out _)
                || !Enum.TryParse(pending.Dependency, ignoreCase: true,
                    out AFGCPCManager.Setup.Core.Dependencies.DependencyId pendingDependency)
                || !Enum.IsDefined(pendingDependency) || !Enum.IsDefined(pending.Phase)))
            throw new InvalidDataException("The installation journal contains an invalid pending dependency operation.");

        return journal with
        {
            Files = journal.Files.ToList(),
            DependenciesInstalledBySetup = new(journal.DependenciesInstalledBySetup, StringComparer.OrdinalIgnoreCase),
            DependenciesPresentBeforeSetup = new(journal.DependenciesPresentBeforeSetup, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static bool IsOwnedFilePath(string root, string relativePath)
    {
        if (Path.IsPathFullyQualified(relativePath)) return false;
        try
        {
            string path = Path.GetFullPath(Path.Combine(root, relativePath));
            return !path.Equals(root, StringComparison.OrdinalIgnoreCase)
                && path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        { return false; }
    }

    private static string NormalizeOwnedFilePath(string root, string relativePath)
    {
        try { return Path.GetFullPath(Path.Combine(root, relativePath)); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        { return relativePath; }
    }

    private static bool IsKnownDependency(string name) =>
        !string.IsNullOrWhiteSpace(name)
        && Enum.GetNames<AFGCPCManager.Setup.Core.Dependencies.DependencyId>()
            .Contains(name, StringComparer.OrdinalIgnoreCase);
}
