namespace AFGCPCManager.Setup.Core;

public static class CommandLineArguments
{
    public static bool Has(IReadOnlyCollection<string> arguments, string key) =>
        arguments.Contains(key, StringComparer.OrdinalIgnoreCase);

    public static string? Get(IReadOnlyList<string> arguments, string key)
    {
        for (int index = 0; index < arguments.Count; index++)
        {
            if (!arguments[index].Equals(key, StringComparison.OrdinalIgnoreCase))
                continue;
            return index + 1 < arguments.Count ? arguments[index + 1] : null;
        }
        return null;
    }

    public static string[] Without(IReadOnlyList<string> arguments, string key) =>
        arguments.Where(argument => !argument.Equals(
            key, StringComparison.OrdinalIgnoreCase)).ToArray();

    public static string[] WithValue(
        IReadOnlyList<string> arguments, string key, string value)
    {
        var result = arguments.ToList();
        for (int index = 0; index < result.Count; index++)
        {
            if (!result[index].Equals(key, StringComparison.OrdinalIgnoreCase))
                continue;
            if (index + 1 < result.Count)
                result[index + 1] = value;
            else
                result.Add(value);
            return result.ToArray();
        }
        result.Add(key);
        result.Add(value);
        return result.ToArray();
    }
}
