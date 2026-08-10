using AFGCPCManager.Setup.Core.Installation;

namespace AFGCPCManager.Setup.Core.Tests.Installation;

public sealed class LifecycleFileLockTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "afgc-lifecycle-lock-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void RejectsConcurrentLifecycleOperationAndReleasesOnDispose()
    {
        string path = Path.Combine(_root, "lifecycle.lock");
        using (LifecycleFileLock first = LifecycleFileLock.Acquire(path))
        {
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => LifecycleFileLock.Acquire(path));
            Assert.Contains("already running", error.Message, StringComparison.OrdinalIgnoreCase);
        }

        using LifecycleFileLock second = LifecycleFileLock.Acquire(path);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
