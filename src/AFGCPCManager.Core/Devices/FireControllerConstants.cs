namespace AFGCPCManager.Core.Devices;

public static class FireControllerConstants
{
    public const ushort VendorId = 0x1949;
    public const ushort ProductId = 0x0402;
    public const byte GamepadReportId = 0x01;
    public const int GamepadReportLength = 11;
    public const byte ConsumerReportId = 0x02;
    public const int ConsumerReportLength = 2;
    public const string BluetoothName = "Amazon Fire Game Controller";

    // Nominal neutral values confirmed by the guided hardware capture.
    public const byte LeftXCenter = 128;
    public const byte LeftYCenter = 127;
    public const byte RightXCenter = 128;
    public const byte RightYCenter = 127;
}
