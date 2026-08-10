namespace AFGCPCManager.Core.Devices;

public static class ControllerIdentificationLightPattern
{
    private static readonly byte[] Masks =
    [
        // Physical LED direction is reversed from the controller's bit numbering.
        0b1000, 0b0100, 0b0010, 0b0001,
        0b1100, 0b1010, 0b0110, 0b1001,
        0b0101, 0b0011, 0b1110, 0b1101,
        0b1011, 0b0111, 0b1111
    ];

    public static byte ForRegistrationOrder(int registrationOrder)
    {
        if (registrationOrder < 1)
            throw new ArgumentOutOfRangeException(nameof(registrationOrder));
        return Masks[(registrationOrder - 1) % Masks.Length];
    }
}
