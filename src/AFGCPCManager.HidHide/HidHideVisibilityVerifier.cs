using System.Diagnostics;

namespace AFGCPCManager.HidHide;

public enum HidHideVisibilityStatus { Hidden, Visible, Indeterminate }

public sealed record HidHideVisibilityResult(HidHideVisibilityStatus Status, string Detail,
    bool HandleResetRequired = false)
{
    public bool IsHidden => Status == HidHideVisibilityStatus.Hidden;
}

public interface IHidHideVisibilityVerifier
{
    string ProbeApplicationPath { get; }
    Task<HidHideVisibilityResult> VerifyHiddenAsync(
        string stableControllerId, CancellationToken cancellationToken = default);
}

public sealed class ProcessHidHideVisibilityVerifier(string probeApplicationPath) : IHidHideVisibilityVerifier
{
    public string ProbeApplicationPath { get; } = Path.GetFullPath(probeApplicationPath);

    public async Task<HidHideVisibilityResult> VerifyHiddenAsync(
        string stableControllerId, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(ProbeApplicationPath))
            return new(HidHideVisibilityStatus.Indeterminate,
                "The independent physical-controller visibility probe is missing. Repair AFGC PC Manager.");
        if (string.IsNullOrWhiteSpace(stableControllerId)
            || stableControllerId.Length != 64 || stableControllerId.Any(character => !Uri.IsHexDigit(character)))
            return new(HidHideVisibilityStatus.Indeterminate,
                "The controller identity could not be verified safely.");

        var start = new ProcessStartInfo(ProbeApplicationPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("--verify-hidden");
        start.ArgumentList.Add(stableControllerId);
        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException("The physical-controller visibility probe could not start.");
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            if (cancellationToken.IsCancellationRequested) throw;
            return new(HidHideVisibilityStatus.Indeterminate,
                "The independent physical-controller visibility check timed out.");
        }

        string output = await outputTask;
        string error = await errorTask;
        return process.ExitCode switch
        {
            0 => new(HidHideVisibilityStatus.Hidden,
                "An independent non-whitelisted process cannot see the physical controller."),
            10 => new(HidHideVisibilityStatus.Visible,
                "The physical controller is still visible outside AFGC PC Manager. Virtual output remains disabled."),
            _ => new(HidHideVisibilityStatus.Indeterminate,
                "The independent visibility check failed: " + Summarize(error, output))
        };
    }

    private static string Summarize(string primary, string fallback)
    {
        string value = string.IsNullOrWhiteSpace(primary) ? fallback : primary;
        value = value.ReplaceLineEndings(" ").Trim();
        return value.Length == 0 ? "no diagnostic was returned"
            : value[..Math.Min(value.Length, 500)];
    }
}
