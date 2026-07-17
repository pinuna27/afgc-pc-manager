namespace AFGCPCManager.Core.Input;

[Flags]
public enum ConsumerButtons : byte
{
    None = 0,
    FastForward = 1 << 0,
    Rewind = 1 << 1,
    PlayPause = 1 << 2,
    Home = 1 << 3
}
