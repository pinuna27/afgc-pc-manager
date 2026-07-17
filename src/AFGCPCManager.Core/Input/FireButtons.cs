namespace AFGCPCManager.Core.Input;

[Flags]
public enum FireButtons : ushort
{
    None = 0,
    A = 1 << 0,
    B = 1 << 1,
    X = 1 << 2,
    Y = 1 << 3,
    LeftShoulder = 1 << 4,
    RightShoulder = 1 << 5,
    Back = 1 << 6,
    Menu = 1 << 7,
    GameCircle = 1 << 8,
    LeftThumb = 1 << 9,
    RightThumb = 1 << 10
}
