using AFGCPCManager.Setup.Core.Installation;

namespace AFGCPCManager.Setup.Core.Tests.Installation;

public sealed class WindowsInstallationRegistrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "afgc-registration-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void RejectsIncompletePayloadBeforeWritingWindowsRegistration()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "AFGCPCManager.exe"), "placeholder");

        InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
            WindowsInstallationRegistration.Register(_root, new Version(1, 0)));

        Assert.Contains("incomplete", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
