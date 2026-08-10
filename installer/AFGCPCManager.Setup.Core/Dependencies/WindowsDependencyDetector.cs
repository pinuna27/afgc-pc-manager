using System.Diagnostics;
using Microsoft.Win32;

namespace AFGCPCManager.Setup.Core.Dependencies;

public sealed record DependencyProbeResult(bool Installed, bool Operational, Version? Version = null, string? Detail = null);

public sealed class WindowsDependencyDetector(Func<DependencyId, DependencyProbeResult?>? operationalProbe = null) : IDependencyDetector
{
    public DependencyState Detect(DependencyId dependency)
    {
        var evidence = new List<DependencyEvidence>();
        Version? version = null;
        string? location = null;
        bool queryFailed = false;
        bool operationalProbeFailed = false;

        TryCollect("registered application", () => FindRegisteredApplication(dependency), result =>
        {
            evidence.Add(new("registered application", result.Present, result.Version, result.Path));
            version ??= result.Version;
            location ??= result.Path;
        });
        TryCollect("driver service", () => FindDriverService(dependency), result =>
            evidence.Add(new("driver service", result.Present, Detail: result.Detail)));

        if (dependency == DependencyId.VJoy)
        {
            TryCollect("runtime library", FindVJoyRuntime, result =>
            {
                evidence.Add(new("runtime library", result.Present, result.Version, result.Path));
                version ??= result.Version;
                location ??= result.Path;
            });
        }
        else
        {
            TryCollect("vendor registration", FindHidHideVendorRegistration, result =>
            {
                evidence.Add(new("vendor registration", result.Present, result.Version, result.Path));
                version ??= result.Version;
                location ??= result.Path;
            });
        }

        DependencyProbeResult? probe = null;
        if (operationalProbe is not null)
        {
            try
            {
                DependencyProbeResult? result = operationalProbe(dependency);
                if (result is not null)
                {
                    probe = result;
                    evidence.Add(new("operational API", result.Installed, result.Version, result.Detail));
                    version ??= result.Version;
                }
            }
            catch (Exception ex)
            {
                queryFailed = true;
                operationalProbeFailed = true;
                evidence.Add(new("operational API", null, Detail: ex.Message));
            }
        }

        bool anyPresent = evidence.Any(item => item.Present == true);
        DependencyReadiness readiness = DetermineReadiness(
            dependency, evidence, probe, queryFailed, operationalProbeFailed);

        return new(dependency, anyPresent, version, location, readiness, evidence);

        void TryCollect<T>(string source, Func<T> query, Action<T> collect)
        {
            try { collect(query()); }
            catch (Exception ex)
            {
                queryFailed = true;
                evidence.Add(new(source, null, Detail: ex.Message));
            }
        }
    }

    private static bool HasEvidence(IEnumerable<DependencyEvidence> evidence, string source) =>
        evidence.Any(item => item.Source == source && item.Present == true);

    internal static DependencyReadiness DetermineReadiness(DependencyId dependency,
        IReadOnlyCollection<DependencyEvidence> evidence, DependencyProbeResult? probe, bool queryFailed,
        bool operationalProbeFailed = false)
    {
        bool anyPresent = evidence.Any(item => item.Present == true);
        bool allReliablyAbsent = evidence.Count > 0 && evidence.All(item => item.Present == false);
        if (probe is { Installed: true, Operational: true }) return DependencyReadiness.Ready;
        if (probe is { Installed: true, Operational: false }) return DependencyReadiness.Unhealthy;
        if (probe is { Installed: false } && anyPresent) return DependencyReadiness.Unhealthy;
        if (operationalProbeFailed) return DependencyReadiness.Unknown;
        if (dependency == DependencyId.VJoy && HasEvidence(evidence, "runtime library") && HasEvidence(evidence, "driver service"))
            return DependencyReadiness.Ready;
        if (anyPresent) return DependencyReadiness.Unhealthy;
        if (queryFailed || !allReliablyAbsent) return DependencyReadiness.Unknown;
        return DependencyReadiness.Absent;
    }

