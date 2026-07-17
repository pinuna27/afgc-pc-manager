using System.Security.Cryptography;
using System.Text.Json;
using AFGCPCManager.Setup.Core.Models;

if (args.Length < 4)
{
    Console.Error.WriteLine("Usage: ReleaseManifest.Builder <version> <dependencies.json> <manifest.json> <asset> [asset ...]");
    return 2;
}

if (!Version.TryParse(args[0], out Version? version))
{
    Console.Error.WriteLine("Release version is invalid.");
    return 2;
}

var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
DependencyPins pins = JsonSerializer.Deserialize<DependencyPins>(await File.ReadAllTextAsync(args[1]), jsonOptions)
    ?? throw new InvalidDataException("Dependency pins are missing or invalid.");
var assets = new List<ReleaseAsset>();
foreach (string path in args[3..])
{
    var file = new FileInfo(path);
    if (!file.Exists) throw new FileNotFoundException("Release asset is missing.", path);
    await using FileStream stream = file.OpenRead();
    string hash = Convert.ToHexString(await SHA256.HashDataAsync(stream));
    assets.Add(new(file.Name, hash, file.Length));
}

var manifest = new ReleaseManifest
{
    Version = version.ToString(),
    Architecture = "x64",
    PublishedAtUtc = DateTimeOffset.UtcNow,
    Assets = assets,
    VJoy = pins.VJoy,
    HidHide = pins.HidHide
};
string output = Path.GetFullPath(args[2]);
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
await File.WriteAllTextAsync(output, JsonSerializer.Serialize(manifest, jsonOptions) + Environment.NewLine);
Console.WriteLine($"Created manifest for {version} with {assets.Count} assets.");
return 0;

internal sealed record DependencyPins(DependencyRelease VJoy, DependencyRelease HidHide);
