using System.Diagnostics;
using AFGCPCManager.Core.Output;
using AFGCPCManager.Setup.Core.Dependencies;
using AFGCPCManager.ViGEm;
using AFGCPCManager.VJoy;

namespace AFGCPCManager.Bootstrapper;

internal static class DriverProcessHost
{
    private const string VJoyProbeArgument = "--internal-vjoy-probe";
    private const string ViGEmProbeArgument = "--internal-vigembus-probe";
    private const string VJoyProvisionArgument = "--internal-vjoy-provision";

    public static bool TryRunInternalCommand(string[] arguments, out int exitCode)
    {
        if (arguments.Length == 1
            && arguments[0].Equals(VJoyProbeArgument,
                StringComparison.OrdinalIgnoreCase))
        {
            exitCode = RunInternalVJoyProbe();
            return true;
        }
        if (arguments.Length == 1
            && arguments[0].Equals(ViGEmProbeArgument,
                StringComparison.OrdinalIgnoreCase))
        {
            exitCode = RunInternalViGEmProbe();
            return true;
        }
        if (arguments.Length == 2
            && arguments[0].Equals(VJoyProvisionArgument,
                StringComparison.OrdinalIgnoreCase)
            && int.TryParse(arguments[1], out int requiredCount))
        {
            exitCode = RunInternalVJoyProvision(requiredCount);
            return true;
        }

        exitCode = 0;
        return false;
    }

    public static DependencyProbeResult ProbeVJoy() =>
        RunProbe(VJoyProbeArgument, "vJoy", TimeSpan.FromSeconds(10));

    public static DependencyProbeResult ProbeViGEmBus() =>
        RunProbe(ViGEmProbeArgument, "ViGEmBus", TimeSpan.FromSeconds(10));

    public static void ProvisionVJoy(int requiredCount)
    {
        DependencyProbeResult result = RunProbe(
            VJoyProvisionArgument, "vJoy provisioning", TimeSpan.FromMinutes(2),
            requiredCount.ToString());
        if (!result.Operational)
            throw new InvalidOperationException(
                result.Detail ?? "The isolated vJoy provisioning process failed.");
    }

    private static int RunInternalVJoyProbe()
    {
        PrepareConsole();
        try
        {
            VJoyBackend backend;
            try
            {
                backend = new VJoyBackend();
            }
            catch (Exception ex) when (ex is VJoyException or DllNotFoundException
                                               or BadImageFormatException
                                               or EntryPointNotFoundException)
            {
                Console.Error.WriteLine(ex.Message);
                return VJoyProbeProtocol.UnavailableExitCode;
            }

            if (!backend.IsDriverEnabled)
            {
                Console.Error.WriteLine("The vJoy driver is installed but disabled.");
                return VJoyProbeProtocol.UnhealthyExitCode;
            }

            int compatible = backend.EnumerateDevices().Count(device =>
                device.Capabilities is not null
                && device.Status is OutputDeviceStatus.Free or OutputDeviceStatus.Owned);
            Console.WriteLine($"vJoy is enabled; compatible outputs: {compatible}.");
            return VJoyProbeProtocol.ReadyExitCode;
        }
        catch (Exception ex)
        {
            TryWriteError(ex.Message);
            return VJoyProbeProtocol.UnhealthyExitCode;
        }
    }

    private static int RunInternalViGEmProbe()
    {
        PrepareConsole();
        try
        {
            using var backend = new ViGEmBackend();
            int slots = backend.EnumerateDevices().Count;
            Console.WriteLine($"ViGEmBus is operational; Xbox output slots: {slots}.");
            return VJoyProbeProtocol.ReadyExitCode;
        }
        catch (Exception ex) when (ex is ViGEmException or DllNotFoundException
                                           or BadImageFormatException
                                           or EntryPointNotFoundException)
        {
            TryWriteError(ex.Message);
            return VJoyProbeProtocol.UnavailableExitCode;
        }
        catch (Exception ex)
        {
            TryWriteError(ex.Message);
            return VJoyProbeProtocol.UnhealthyExitCode;
        }
    }

    private static int RunInternalVJoyProvision(int requiredCount)
    {
        PrepareConsole();
        try
        {
            new VJoyDeviceProvisioner()
                .EnsureCompatibleDeviceCountForProcessLifetimeAsync(requiredCount)
                .GetAwaiter().GetResult();
            Console.WriteLine($"vJoy exposes at least {requiredCount} compatible output(s).");
            return VJoyProbeProtocol.ReadyExitCode;
        }
        catch (Exception ex)
        {
            TryWriteError(ex.Message);
            return VJoyProbeProtocol.UnhealthyExitCode;
        }
    }

    private static DependencyProbeResult RunProbe(
        string command,
        string displayName,
        TimeSpan timeout,
        params string[] additionalArguments)
    {
        var start = new ProcessStartInfo(CurrentExecutable())
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add(command);
        foreach (string argument in additionalArguments)
            start.ArgumentList.Add(argument);

        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException(
                $"The isolated {displayName} process could not start.");
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> errorTask = process.StandardError.ReadToEndAsync();
        using var timeoutSource = new CancellationTokenSource(timeout);
        try
        {
            process.WaitForExitAsync(timeoutSource.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); }
            catch (Exception ex) when (ex is InvalidOperationException
                                       or System.ComponentModel.Win32Exception)
            { }
            throw new TimeoutException($"The isolated {displayName} process timed out.");
        }

        string output = outputTask.GetAwaiter().GetResult();
        string error = errorTask.GetAwaiter().GetResult();
        string detail = string.IsNullOrWhiteSpace(error) ? output : error;
        return VJoyProbeProtocol.Interpret(process.ExitCode, detail);
    }

    private static void PrepareConsole()
    {
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
    }

    private static void TryWriteError(string message)
    {
        try { Console.Error.WriteLine(message); }
        catch (IOException) { }
    }

    private static string CurrentExecutable() => Environment.ProcessPath
        ?? throw new InvalidOperationException("The setup executable path is unavailable.");
}
