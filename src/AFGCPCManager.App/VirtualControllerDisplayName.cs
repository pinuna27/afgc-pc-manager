using AFGCPCManager.Core.Settings;

namespace AFGCPCManager.App;

internal static class VirtualControllerDisplayName
{
    private static readonly string[] KnownSuffixes =
        [" (Corrected)", " (DirectInput)", " (XInput)"];

    public static string Format(string originalName, GamepadOutputMode mode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalName);
        string name = new(originalName.Where(character => !char.IsControl(character)).ToArray());
        name = name.Trim();
        bool removed;
        do
        {
            removed = false;
            foreach (string suffix in KnownSuffixes)
            {
                if (!name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
                name = name[..^suffix.Length].TrimEnd();
                removed = true;
                break;
            }
        } while (removed);
        if (name.Length == 0) name = "Game Controller";

        return $"{name} ({(mode == GamepadOutputMode.XInput ? "XInput" : "DirectInput")})";
    }
}
