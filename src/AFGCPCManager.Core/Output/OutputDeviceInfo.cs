namespace AFGCPCManager.Core.Output;

public sealed record OutputDeviceInfo(uint Id, OutputDeviceStatus Status, VirtualGamepadCapabilities? Capabilities = null);
