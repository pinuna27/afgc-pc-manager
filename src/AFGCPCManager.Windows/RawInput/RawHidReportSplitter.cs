namespace AFGCPCManager.Windows.RawInput;

internal static class RawHidReportSplitter
{
    public static IReadOnlyList<byte[]> Split(ReadOnlySpan<byte> reports, uint reportSize, uint reportCount)
    {
        if (reportSize == 0 || reportCount == 0 || reportSize > int.MaxValue || reportCount > int.MaxValue) return [];
        ulong required = (ulong)reportSize * reportCount;
        if (required > (ulong)reports.Length) return [];
        var result = new byte[reportCount][];
        for (int i = 0; i < result.Length; i++)
            result[i] = reports.Slice(checked(i * (int)reportSize), (int)reportSize).ToArray();
        return result;
    }
}
