using AFGCPCManager.Core.Devices;
using AFGCPCManager.Windows.Devices;
using AFGCPCManager.Windows.RawInput;

namespace AFGCPCManager.Windows.Tests.Devices;

public sealed class FireControllerDiscoveryTests
{
    [Fact]
    public void KnownCompositeCollectionsUseCapturedHidContracts()
    {
        const string gamepad = @"\\?\HID#VID_1949&PID_0402&COL01#INSTANCE";
        const string consumer = @"\\?\HID#VID_1949&PID_0402&COL02#INSTANCE";

        Assert.Equal(((ushort)1, (ushort)5), FireControllerDiscovery.KnownUsage(gamepad));
        Assert.Equal(((ushort)0x0C, (ushort)1), FireControllerDiscovery.KnownUsage(consumer));
        Assert.Equal(11, DirectHidControllerInput.InputReportLengthForPath(gamepad));
        Assert.Equal(2, DirectHidControllerInput.InputReportLengthForPath(consumer));
    }

    [Fact]
    public void CollectionPathsFromOneControllerShareAGroupKey()
    {
        string first = @"\\?\HID#VID_1949&PID_0402&COL01#8&ABC&0&0000#{GUID}";
        string second = @"\\?\HID#VID_1949&PID_0402&COL03#8&ABC&0&0001#{GUID}";
        Assert.Equal(FireControllerDiscovery.NormalizeCollectionPath(first), FireControllerDiscovery.NormalizeCollectionPath(second), ignoreCase: true);
    }

    [Fact]
    public void DifferentBluetoothInstancesRemainSeparate()
    {
        string first = @"\\?\HID#VID_1949&PID_0402&COL01#AAA#{GUID}";
        string second = @"\\?\HID#VID_1949&PID_0402&COL01#BBB#{GUID}";
        Assert.NotEqual(FireControllerDiscovery.NormalizeCollectionPath(first), FireControllerDiscovery.NormalizeCollectionPath(second));
    }

    [Fact]
    public void SnapshotGroupsCompositeCollectionsAndRemovesDuplicateEndpoints()
    {
        string first = @"\\?\HID#VID_1949&PID_0402&COL01#8&ABC&0&0000#{GUID}";
        string second = @"\\?\HID#VID_1949&PID_0402&COL02#8&ABC&0&0001#{GUID}";

        IReadOnlyList<AFGCPCManager.Core.Devices.DiscoveredFireController> result =
            FireControllerDiscovery.BuildSnapshot([
                (first, (ushort)1, (ushort)5, null),
                (first, (ushort)1, (ushort)5, null),
                (second, (ushort)12, (ushort)1, null)]);

        Assert.Single(result);
        Assert.Equal(2, result[0].Endpoints.Count);
    }

    [Fact]
    public void HardwareSerialKeepsIdentityAcrossRePairPaths()
    {
        string oldPath = @"\\?\HID#VID_1949&PID_0402&COL01#8&OLD&0&0000#{GUID}";
        string newPath = @"\\?\HID#VID_1949&PID_0402&COL01#8&NEW&0&0000#{GUID}";

        var oldController = FireControllerDiscovery.BuildSnapshot(
            [(oldPath, (ushort)1, (ushort)5, "a0:02:dc:f1:85:7e")]).Single();
        var newController = FireControllerDiscovery.BuildSnapshot(
            [(newPath, (ushort)1, (ushort)5, "A002DCF1857E")]).Single();

        Assert.Equal(oldController.Identity.StableId, newController.Identity.StableId);
    }

    [Fact]
    public void SerialFromOneCollectionIdentifiesSiblingWhenItsReadFails()
    {
        string first = @"\\?\HID#VID_1949&PID_0402&COL01#8&ABC&0&0000#{GUID}";
        string second = @"\\?\HID#VID_1949&PID_0402&COL02#8&ABC&0&0001#{GUID}";

        var result = FireControllerDiscovery.BuildSnapshot([
            (first, (ushort)1, (ushort)5, "A002DCF1857E"),
            (second, (ushort)12, (ushort)1, null)]);

        Assert.Single(result);
        Assert.Equal(2, result[0].Endpoints.Count);
    }

