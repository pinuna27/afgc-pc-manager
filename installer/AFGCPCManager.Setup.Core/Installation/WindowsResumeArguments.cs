using System.Text.Json;
using Microsoft.Win32;

namespace AFGCPCManager.Setup.Core.Installation;

public static class WindowsResumeArguments
{
    public const string Argument = "--resume-arguments";

    public static string[] Expand(string[] arguments)
    {
        int index = Array.FindIndex(arguments, value => value.Equals(Argument, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return arguments;
        if (Array.FindIndex(arguments, index + 1,
                value => value.Equals(Argument, StringComparison.OrdinalIgnoreCase)) >= 0)
            throw new ArgumentException("The setup resume-arguments option was specified more than once.");
        if (index + 1 >= arguments.Length) throw new ArgumentException("The setup resume-arguments path is missing.");

        string path = Path.GetFullPath(arguments[index + 1]);
        var errors = new List<Exception>();
        var candidates = new List<string>();
        try
        {
            string? registry = ReadBackup(path);
            if (registry is not null) candidates.Add(registry);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        { errors.Add(ex); }
        try
        {
            if (File.Exists(path)) candidates.Add(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        { errors.Add(ex); }

        foreach (string json in candidates.Distinct(StringComparer.Ordinal))
        {
            try
            {
                string[] stored = JsonSerializer.Deserialize<string[]>(json)
                    ?? throw new InvalidDataException("The setup resume arguments are empty.");
                if (stored.Any(value => value is null || value.Equals(Argument, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("The setup resume arguments contain invalid values.");
                return [.. arguments[..index], .. stored, .. arguments[(index + 2)..]];
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException) { errors.Add(ex); }
        }

        if (candidates.Count == 0 && errors.Count == 0)
            throw new FileNotFoundException($"The setup resume arguments could not be recovered from '{path}'.", path);
        throw new InvalidDataException("The setup resume arguments are invalid or unreadable.", new AggregateException(errors));
    }

    internal static void Write(string path, IEnumerable<string> arguments)
    {
        string fullPath = Path.GetFullPath(path);
        string json = JsonSerializer.Serialize(arguments);
        using RegistryKey key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\AFGC PC Manager\Resume", true);
        key.SetValue(BackupName(fullPath), json, RegistryValueKind.String);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            string temporary = fullPath + ".new";
            File.WriteAllText(temporary, json);
            File.Move(temporary, fullPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The registry copy is authoritative. The file is only redundant recovery storage.
        }
    }

    internal static void Delete(string path)
    {
        string fullPath = Path.GetFullPath(path);
        using (RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\AFGC PC Manager\Resume", writable: true))
            key?.DeleteValue(BackupName(fullPath), throwOnMissingValue: false);
        try { if (File.Exists(fullPath)) File.Delete(fullPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The registry copy and startup trigger are authoritative. A redundant file
            // that could not be removed is harmless and can be overwritten next time.
        }
    }

    private static string? ReadBackup(string path)
    {
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\AFGC PC Manager\Resume");
        return key?.GetValue(BackupName(path))?.ToString();
    }

    private static string BackupName(string path) => new DirectoryInfo(Path.GetDirectoryName(path)!).Name;

    internal static string BuildCommand(string executable, string argumentsPath) =>
        WindowsSetupResumeRegistration.Quote(executable) + " " + Quote(Argument) + " " + Quote(argumentsPath);

    private static string Quote(string value) => WindowsSetupResumeRegistration.Quote(value);
}
