namespace AFGCPCManager.Core.Devices;

public sealed record PhysicalDeviceEndpoint(string DevicePath, ushort UsagePage, ushort Usage);
