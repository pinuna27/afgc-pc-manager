namespace AFGCPCManager.Setup.Core.Installation;

public sealed class LifecycleFileLock : IDisposable
{
    private readonly FileStream _stream;

    private LifecycleFileLock(FileStream stream) => _stream = stream;

    public static LifecycleFileLock Acquire() => Acquire(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "AFGC PC Manager", "lifecycle.lock"));

    public static LifecycleFileLock Acquire(string path)
    {
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        try
        {
            return new LifecycleFileLock(new FileStream(fullPath, FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None, bufferSize: 1, FileOptions.None));
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException(
                "Another AFGC PC Manager setup or uninstall operation is already running.", ex);
        }
    }

    public void Dispose() => _stream.Dispose();
}
