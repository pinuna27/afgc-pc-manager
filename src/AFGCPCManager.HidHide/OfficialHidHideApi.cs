using Nefarius.Drivers.HidHide;

namespace AFGCPCManager.HidHide;

internal sealed class OfficialHidHideApi : IHidHideApi
{
    private readonly HidHideControlService _service = new();
    public bool IsInstalled => _service.IsInstalled;
    public bool IsOperational => _service.IsOperational;
    public bool IsActive
    {
        get => _service.IsActive;
        set => _service.IsActive = value;
    }
    public bool IsAppListInverted => _service.IsAppListInverted;
    public Version LocalDriverVersion => _service.LocalDriverVersion;
    public IReadOnlyCollection<string> ApplicationPaths => _service.ApplicationPaths.ToArray();
    public IReadOnlyCollection<string> BlockedInstanceIds => _service.BlockedInstanceIds.ToArray();
    public void AddApplicationPath(string path) => _service.AddApplicationPath(path, true);
    public void RemoveApplicationPath(string path) => _service.RemoveApplicationPath(path);
    public void AddBlockedInstanceId(string instanceId) => _service.AddBlockedInstanceId(instanceId);
    public void RemoveBlockedInstanceId(string instanceId) => _service.RemoveBlockedInstanceId(instanceId);
}
