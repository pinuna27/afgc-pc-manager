using AFGCPCManager.Bootstrapper;
using AFGCPCManager.Setup.Core;

namespace AFGCPCManager.Bootstrapper.Tests;

public sealed class SetupArgumentsTests
{
    [Fact]
    public void ReadsOptionsWithoutCaseSensitivity()
    {
        string[] arguments = ["--INSTALL-DIR", @"D:\Controllers", "--repair"];

        Assert.Equal(@"D:\Controllers", CommandLineArguments.Get(arguments, "--install-dir"));
        Assert.True(CommandLineArguments.Has(arguments, "--REPAIR"));
    }

    [Fact]
    public void ReplacesAnExistingOptionValue()
    {
        string[] result = CommandLineArguments.WithValue(
            ["--install-dir", @"C:\Old", "--repair"],
            "--install-dir", @"D:\New");

        Assert.Equal(["--install-dir", @"D:\New", "--repair"], result);
    }

    [Fact]
    public void AppendsAMissingOptionValue()
    {
        string[] result = CommandLineArguments.WithValue(
            ["--repair"], "--install-dir", @"D:\New");

        Assert.Equal(["--repair", "--install-dir", @"D:\New"], result);
    }
}
