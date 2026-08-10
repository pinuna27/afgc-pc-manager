namespace AFGCPCManager.App;

internal static class RuntimeEventLog
{
    private static readonly object Gate = new();
    public static string PathName { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AFGC PC Manager", "runtime.log");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                string directory = Path.GetDirectoryName(PathName)!;
                Directory.CreateDirectory(directory);
                if (File.Exists(PathName) && new FileInfo(PathName).Length > 1_000_000)
                    File.Move(PathName, PathName + ".previous", overwrite: true);
                File.AppendAllText(PathName,
                    $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
            }
        }
        catch { }
    }
}
