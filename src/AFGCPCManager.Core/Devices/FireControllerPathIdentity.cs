using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace AFGCPCManager.Core.Devices;

public static partial class FireControllerPathIdentity
{
    public static bool IsMatch(string? path) =>
        !string.IsNullOrWhiteSpace(path) && DeviceTokenPattern().IsMatch(path);

    public static string NormalizeCollectionPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string withoutCollection = CollectionPattern().Replace(path, string.Empty, 1);
        return CollectionInstanceSuffixPattern().Replace(withoutCollection, string.Empty, 1);
    }

    public static string CreateStableId(string path, string? serialNumber = null)
    {
        string? serial = NormalizeSerialNumber(serialNumber);
        string groupKey = serial is null
            ? NormalizeCollectionPath(path).ToUpperInvariant()
            : "SERIAL:" + serial;
        return Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes("AFGC-PC-MANAGER\0" + groupKey)));
    }

    public static string? NormalizeSerialNumber(string? serialNumber)
    {
        if (string.IsNullOrWhiteSpace(serialNumber)) return null;
        string value = new(serialNumber.Where(character => character is not ':' and not '-').ToArray());
        if (value.Length != 12 || value.Any(character => !Uri.IsHexDigit(character))
            || value.All(character => character == '0') || value.All(character => character is 'F' or 'f'))
            return null;
        return value.ToUpperInvariant();
    }

    [GeneratedRegex(@"(?:^|[#_\\])(?:VID_1949&PID_0402|VID&00021949_PID&0402)(?=$|[&#\\])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DeviceTokenPattern();

    [GeneratedRegex(@"&COL[0-9A-F]{2}(?=#)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CollectionPattern();

    [GeneratedRegex(@"&[0-9A-F]{4}(?=#\{)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CollectionInstanceSuffixPattern();
}
