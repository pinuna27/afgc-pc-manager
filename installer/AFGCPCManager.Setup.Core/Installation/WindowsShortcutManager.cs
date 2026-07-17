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
        CreateShortcut(Path.Combine(folder, "AFGC PC Manager.lnk"), app, "", installDirectory, app);
        CreateShortcut(Path.Combine(folder, "Recover controller visibility.lnk"), app, "--recover-hidhide", installDirectory, app);
        CreateShortcut(Path.Combine(folder, "Uninstall AFGC PC Manager.lnk"), uninstaller, "", installDirectory, uninstaller);
    }

    public static void Remove()
    {
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), FolderName);
        foreach (string name in new[] { "AFGC PC Manager.lnk", "Recover controller visibility.lnk", "Uninstall AFGC PC Manager.lnk" })
            File.Delete(Path.Combine(folder, name));
        if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
    }

    private static void CreateShortcut(string shortcutPath, string target, string arguments, string workingDirectory, string icon)
    {
        Type shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new PlatformNotSupportedException("Windows Script Host is unavailable.");
        dynamic shell = Activator.CreateInstance(shellType) ?? throw new InvalidOperationException("Could not create the Windows shortcut service.");
        dynamic shortcut = shell.CreateShortcut(shortcutPath);
        shortcut.TargetPath = target;
        shortcut.Arguments = arguments;
        shortcut.WorkingDirectory = workingDirectory;
        shortcut.IconLocation = icon;
        shortcut.Description = Path.GetFileNameWithoutExtension(shortcutPath);
        shortcut.Save();
    }
}
