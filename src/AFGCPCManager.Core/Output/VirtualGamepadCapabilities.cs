namespace AFGCPCManager.Core.Output;

public sealed record VirtualGamepadCapabilities(
    IReadOnlyDictionary<VirtualAxis, AxisRange> Axes,
    int ButtonCount,
    int ContinuousPovCount);
