using System.IO.Compression;
using AFGCPCManager.Setup.Core.Installation;

namespace AFGCPCManager.Setup.Core.Tests.Installation;

public sealed class PayloadArchiveExtractorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "afgc-archive-tests", Guid.NewGuid().ToString("N"));
    [Fact]
    public void ExtractsSafePayload()
    {
        string zip = Create(("sub/app.exe", "payload")); string output = Path.Combine(_root, "out"); new PayloadArchiveExtractor().Extract(zip, output);
        Assert.Equal("payload", File.ReadAllText(Path.Combine(output, "sub", "app.exe")));
    }
    [Fact]
    public void RejectsPathTraversal()
    {
        string zip = Create(("safe.exe", "partial"), ("../escape.exe", "bad"));
        string output = Path.Combine(_root, "out");
        Assert.Throws<InvalidDataException>(() => new PayloadArchiveExtractor().Extract(zip, output));
        Assert.False(Directory.Exists(output));
    }
    [Fact]
    public void RejectsExpandedSizeLimit()
    {
        string zip = Create(("large.bin", "123456")); string output = Path.Combine(_root, "out");
        Assert.Throws<InvalidDataException>(() => new PayloadArchiveExtractor(5).Extract(zip, output));
        Assert.False(Directory.Exists(output));
    }
    [Fact]
    public void RejectsCaseInsensitiveDuplicatePaths()
    {
        string zip = Create(("app.exe", "first"), ("APP.EXE", "second")); string output = Path.Combine(_root, "out");
        Assert.Throws<InvalidDataException>(() => new PayloadArchiveExtractor().Extract(zip, output));
        Assert.False(Directory.Exists(output));
    }
    private string Create(params (string Name, string Content)[] entries)
    {
        Directory.CreateDirectory(_root); string path = Path.Combine(_root, Guid.NewGuid() + ".zip"); using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var item in entries) { using var writer = new StreamWriter(archive.CreateEntry(item.Name).Open()); writer.Write(item.Content); } return path;
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
