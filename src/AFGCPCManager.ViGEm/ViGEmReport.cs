namespace AFGCPCManager.ViGEm;

internal readonly record struct ViGEmReport(
    ushort Buttons,
    byte LeftTrigger,
    byte RightTrigger,
    short LeftThumbX,
    short LeftThumbY,
    short RightThumbX,
    short RightThumbY);
