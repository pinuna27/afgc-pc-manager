namespace AFGCPCManager.App;

internal static class BatteryLevelDisplay
{
    public const string Unavailable = "—";

    public static string Format(byte? percentage) =>
        percentage is byte value && value <= 100
            ? $"{value}%"
            : Unavailable;
}
