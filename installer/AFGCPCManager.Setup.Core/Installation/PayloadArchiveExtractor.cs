using System.IO.Compression;

namespace AFGCPCManager.Setup.Core.Installation;

public sealed class PayloadArchiveExtractor(long maximumExpandedBytes = 512L * 1024 * 1024, int maximumEntries = 10_000)
{
    public void Extract(string archivePath, string destination)
    {
        if (Directory.Exists(destination)) Directory.Delete(destination, true); Directory.CreateDirectory(destination);
        string root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar; long expanded = 0; int entries = 0;
        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (++entries > maximumEntries) throw new InvalidDataException("The application archive contains too many entries.");
            expanded = checked(expanded + entry.Length); if (expanded > maximumExpandedBytes) throw new InvalidDataException("The expanded application archive is too large.");
            string target = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The application archive contains a path outside its payload directory.");
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); entry.ExtractToFile(target, true);
        }
    }
}
