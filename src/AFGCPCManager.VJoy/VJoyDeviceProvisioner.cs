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
        => (await EnsureCompatibleDeviceCountAsync(1, cancellationToken))[0];

    public async Task<IReadOnlyList<VJoyProvisioningPlan>> EnsureCompatibleDeviceCountAsync(int requiredCount, CancellationToken cancellationToken = default)
    {
        if (requiredCount is < 1 or > (int)VJoyBackend.MaximumDeviceId)
            throw new ArgumentOutOfRangeException(nameof(requiredCount));
        IReadOnlyList<VJoyProvisioningPlan> plans;
        using (var backend = new VJoyBackend()) plans = SelectPlans(backend.EnumerateDevices(), requiredCount);

        foreach (VJoyProvisioningPlan plan in plans.Where(x => x.Action != VJoyProvisioningAction.None))
        {
            string configurator = FindConfigurator()
                ?? throw new FileNotFoundException("vJoyConfig.exe was not installed with vJoy.");
            var start = new ProcessStartInfo(configurator) { UseShellExecute = true, Verb = "runas" };
            foreach (string argument in plan.Arguments) start.ArgumentList.Add(argument);
            using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start vJoyConfig.exe.");
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0) throw new InvalidOperationException($"vJoyConfig.exe exited with code {process.ExitCode}.");
        }

        using var verification = new VJoyBackend();
        int compatible = verification.EnumerateDevices().Count(IsUsable);
        if (compatible < requiredCount)
            throw new InvalidOperationException($"vJoy exposed only {compatible} of {requiredCount} required compatible devices after configuration.");
        return plans;
    }

    public static VJoyProvisioningPlan SelectPlan(IReadOnlyList<OutputDeviceInfo> devices)
        => SelectPlans(devices, 1)[0];

    public static IReadOnlyList<VJoyProvisioningPlan> SelectPlans(IReadOnlyList<OutputDeviceInfo> devices, int requiredCount)
    {
        if (requiredCount is < 1 or > (int)VJoyBackend.MaximumDeviceId)
            throw new ArgumentOutOfRangeException(nameof(requiredCount));
        var plans = devices.Where(IsUsable).OrderBy(x => x.Id)
            .Take(requiredCount).Select(x => new VJoyProvisioningPlan(x.Id, VJoyProvisioningAction.None)).ToList();
        if (plans.Count == requiredCount) return plans;
        var selected = plans.Select(x => x.DeviceId).ToHashSet();
        foreach (OutputDeviceInfo device in devices.Where(x => !selected.Contains(x.Id) && x.Status == OutputDeviceStatus.Missing).OrderBy(x => x.Id))
        {
            plans.Add(new(device.Id, VJoyProvisioningAction.Create));
            if (plans.Count == requiredCount) return plans;
        }
        foreach (OutputDeviceInfo device in devices.Where(x => !selected.Contains(x.Id) && x.Status == OutputDeviceStatus.Free && x.Capabilities is null).OrderBy(x => x.Id))
        {
            plans.Add(new(device.Id, VJoyProvisioningAction.Reconfigure));
            if (plans.Count == requiredCount) return plans;
        }
        throw new InvalidOperationException($"Only {plans.Count} of {requiredCount} vJoy devices can be made available. Busy devices were left unchanged.");
    }

    private static bool IsUsable(OutputDeviceInfo device) =>
        device.Capabilities is not null && device.Status is OutputDeviceStatus.Free or OutputDeviceStatus.Owned;

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
