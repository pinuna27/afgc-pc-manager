namespace AFGCPCManager.Core.Output;

public readonly record struct AxisRange(long Minimum, long Maximum)
{
    public long Midpoint => Minimum + ((Maximum - Minimum + 1) / 2);
}
