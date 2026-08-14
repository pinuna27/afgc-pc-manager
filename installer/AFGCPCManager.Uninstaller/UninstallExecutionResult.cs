namespace AFGCPCManager.Uninstaller;

internal enum ControllerVisibilityOutcome
{
    Restored,
    HidHideNotInstalled,
    NoOwnedEntries
}

internal sealed record UninstallExecutionResult(
    int ExitCode,
    ControllerVisibilityOutcome ControllerVisibility,
    bool ResumesAfterRestart = false);

internal static class UninstallCompletionPresentation
{
    public static string? ControllerMessage(ControllerVisibilityOutcome outcome) =>
        outcome == ControllerVisibilityOutcome.Restored
            ? "Controller restored to its default behavior."
            : null;

    public static string RestartMessage(bool resumesAfterRestart) => resumesAfterRestart
        ? "Uninstall will resume automatically after you sign in."
        : "Restart Windows when convenient to finish removing the selected components.";
}
