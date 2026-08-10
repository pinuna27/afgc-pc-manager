using AFGCPCManager.Core.Output;
using AFGCPCManager.HidHide;

namespace AFGCPCManager.App;

internal sealed class ControllerOutputSafetyGate(
    Func<string, IEnumerable<string>, string, CancellationToken,
        Task<HidHideVisibilityResult>> verifyHidden,
    Func<string, CancellationToken, Task> unhide,
    Action<string> markHidden)
{
    public async Task<OutputSafetyAuthorization> AuthorizeAsync(
        bool hidePhysicalController,
        string stableControllerId,
        IEnumerable<string> inputPaths,
        string applicationPath,
        IGamepadOutputSession output,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (!hidePhysicalController)
            return new(true, "Physical controller hiding is disabled.");

        try
        {
            HidHideVisibilityResult visibility = await verifyHidden(
                stableControllerId, inputPaths, applicationPath, cancellationToken);
            markHidden(stableControllerId);
            if (visibility.IsHidden && visibility.HandleResetRequired)
            {
                string? resetReleaseFailure = TryRelease(output);
                const string resetDetail = "HidHide was changed while this controller was connected. "
                    + "Turn the controller off and back on once so existing Windows handles are closed.";
                return new(false, resetReleaseFailure is null
                    ? resetDetail
                    : $"{resetDetail} The reserved virtual output also could not be released: {resetReleaseFailure}",
                    true);
            }
            if (visibility.IsHidden) return new(true, visibility.Detail);

            string? releaseFailure = TryRelease(output);
            return new(false, releaseFailure is null
                ? visibility.Detail
                : $"{visibility.Detail} The reserved virtual output also could not be released: {releaseFailure}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await TryUnhideAsync(stableControllerId);
            TryRelease(output);
            throw;
        }
        catch (Exception ex)
        {
            string? cleanupFailure = await TryUnhideAsync(stableControllerId);
            string? releaseFailure = TryRelease(output);
            string detail = $"Physical hiding could not be configured: {ex.Message}";
            if (cleanupFailure is not null)
                detail += $" Physical visibility cleanup also failed: {cleanupFailure}";
            if (releaseFailure is not null)
                detail += $" The reserved virtual output also could not be released: {releaseFailure}";
            return new(false, detail);
        }
    }

    private async Task<string?> TryUnhideAsync(string stableControllerId)
    {
        try { await unhide(stableControllerId, CancellationToken.None); return null; }
        catch (Exception ex) { return ex.Message; }
    }

    private static string? TryRelease(IGamepadOutputSession output)
    {
        try { output.Dispose(); return null; }
        catch (Exception ex) { return ex.Message; }
    }
}

internal sealed record OutputSafetyAuthorization(bool IsAuthorized, string Detail,
    bool HandleResetRequired = false);