    [Fact]
    public void DifferentHardwareSerialsRemainSeparate()
    {
        string first = @"\\?\HID#VID_1949&PID_0402&COL01#8&SAME&0&0000#{GUID}";
        string second = @"\\?\HID#VID_1949&PID_0402&COL02#8&SAME&0&0001#{GUID}";

        var result = FireControllerDiscovery.BuildSnapshot([
            (first, (ushort)1, (ushort)5, "A002DCF1857E"),
            (second, (ushort)12, (ushort)1, "A002DCF1857F")]);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void SnapshotUsesDiscoveredControllerName()
    {
        string path = @"\\?\HID#VID_1949&PID_0402&COL01#8&ABC&0&0000#{GUID}";

        var controller = FireControllerDiscovery.BuildSnapshot([
            (path, (ushort)1, (ushort)5, "A002DCF1857E", "Living Room Fire Pad")
        ]).Single();

        Assert.Equal("Living Room Fire Pad", controller.Identity.DisplayName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("HID-compliant game controller")]
    [InlineData("Bluetooth HID Device")]
    public void SnapshotFallsBackWhenDiscoveredNameIsMissingOrGeneric(string? displayName)
    {
        string path = @"\\?\HID#VID_1949&PID_0402&COL01#8&ABC&0&0000#{GUID}";

        var controller = FireControllerDiscovery.BuildSnapshot([
            (path, (ushort)1, (ushort)5, "A002DCF1857E", displayName)
        ]).Single();

        Assert.Equal(FireControllerConstants.BluetoothName,
            controller.Identity.DisplayName);
    }

    [Fact]
    public void RenamedBluetoothParentWinsOverFactoryHidChildName()
    {
        string? name = ControllerDisplayNameResolver.SelectName([
            (FireControllerConstants.BluetoothName, FireControllerConstants.BluetoothName),
            ("Living Room Fire Pad", FireControllerConstants.BluetoothName)
        ], FireControllerConstants.BluetoothName);

        Assert.Equal("Living Room Fire Pad", name);
    }

    [Fact]
    public void BluetoothAdapterNameIsNotUsedAsControllerName()
    {
        string? name = ControllerDisplayNameResolver.SelectName([
            (null, FireControllerConstants.BluetoothName),
            ("Intel(R) Wireless Bluetooth(R)", null)
        ], FireControllerConstants.BluetoothName);

        Assert.Equal(FireControllerConstants.BluetoothName, name);
    }

    [Theory]
    [InlineData(@"BTHENUM\{00001124-0000-1000-8000-00805F9B34FB}_VID&00021949_PID&0402\7&22C3C290&0&A002DCF1857E_C00000000")]
    [InlineData(@"BTHENUM\DEV_A002DCF1857E\7&22C3C290&0&BLUETOOTHDEVICE_A002DCF1857E")]
    public void BluetoothParentInstanceProvidesStableHardwareAddress(string instanceId)
    {
        Assert.Equal("A002DCF1857E",
            ControllerDisplayNameResolver.ExtractBluetoothAddress(instanceId));
    }

    [Theory]
    [InlineData(@"HID\{00001124-0000-1000-8000-00805F9B34FB}_VID&00021949_PID&0402&COL01\8&153F8146&1&0000")]
    [InlineData(@"BTH\MS_BTHBRB\6&2603A294&0&1")]
    [InlineData(null)]
    public void NonBluetoothAddressInstanceIsNotMistakenForSerial(string? instanceId)
    {
        Assert.Null(ControllerDisplayNameResolver.ExtractBluetoothAddress(instanceId));
    }
}
