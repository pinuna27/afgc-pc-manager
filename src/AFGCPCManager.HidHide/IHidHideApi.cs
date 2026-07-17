namespace AFGCPCManager.HidHide;

internal interface IHidHideApi
{
    bool IsInstalled { get; }
    bool IsOperational { get; }
    bool IsActive { get; set; }
    bool IsAppListInverted { get; }
    Version LocalDriverVersion { get; }
    IReadOnlyCollection<string> ApplicationPaths { get; }
    IReadOnlyCollection<string> BlockedInstanceIds { get; }
    void AddApplicationPath(string path);
    void RemoveApplicationPath(string path);
    void AddBlockedInstanceId(string instanceId);
    void RemoveBlockedInstanceId(string instanceId);
}
