using System.Runtime.InteropServices;

namespace AFGCPCManager.Setup.Core.Installation;

public sealed record DurableSetupBundle(string Directory, string SetupPath, string ArchivePath, string? ManifestPath, string? SignaturePath);
public sealed record DurableLocalSetupBundle(string Directory, string SetupPath, string PayloadDirectory, string? ManifestPath, string? SignaturePath);

public static class DurableSetupStaging
{
    public static DurableSetupBundle Stage(Version version, string setupPath, string archivePath, string? manifestPath, string? signaturePath) =>
        Stage(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AFGC PC Manager", "Setup"),
            version, setupPath, archivePath, manifestPath, signaturePath);

    public static DurableSetupBundle Stage(string root, Version version, string setupPath, string archivePath, string? manifestPath, string? signaturePath)
    {
        string directory = Path.Combine(Path.GetFullPath(root), version.ToString());
        Directory.CreateDirectory(directory);
        string setup = CopyAtomically(setupPath, Path.Combine(directory, "AFGCPCManager.Setup.exe"));
        string archive = CopyAtomically(archivePath, Path.Combine(directory, Path.GetFileName(archivePath)));
        string? manifest = manifestPath is null ? null : CopyAtomically(manifestPath, Path.Combine(directory, "release-manifest.json"));
        string? signature = signaturePath is null ? null : CopyAtomically(signaturePath, Path.Combine(directory, "release-manifest.sig"));
        return new(directory, setup, archive, manifest, signature);
    }

    public static DurableLocalSetupBundle StageLocal(
        Version version, string setupPath, string? manifestPath, string? signaturePath) =>
        StageLocal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "AFGC PC Manager", "Setup"), version, setupPath, manifestPath, signaturePath);

    public static DurableLocalSetupBundle StageLocal(
        string root, Version version, string setupPath, string? manifestPath, string? signaturePath)
    {
        if ((manifestPath is null) != (signaturePath is null))
            throw new ArgumentException("Both the local manifest and signature are required for durable resume.");
        string directory = Path.Combine(Path.GetFullPath(root), version.ToString());
        Directory.CreateDirectory(directory);
        string setup = CopyAtomically(setupPath, Path.Combine(directory, "AFGCPCManager.Setup.exe"));
        string payload = Path.Combine(directory, "resume-payload");
        Directory.CreateDirectory(payload);
        string? manifest = manifestPath is null ? null : CopyAtomically(manifestPath, Path.Combine(directory, "release-manifest.json"));
        string? signature = signaturePath is null ? null : CopyAtomically(signaturePath, Path.Combine(directory, "release-manifest.sig"));
        return new(directory, setup, payload, manifest, signature);
    }

    public static void Cleanup(DurableSetupBundle bundle) => Cleanup(bundle.Directory);
    public static void Cleanup(DurableLocalSetupBundle bundle) => Cleanup(bundle.Directory);

    public static void Cleanup(string directory)
    {
        try { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).OrderByDescending(x => x.Length))
                MoveFileEx(file, null, MoveFileDelayUntilReboot);
            foreach (string childDirectory in Directory.EnumerateDirectories(directory, "*", SearchOption.AllDirectories).OrderByDescending(x => x.Length))
                MoveFileEx(childDirectory, null, MoveFileDelayUntilReboot);
            MoveFileEx(directory, null, MoveFileDelayUntilReboot);
        }
    }

    private static string CopyAtomically(string source, string destination)
    {
        string sourceFull = Path.GetFullPath(source), destinationFull = Path.GetFullPath(destination);
        if (sourceFull.Equals(destinationFull, StringComparison.OrdinalIgnoreCase)) return destinationFull;
        if (!File.Exists(sourceFull)) throw new FileNotFoundException("A setup-resume asset is missing.", sourceFull);
        string temporary = destinationFull + ".new";
        File.Copy(sourceFull, temporary, true);
        File.Move(temporary, destinationFull, true);
        return destinationFull;
    }

    private const uint MoveFileDelayUntilReboot = 0x4;
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool MoveFileEx(string existingFileName, string? newFileName, uint flags);
}
