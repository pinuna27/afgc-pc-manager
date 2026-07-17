using System.Diagnostics;
using System.Security.Principal;

namespace AFGCPCManager.Setup.Core.Installation;

public static class Elevation
{
    public static bool IsAdministrator() => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    public static int RelaunchAsAdministrator(string executable, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = true, Verb = "runas" };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Elevation was cancelled.");
        process.WaitForExit(); return process.ExitCode;
    }
}
