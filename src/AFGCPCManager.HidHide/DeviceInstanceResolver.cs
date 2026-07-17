using Nefarius.Utilities.DeviceManagement.PnP;

namespace AFGCPCManager.HidHide;

public sealed class DeviceInstanceResolver : IDeviceInstanceResolver
{
    public string Resolve(string deviceInterfacePath) =>
        PnPDevice.GetInstanceIdFromInterfaceId(deviceInterfacePath) ??
        throw new InvalidOperationException("The device interface could not be resolved to a device instance.");
}
