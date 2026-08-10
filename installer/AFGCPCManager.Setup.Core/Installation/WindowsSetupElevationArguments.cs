namespace AFGCPCManager.Setup.Core.Installation;

public static class WindowsSetupElevationArguments
{
    public static string[] Prepare(IEnumerable<string> arguments, bool cliMode)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        string[] values = arguments.ToArray();
        if (cliMode)
            return values.Contains("--cli", StringComparer.OrdinalIgnoreCase)
                ? values
                : ["--cli", .. values];
        return values.Contains("--wizard-run", StringComparer.OrdinalIgnoreCase)
            ? values
            : [.. values, "--wizard-run"];
    }
}
