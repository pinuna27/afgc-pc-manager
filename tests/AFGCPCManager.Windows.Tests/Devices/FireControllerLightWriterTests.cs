using AFGCPCManager.Windows.Devices;

namespace AFGCPCManager.Windows.Tests.Devices;

public sealed class FireControllerLightWriterTests
{
    [Fact]
    public void TriesCompositeEndpointsUntilOneAcceptsTheReport()
    {
        var attempts = new List<(string Path, byte Mask)>();
        var writer = new FireControllerLightWriter((path, mask) =>
        {
            attempts.Add((path, mask));
            return path == "output";
        });

        bool sent = writer.TrySetIdentificationLight(
            ["input", "output", "unused"], 0b0100);

        Assert.True(sent);
        Assert.Equal([("input", (byte)0b0100), ("output", (byte)0b0100)], attempts);
    }

    [Fact]
    public void DuplicateAndEmptyPathsAreNotWritten()
    {
        var attempts = new List<string>();
        var writer = new FireControllerLightWriter((path, _) =>
        {
            attempts.Add(path);
            return false;
        });

        Assert.False(writer.TrySetIdentificationLight(
            ["", "endpoint", "ENDPOINT"], 0));
        Assert.Equal(["endpoint"], attempts);
    }

    [Fact]
    public void EndpointFailureDoesNotPreventTryingTheNextCollection()
    {
        var writer = new FireControllerLightWriter((path, _) => path switch
        {
            "failure" => throw new IOException("disconnected"),
            "output" => true,
            _ => false
        });

        Assert.True(writer.TrySetIdentificationLight(["failure", "output"], 1));
    }

    [Fact]
    public void RejectsBitsOutsideTheFourLightReport()
    {
        var writer = new FireControllerLightWriter((_, _) => true);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            writer.TrySetIdentificationLight(["endpoint"], 0x10));
    }
}
