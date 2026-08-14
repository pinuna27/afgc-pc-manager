using AFGCPCManager.Windows.RawInput;

namespace AFGCPCManager.Windows.Tests.RawInput;

public sealed class RawHidReportSplitterTests
{
    [Fact]
    public void SplitsMultipleReports()
    {
        byte[] input = [1, 2, 3, 4, 5, 6];
        var result = RawHidReportSplitter.Split(input, 3, 2);
        Assert.Equal([1, 2, 3], result[0]); Assert.Equal([4, 5, 6], result[1]);
    }

    [Theory]
    [InlineData(0u, 1u)]
    [InlineData(3u, 0u)]
    [InlineData(4u, 2u)]
    public void RejectsInvalidDimensions(uint size, uint count) => Assert.Empty(RawHidReportSplitter.Split(new byte[6], size, count));
}
