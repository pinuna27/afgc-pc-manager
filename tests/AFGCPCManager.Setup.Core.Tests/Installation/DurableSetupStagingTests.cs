using AFGCPCManager.Setup.Core.Installation;

namespace AFGCPCManager.Setup.Core.Tests.Installation;

public sealed class DurableSetupStagingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "afgc-durable-staging-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void CopiesEveryResumeAssetToVersionedDurableDirectory()
    {
        string source = Path.Combine(_root, "source"); Directory.CreateDirectory(source);
        string setup = Write(source, "setup.exe", "setup"), archive = Write(source, "payload.zip", "archive");
        string manifest = Write(source, "manifest.json", "manifest"), signature = Write(source, "manifest.sig", "signature");
        DurableSetupBundle bundle = DurableSetupStaging.Stage(Path.Combine(_root, "durable"), new(1, 2, 3), setup, archive, manifest, signature);
        Assert.EndsWith(Path.Combine("1.2.3", "AFGCPCManager.Setup.exe"), bundle.SetupPath);
        Assert.Equal("archive", File.ReadAllText(bundle.ArchivePath));
        Assert.Equal("manifest", File.ReadAllText(bundle.ManifestPath!));
        Assert.Equal("signature", File.ReadAllText(bundle.SignaturePath!));
        DurableSetupStaging.Cleanup(bundle);
        Assert.False(Directory.Exists(bundle.Directory));
    }

    private static string Write(string directory, string name, string contents) { string path = Path.Combine(directory, name); File.WriteAllText(path, contents); return path; }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
