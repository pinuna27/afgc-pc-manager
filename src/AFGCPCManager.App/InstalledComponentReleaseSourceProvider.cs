using System.Diagnostics;
using AFGCPCManager.Core.Updates;
using Microsoft.Win32;

namespace AFGCPCManager.App;

internal sealed class InstalledComponentReleaseSourceProvider(
    Func<Version?> detectHidHideVersion)
{
    public IReadOnlyList<ReleaseSource> GetSources(Version applicationVersion)
    {
        var sources = new List<ReleaseSource>
        {
            new(ReleaseComponent.AfgcPcManager, AppIdentity.GitHubOwner,
                AppIdentity.GitHubRepository, applicationVersion)
        };

        AddIfInstalled(sources, ReleaseComponent.VJoy,
            "BrunnerInnovation", "vJoy", DetectVJoyVersion());
        AddIfInstalled(sources, ReleaseComponent.ViGEmBus,
            "nefarius", "ViGEmBus", DetectViGEmBusVersion());

        Version? hidHideVersion;
        try
        {
            hidHideVersion = detectHidHideVersion();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                   or InvalidOperationException)
        {
            hidHideVersion = null;
        }
        AddIfInstalled(sources, ReleaseComponent.HidHide,
            "nefarius", "HidHide", hidHideVersion);
        return sources;
    }

    private static void AddIfInstalled(
        ICollection<ReleaseSource> sources,
        ReleaseComponent component,
        string owner,
        string repository,
        Version? installedVersion)
    {
        if (installedVersion is not null)
            sources.Add(new(component, owner, repository, installedVersion));
    }

    private static Version? DetectVJoyVersion()
    {
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string[] candidates =
        [
            Path.Combine(programFiles, "vJoy", "x64", "vJoyInterface.dll"),
            Path.Combine(programFiles, "vJoy", "vJoyInterface.dll")
        ];

        foreach (string path in candidates)
        {
            if (File.Exists(path)
                && Version.TryParse(FileVersionInfo.GetVersionInfo(path).FileVersion,
                    out Version? version))
                return version;
        }
        return null;
    }

    private static Version? DetectViGEmBusVersion()
    {
        var versions = new List<Version>();
        foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using RegistryKey? uninstall = machine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall is null)
                    continue;

                foreach (string childName in uninstall.GetSubKeyNames())
                {
                    using RegistryKey? child = uninstall.OpenSubKey(childName);
                    string? name = child?.GetValue("DisplayName")?.ToString();
                    if (name is null || !(name.Contains("ViGEm Bus Driver",
                            StringComparison.OrdinalIgnoreCase)
                        || name.Contains("Virtual Gamepad Emulation Bus Driver",
                            StringComparison.OrdinalIgnoreCase)))
                        continue;
                    if (Version.TryParse(child?.GetValue("DisplayVersion")?.ToString(),
                            out Version? version))
                        versions.Add(version);
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                return null;
            }
        }
        return versions.OrderDescending().FirstOrDefault();
    }
}
