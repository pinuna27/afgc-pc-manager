using System.Diagnostics;
using AFGCPCManager.Core.Output;

namespace AFGCPCManager.VJoy;

public enum VJoyProvisioningAction { None, Create, Reconfigure }

public sealed record VJoyProvisioningPlan(uint DeviceId, VJoyProvisioningAction Action)
{
    public IReadOnlyList<string> Arguments
    {
        get
        {
            if (Action == VJoyProvisioningAction.None) return [];
            var arguments = new List<string> { DeviceId.ToString() };
            if (Action == VJoyProvisioningAction.Reconfigure) arguments.Add("-f");
            arguments.AddRange(["-a", "x", "y", "z", "rx", "ry", "rz", "-b", "11", "-p", "1"]);
            return arguments;
        }
    }
}

public sealed class VJoyDeviceProvisioner
{
    public async Task<VJoyProvisioningPlan> EnsureOneCompatibleDeviceAsync(CancellationToken cancellationToken = default)
    {
        VJoyProvisioningPlan plan;
        using (var backend = new VJoyBackend()) plan = SelectPlan(backend.EnumerateDevices());
        if (plan.Action == VJoyProvisioningAction.None) return plan;

        string configurator = FindConfigurator()
            ?? throw new FileNotFoundException("vJoyConfig.exe was not installed with vJoy.");
        var start = new ProcessStartInfo(configurator) { UseShellExecute = false, CreateNoWindow = true };
        foreach (string argument in plan.Arguments) start.ArgumentList.Add(argument);
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start vJoyConfig.exe.");
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0) throw new InvalidOperationException($"vJoyConfig.exe exited with code {process.ExitCode}.");

        using var verification = new VJoyBackend();
        OutputDeviceInfo configured = verification.EnumerateDevices().Single(x => x.Id == plan.DeviceId);
        if (configured.Capabilities is null)
            throw new InvalidOperationException("vJoy did not expose the required axes, buttons, and POV after configuration.");
        return plan;
    }

    public static VJoyProvisioningPlan SelectPlan(IReadOnlyList<OutputDeviceInfo> devices)
    {
        OutputDeviceInfo? compatible = devices.FirstOrDefault(x =>
            x.Capabilities is not null && x.Status is OutputDeviceStatus.Free or OutputDeviceStatus.Owned);
        if (compatible is not null) return new(compatible.Id, VJoyProvisioningAction.None);
        OutputDeviceInfo? missing = devices.FirstOrDefault(x => x.Status == OutputDeviceStatus.Missing);
        if (missing is not null) return new(missing.Id, VJoyProvisioningAction.Create);
        OutputDeviceInfo? free = devices.FirstOrDefault(x => x.Status == OutputDeviceStatus.Free);
        if (free is not null) return new(free.Id, VJoyProvisioningAction.Reconfigure);
        throw new InvalidOperationException("No free or unconfigured vJoy device is available. Busy devices were left unchanged.");
    }

    private static string? FindConfigurator()
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string[] candidates =
        [
            Path.Combine(root, "vJoy", "x64", "vJoyConfig.exe"),
            Path.Combine(root, "vJoy", "vJoyConfig.exe")
        ];
        return candidates.FirstOrDefault(File.Exists);
    }
}
