using AFGCPCManager.Windows.Devices;

namespace AFGCPCManager.Windows.Tests.Devices;

public sealed class FireControllerDiscoveryTests
{
    [Fact]
    public void CollectionPathsFromOneControllerShareAGroupKey()
    {
        string first = @"\\?\HID#VID_1949&PID_0402&COL01#ABC#{GUID}";
        string second = @"\\?\HID#VID_1949&PID_0402&COL03#ABC#{GUID}";
        Assert.Equal(FireControllerDiscovery.NormalizeCollectionPath(first), FireControllerDiscovery.NormalizeCollectionPath(second), ignoreCase: true);
    }

    [Fact]
    public void DifferentBluetoothInstancesRemainSeparate()
    {
        string first = @"\\?\HID#VID_1949&PID_0402&COL01#AAA#{GUID}";
        string second = @"\\?\HID#VID_1949&PID_0402&COL01#BBB#{GUID}";
        Assert.NotEqual(FireControllerDiscovery.NormalizeCollectionPath(first), FireControllerDiscovery.NormalizeCollectionPath(second));
    }
}
