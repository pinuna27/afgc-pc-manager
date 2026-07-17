using AFGCPCManager.Core.Output;

namespace AFGCPCManager.Core.Mapping;

public readonly record struct MappingResult(
    VirtualGamepadState Gamepad,
    IReadOnlyList<ConsumerAction> ConsumerActions);
