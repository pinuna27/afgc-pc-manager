using System.Diagnostics;
using Microsoft.Win32;

namespace AFGCPCManager.Setup.Core.Dependencies;

public sealed class WindowsDependencyDetector : IDependencyDetector
{
    public DependencyState Detect(DependencyId dependency) => dependency switch
    {
        DependencyId.HidHide => DetectHidHide(),
        DependencyId.VJoy => DetectVJoy(),
        _ => throw new ArgumentOutOfRangeException(nameof(dependency))
    };

    private static DependencyState DetectHidHide()
    {
        using RegistryKey classes = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Registry64);
        using RegistryKey? versionKey = classes.OpenSubKey(@"Installer\Dependencies\NSS.Drivers.HidHide.x64");
        string? versionText = versionKey?.GetValue("Version")?.ToString();
        using RegistryKey? pathKey = classes.OpenSubKey(@"SOFTWARE\Nefarius Software Solutions e.U.\Nefarius Software Solutions e.U. HidHide");
        string? path = pathKey?.GetValue("Path")?.ToString();
        bool installed = versionText is not null || !string.IsNullOrWhiteSpace(path);
        return new(DependencyId.HidHide, installed, ParseVersion(versionText), path);
    }

    private static DependencyState DetectVJoy()
    {
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string[] candidates =
        [
            Path.Combine(programFiles, "vJoy", "x64", "vJoyInterface.dll"),
            Path.Combine(programFiles, "vJoy", "vJoyInterface.dll")
        ];
        string? file = candidates.FirstOrDefault(File.Exists);
        if (file is null) return new(DependencyId.VJoy, false);
        Version? version = ParseVersion(FileVersionInfo.GetVersionInfo(file).FileVersion);
        return new(DependencyId.VJoy, true, version, Path.GetDirectoryName(file));
    }

    internal static Version? ParseVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string numeric = new(value.TakeWhile(c => char.IsDigit(c) || c == '.').ToArray());
        return Version.TryParse(numeric.TrimEnd('.'), out Version? version) ? version : null;
    }
}
