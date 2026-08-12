using AFGCPCManager.Core.Devices;
using Microsoft.Win32;

namespace AFGCPCManager.Windows.Devices;

public sealed record VJoyDisplayNameUpdate(string Name, bool Changed);

public sealed class VJoyDirectInputNameManager
{
    internal const string OemRegistryPath =
        @"System\CurrentControlSet\Control\MediaProperties\PrivateProperties\Joystick\OEM\VID_1234&PID_BEAD";
    private const string OemNameValue = "OEMName";
    private const string DirectInputSuffix = " (DirectInput)";
    private static readonly string[] LegacyOrModeSuffixes =
        [" (Corrected)", " (DirectInput)", " (XInput)"];
    private const int MaximumNameLength = 255;

    private readonly Func<string?> _read;
    private readonly Action<string> _write;

    public VJoyDirectInputNameManager() : this(ReadCurrentName, WriteCurrentName) { }

    internal VJoyDirectInputNameManager(Func<string?> read, Action<string> write)
    {
        _read = read ?? throw new ArgumentNullException(nameof(read));
        _write = write ?? throw new ArgumentNullException(nameof(write));
    }

    public VJoyDisplayNameUpdate? Synchronize(IReadOnlyList<RegisteredController> controllers)
    {
        ArgumentNullException.ThrowIfNull(controllers);
        RegisteredController? selected = controllers
            .OrderBy(controller => controller.PreferredVJoyId ?? uint.MaxValue)
            .ThenBy(controller => controller.RegistrationOrder)
            .FirstOrDefault();
        if (selected is null) return null;

        string desired = BuildDirectInputName(selected.DisplayName);
        string? current = _read();
        if (string.Equals(current, desired, StringComparison.Ordinal))
            return new(desired, false);

        _write(desired);
        return new(desired, true);
    }

    internal static string BuildDirectInputName(string originalName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalName);
        string sanitized = new(originalName.Where(character => !char.IsControl(character)).ToArray());
        sanitized = sanitized.Trim();
        bool removed;
        do
        {
            removed = false;
            foreach (string suffix in LegacyOrModeSuffixes)
            {
                if (!sanitized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
                sanitized = sanitized[..^suffix.Length].TrimEnd();
                removed = true;
                break;
            }
        } while (removed);
        if (sanitized.Length == 0) sanitized = "Game Controller";

        int maximumBaseLength = MaximumNameLength - DirectInputSuffix.Length;
        if (sanitized.Length > maximumBaseLength)
            sanitized = sanitized[..maximumBaseLength].TrimEnd();
        return sanitized + DirectInputSuffix;
    }

    private static string? ReadCurrentName()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(OemRegistryPath, writable: false);
        if (key is null)
            throw new InvalidOperationException("The installed vJoy DirectInput registration was not found.");
        return key.GetValue(OemNameValue) as string;
    }

    private static void WriteCurrentName(string name)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(OemRegistryPath, writable: true);
        if (key is null)
            throw new InvalidOperationException("The installed vJoy DirectInput registration was not found.");
        key.SetValue(OemNameValue, name, RegistryValueKind.String);
    }
}
