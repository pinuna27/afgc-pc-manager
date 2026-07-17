using AFGCPCManager.Windows.RawInput;

namespace AFGCPCManager.Windows.Tests.RawInput;

public sealed class FireDevicePathMatcherTests
{
    [Theory]
    [InlineData(@"\\?\HID#VID_1949&PID_0402&COL01", true)]
    [InlineData(@"\\?\BTHENUM#{00001124}_VID&00021949_PID&0402", true)]
    [InlineData(@"\\?\HID#VID_045E&PID_028E", false)]
    public void IdentifiesOnlyFireControllerPaths(string path, bool expected) => Assert.Equal(expected, FireDevicePathMatcher.IsMatch(path));
}
