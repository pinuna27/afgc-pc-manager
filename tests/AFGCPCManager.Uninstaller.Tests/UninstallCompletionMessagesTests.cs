using AFGCPCManager.Uninstaller;

namespace AFGCPCManager.Uninstaller.Tests;

public sealed class UninstallCompletionPresentationTests
{
    [Fact]
    public void Restored_ShowsPlainLanguageControllerMessage()
    {
        string? message = UninstallCompletionPresentation.ControllerMessage(
            ControllerVisibilityOutcome.Restored);

        Assert.Equal("Controller restored to its default behavior.", message);
    }

    [Fact]
    public void HidHideNotInstalled_ShowsNoTechnicalMessage()
    {
        string? message = UninstallCompletionPresentation.ControllerMessage(
            ControllerVisibilityOutcome.HidHideNotInstalled);

        Assert.Null(message);
    }

    [Fact]
    public void NoOwnedEntries_ShowsNoTechnicalMessage()
    {
        string? message = UninstallCompletionPresentation.ControllerMessage(
            ControllerVisibilityOutcome.NoOwnedEntries);

        Assert.Null(message);
    }

    [Fact]
    public void CompletedRemoval_AsksOnlyForAConvenientRestart()
    {
        string message = UninstallCompletionPresentation.RestartMessage(
            resumesAfterRestart: false);

        Assert.Contains("when convenient", message);
        Assert.DoesNotContain("resume automatically", message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InterruptedRemoval_ExplainsThatItWillResumeAfterSignIn()
    {
        string message = UninstallCompletionPresentation.RestartMessage(
            resumesAfterRestart: true);

        Assert.Contains("resume automatically", message);
        Assert.Contains("after you sign in", message);
    }
}
