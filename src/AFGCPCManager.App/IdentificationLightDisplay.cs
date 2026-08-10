namespace AFGCPCManager.App;

internal static class IdentificationLightDisplay
{
    public const string OnGlyph = "■";
    public const string OffGlyph = "—";

    public static string Format(byte? mask)
    {
        if (mask is null) return "Not controlled";
        // Display the characters in the user's front-to-back viewing order,
        // independently of the controller's reversed physical bit numbering.
        return string.Join(' ', Enumerable.Range(0, 4).Reverse()
            .Select(bit => (mask.Value & (1 << bit)) != 0 ? OnGlyph : OffGlyph));
    }
}