    private static (bool Present, Version? Version, string? Path) FindRegisteredApplication(DependencyId dependency)
    {
        var matches = new List<(Version? Version, string? Path)>();
        foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using RegistryKey? uninstall = machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall is null) continue;
            foreach (string keyName in uninstall.GetSubKeyNames())
            {
                using RegistryKey? entry = uninstall.OpenSubKey(keyName);
                string? displayName = entry?.GetValue("DisplayName")?.ToString();
                if (displayName is null || !MatchesInstalledApplication(dependency, displayName)) continue;
                matches.Add((ParseVersion(entry?.GetValue("DisplayVersion")?.ToString()),
                    entry?.GetValue("InstallLocation")?.ToString()));
            }
        }
        if (matches.Count == 0) return (false, null, null);
        (Version? version, string? path) = matches
            .OrderByDescending(match => match.Version, NormalizedVersionComparer.Instance)
            .ThenByDescending(match => !string.IsNullOrWhiteSpace(match.Path))
            .First();
        return (true, version, path);
    }

    internal static bool MatchesInstalledApplication(DependencyId dependency, string displayName) => dependency switch
    {
        DependencyId.VJoy => displayName.Equals("vJoy", StringComparison.OrdinalIgnoreCase)
            || displayName.Contains("vJoy Device Driver", StringComparison.OrdinalIgnoreCase),
        DependencyId.HidHide => displayName.Contains("HidHide", StringComparison.OrdinalIgnoreCase),
        _ => false
    };

    private static (bool Present, string? Detail) FindDriverService(DependencyId dependency)
    {
        string token = dependency == DependencyId.VJoy ? "vjoy" : "hidhide";
        using RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using RegistryKey? services = machine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
        if (services is null) return (false, null);
        foreach (string keyName in services.GetSubKeyNames())
        {
            using RegistryKey? service = services.OpenSubKey(keyName);
            string searchable = string.Join(" ", keyName, service?.GetValue("DisplayName"), service?.GetValue("ImagePath"));
            if (searchable.Contains(token, StringComparison.OrdinalIgnoreCase)) return (true, keyName);
        }
        return (false, null);
    }

    private static (bool Present, Version? Version, string? Path) FindVJoyRuntime()
    {
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "vJoy");
        string[] candidates = [Path.Combine(root, "x64", "vJoyInterface.dll"), Path.Combine(root, "vJoyInterface.dll")];
        string? file = candidates.FirstOrDefault(File.Exists);
        return file is null ? (false, null, null) :
            (true, ParseVersion(FileVersionInfo.GetVersionInfo(file).FileVersion), Path.GetDirectoryName(file));
    }

    private static (bool Present, Version? Version, string? Path) FindHidHideVendorRegistration()
    {
        using RegistryKey classes = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Registry64);
        using RegistryKey? versionKey = classes.OpenSubKey(@"Installer\Dependencies\NSS.Drivers.HidHide.x64");
        using RegistryKey? pathKey = classes.OpenSubKey(@"SOFTWARE\Nefarius Software Solutions e.U.\Nefarius Software Solutions e.U. HidHide");
        string? version = versionKey?.GetValue("Version")?.ToString();
        string? path = pathKey?.GetValue("Path")?.ToString();
        return (version is not null || !string.IsNullOrWhiteSpace(path), ParseVersion(version), path);
    }

    internal static Version? ParseVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string numeric = new(value.TakeWhile(c => char.IsDigit(c) || c == '.').ToArray());
        if (!Version.TryParse(numeric.TrimEnd('.'), out Version? version)) return null;
        return version.Major == 0 && version.Minor == 0
            && Math.Max(version.Build, 0) == 0 && Math.Max(version.Revision, 0) == 0 ? null : version;
    }

    private sealed class NormalizedVersionComparer : IComparer<Version?>
    {
        public static NormalizedVersionComparer Instance { get; } = new();

        public int Compare(Version? left, Version? right)
        {
            if (left is null) return right is null ? 0 : -1;
            if (right is null) return 1;
            if (DependencyPlanBuilder.IsOlder(left, right)) return -1;
            return DependencyPlanBuilder.IsOlder(right, left) ? 1 : 0;
        }
    }
}
