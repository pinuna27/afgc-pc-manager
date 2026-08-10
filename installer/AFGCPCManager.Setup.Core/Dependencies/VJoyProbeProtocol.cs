namespace AFGCPCManager.Setup.Core.Dependencies;

public static class VJoyProbeProtocol
{
    public const int ReadyExitCode = 0;
    public const int UnhealthyExitCode = 10;
    public const int UnavailableExitCode = 20;

    public static DependencyProbeResult Interpret(int exitCode, string? detail = null)
    {
        string? summary = Summarize(detail);
        return exitCode switch
        {
            ReadyExitCode => new(true, true, Detail: summary),
            UnhealthyExitCode => new(true, false, Detail: summary),
            UnavailableExitCode => new(false, false, Detail: summary),
            _ => throw new InvalidOperationException(
                $"The isolated vJoy readiness probe exited with code {exitCode}."
                + (summary is null ? string.Empty : $" {summary}"))
        };
    }

    private static string? Summarize(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail)) return null;
        string value = detail.ReplaceLineEndings(" ").Trim();
        return value[..Math.Min(value.Length, 500)];
    }
}
