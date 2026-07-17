namespace AFGCPCManager.Core.Output;

public static class AxisScaler
{
    public static long ScaleStickByte(byte value, byte center, AxisRange range)
    {
        Validate(range);
        if (center is 0 or byte.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(center));

        long midpoint = range.Midpoint;
        if (value == center)
            return midpoint;

        if (value < center)
        {
            double proportion = (double)value / center;
            return range.Minimum + (long)Math.Round(
                proportion * (midpoint - range.Minimum),
                MidpointRounding.AwayFromZero);
        }

        double upperProportion = (double)(value - center) / (byte.MaxValue - center);
        return midpoint + (long)Math.Round(
            upperProportion * (range.Maximum - midpoint),
            MidpointRounding.AwayFromZero);
    }

    public static long ScaleTriggerByte(byte value, AxisRange range)
    {
        Validate(range);
        return range.Minimum + (long)Math.Round(
            value / (double)byte.MaxValue * (range.Maximum - range.Minimum),
            MidpointRounding.AwayFromZero);
    }

    private static void Validate(AxisRange range)
    {
        if (range.Maximum <= range.Minimum)
            throw new ArgumentException("Axis maximum must be greater than its minimum.", nameof(range));
    }
}
