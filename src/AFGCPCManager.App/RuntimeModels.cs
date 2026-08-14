using AFGCPCManager.Core.Settings;

namespace AFGCPCManager.App;

internal sealed record ControllerRowModel(
    string StableId,
    string DisplayName,
    int RegistrationOrder,
    bool IsConnected,
    uint? OutputDeviceId,
    string? Issue,
    byte? IdentificationLightMask = null,
    GamepadOutputMode OutputMode = GamepadOutputMode.DirectInput);

internal sealed record DiagnosticSnapshot(
    string Version,
    string BridgeStatus,
    int RegisteredControllers,
    int ConnectedControllers,
    int CompatibleOutputDevices,
    IReadOnlyList<ControllerRowModel> Controllers,
    string HidingStatus,
    bool StartupEnabled,
    IReadOnlyList<string> RecentEvents,
    GamepadOutputMode OutputMode = GamepadOutputMode.DirectInput)
{
    public string ToReport()
    {
        string outputName = OutputMode == GamepadOutputMode.XInput
            ? "Xbox (XInput)"
            : "vJoy (DirectInput)";
        var lines = new List<string>
        {
            "AFGC PC Manager diagnostics",
            $"Version: {Version}",
            $"Bridge: {BridgeStatus}",
            $"Controllers: {ConnectedControllers} connected / {RegisteredControllers} registered",
            $"Virtual output: {outputName}",
            $"Compatible output slots: {CompatibleOutputDevices}",
            $"Physical hiding: {HidingStatus}",
            $"Start with Windows: {StartupEnabled}",
            string.Empty,
            "Controller assignments:"
        };
        lines.AddRange(Controllers.Select(controller =>
            $"- Controller {controller.RegistrationOrder} "
            + $"[{controller.StableId[..Math.Min(12, controller.StableId.Length)]}]: "
            + $"{(controller.IsConnected ? "connected" : "disconnected")}, "
            + $"output {controller.OutputDeviceId?.ToString() ?? "none"}, "
            + $"lights {IdentificationLightDisplay.Format(controller.IdentificationLightMask)}"
            + (controller.Issue is null ? string.Empty : $", issue: {controller.Issue}")));
        lines.Add(string.Empty);
        lines.Add("Recent events:");
        lines.AddRange(RecentEvents.Select(item => $"- {item}"));
        return string.Join(Environment.NewLine, lines);
    }
}
