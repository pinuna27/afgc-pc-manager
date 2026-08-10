using AFGCPCManager.Setup.Core.Installation;

namespace AFGCPCManager.Setup.Core.Tests.Installation;

public sealed class WindowsResumeArgumentsTests
{
    [Fact]
    public void Expand_ReplacesArgumentFileReferenceWithStoredArguments()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "resume.json");
        try
        {
            File.WriteAllText(path, "[\"--wizard-run\",\"--install-dir\",\"C:\\\\Program Files\\\\AFGC PC Manager\"]");

            string[] result = WindowsResumeArguments.Expand(["--before", WindowsResumeArguments.Argument, path, "--after"]);

            Assert.Equal(["--before", "--wizard-run", "--install-dir", "C:\\Program Files\\AFGC PC Manager", "--after"], result);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void Expand_LeavesOrdinaryArgumentsUnchanged()
    {
        string[] arguments = ["--wizard-run"];

        Assert.Same(arguments, WindowsResumeArguments.Expand(arguments));
    }

    [Fact]
    public void Expand_RejectsMalformedStoredArguments()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "resume.json");
        try
        {
            File.WriteAllText(path, "{ invalid");

            Assert.Throws<InvalidDataException>(() =>
                WindowsResumeArguments.Expand([WindowsResumeArguments.Argument, path]));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData("[null]")]
    [InlineData("[\"--resume-arguments\"]")]
    public void Expand_RejectsUnsafeStoredArguments(string json)
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "resume.json");
        try
        {
            File.WriteAllText(path, json);

            Assert.Throws<InvalidDataException>(() =>
                WindowsResumeArguments.Expand([WindowsResumeArguments.Argument, path]));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void Expand_RejectsDuplicateResumeMarkers()
    {
        Assert.Throws<ArgumentException>(() => WindowsResumeArguments.Expand(
            [WindowsResumeArguments.Argument, "first.json", WindowsResumeArguments.Argument, "second.json"]));
    }

    [Fact]
    public void RegistryCopyRecoversArgumentsWhenRedundantFileIsMissing()
    {
        if (!Elevation.IsAdministrator()) return;
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "AFGC PC Manager", "Setup", "resume-arguments.json");
        try
        {
            WindowsSetupResumeRegistration.Register("C:\\fake-setup.exe", ["--wizard-run", "--payload", "C:\\payload"]);
            File.Delete(path);

            string[] expanded = WindowsResumeArguments.Expand([WindowsResumeArguments.Argument, path]);

            Assert.Equal(["--wizard-run", "--payload", "C:\\payload"], expanded);
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
            WindowsSetupResumeRegistration.Unregister();
        }
        finally { WindowsSetupResumeRegistration.Unregister(); }
    }
}
