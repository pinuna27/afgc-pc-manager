using System.Runtime.InteropServices;

namespace AFGCPCManager.Setup.Core.Installation;

public static class WindowsShortcutManager
{
    private const string FolderName = "AFGC PC Manager";

    public static void Create(string installDirectory)
    {
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), FolderName);
        Directory.CreateDirectory(folder);
        string app = Path.Combine(installDirectory, "AFGCPCManager.exe");
        string uninstaller = Path.Combine(installDirectory, "AFGCPCManager.Uninstaller.exe");
        try
        {
            CreateShortcut(Path.Combine(folder, "AFGC PC Manager.lnk"), app, "", installDirectory, app);
            CreateShortcut(Path.Combine(folder, "Recover controller visibility.lnk"), app, "--recover-hidhide", installDirectory, app);
            CreateShortcut(Path.Combine(folder, "Uninstall AFGC PC Manager.lnk"), uninstaller, "", installDirectory, uninstaller);
        }
        catch
        {
            try { Remove(); } catch { }
            throw;
        }
    }

    public static void Remove()
    {
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), FolderName);
        if (!Directory.Exists(folder)) return;
        var errors = new List<Exception>();
        foreach (string name in new[] { "AFGC PC Manager.lnk", "Recover controller visibility.lnk", "Uninstall AFGC PC Manager.lnk" })
        {
            try { File.Delete(Path.Combine(folder, name)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { errors.Add(ex); }
        }
        try { if (!Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { errors.Add(ex); }
        if (errors.Count > 0) throw new AggregateException("One or more Start menu shortcuts could not be removed.", errors);
    }

    private static void CreateShortcut(string shortcutPath, string target, string arguments, string workingDirectory, string icon)
    {
        Type shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new PlatformNotSupportedException("Windows Script Host is unavailable.");
        object shell = Activator.CreateInstance(shellType) ?? throw new InvalidOperationException("Could not create the Windows shortcut service.");
        object? shortcut = null;
        try
        {
            shortcut = ((dynamic)shell).CreateShortcut(shortcutPath);
            ((dynamic)shortcut).TargetPath = target;
            ((dynamic)shortcut).Arguments = arguments;
            ((dynamic)shortcut).WorkingDirectory = workingDirectory;
            ((dynamic)shortcut).IconLocation = icon;
            ((dynamic)shortcut).Description = Path.GetFileNameWithoutExtension(shortcutPath);
            ((dynamic)shortcut).Save();
        }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut);
            if (Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
        }
    }
}
