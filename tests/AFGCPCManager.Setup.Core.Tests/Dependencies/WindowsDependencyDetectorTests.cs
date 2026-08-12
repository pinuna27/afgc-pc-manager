using AFGCPCManager.Setup.Core.Dependencies;
using System.Reflection;

namespace AFGCPCManager.Setup.Core.Tests.Dependencies;

public sealed class WindowsDependencyDetectorTests
{
    [Fact]
    public void UnsupportedOptionalProbeDoesNotOverrideReliableAbsence()
    {
        DependencyEvidence[] evidence =
        [
            new("registered application", false),
            new("driver service", false),
            new("runtime library", false)
        ];

        MethodInfo determine = typeof(WindowsDependencyDetector).GetMethod("DetermineReadiness",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        DependencyReadiness readiness = (DependencyReadiness)determine.Invoke(null,
            [DependencyId.VJoy, evidence, null, false, false])!;

        Assert.Equal(DependencyReadiness.Absent, readiness);
    }

    [Fact]
    public void OperationalProbeFailureDoesNotFallBackToWeakerVJoyEvidence()
    {
        DependencyEvidence[] evidence =
        [
            new("registered application", true),
            new("driver service", true),
            new("runtime library", true),
            new("operational API", null, Detail: "probe failed")
        ];

        MethodInfo determine = typeof(WindowsDependencyDetector).GetMethod("DetermineReadiness",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        DependencyReadiness readiness = (DependencyReadiness)determine.Invoke(null,
            [DependencyId.VJoy, evidence, null, true, true])!;

        Assert.Equal(DependencyReadiness.Unknown, readiness);
    }

    [Fact]
    public void ViGEmRegistrationAndDriverServiceAreReadyWithoutAnApiProbe()
    {
        DependencyEvidence[] evidence =
        [
            new("registered application", true, new Version(1, 22)),
            new("driver service", true, Detail: "ViGEmBus")
        ];

        MethodInfo determine = typeof(WindowsDependencyDetector).GetMethod("DetermineReadiness",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        DependencyReadiness readiness = (DependencyReadiness)determine.Invoke(null,
            [DependencyId.ViGEmBus, evidence, null, false, false])!;

        Assert.Equal(DependencyReadiness.Ready, readiness);
    }

    [Theory]
    [InlineData("0.0.0", null)]
    [InlineData("0.0.0.0", null)]
    [InlineData("2.2.2.0", "2.2.2.0")]
    public void ParseVersion_TreatsAllZeroMetadataAsUnknown(string value, string? expected)
    {
        MethodInfo parser = typeof(WindowsDependencyDetector).GetMethod("ParseVersion", BindingFlags.Static | BindingFlags.NonPublic)!;
        Version? parsed = (Version?)parser.Invoke(null, [value]);
        Assert.Equal(expected, parsed?.ToString());
    }

    [Theory]
    [InlineData(DependencyId.VJoy, "vJoy Device Driver 2.2.2", true)]
    [InlineData(DependencyId.VJoy, "Third-party vJoy Feeder", false)]
    [InlineData(DependencyId.ViGEmBus, "Nefarius Virtual Gamepad Emulation Bus Driver", true)]
    [InlineData(DependencyId.ViGEmBus, "ViGEm Bus Driver", true)]
    [InlineData(DependencyId.ViGEmBus, "ViGEm Client SDK", false)]
    [InlineData(DependencyId.HidHide, "Nefarius HidHide", true)]
    public void MatchesOnlyDriverInstallations(DependencyId dependency, string displayName, bool expected)
    {
        MethodInfo matcher = typeof(WindowsDependencyDetector).GetMethod("MatchesInstalledApplication",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        Assert.Equal(expected, matcher.Invoke(null, [dependency, displayName]));
    }
}
