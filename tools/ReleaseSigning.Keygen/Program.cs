using System.Security.Cryptography;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: ReleaseSigning.Keygen <private-key.pem> <public-key.pem>");
    return 2;
}
string privatePath = Path.GetFullPath(args[0]), publicPath = Path.GetFullPath(args[1]);
if (File.Exists(privatePath) || File.Exists(publicPath))
{
    Console.Error.WriteLine("Refusing to overwrite an existing signing key.");
    return 1;
}
Directory.CreateDirectory(Path.GetDirectoryName(privatePath)!); Directory.CreateDirectory(Path.GetDirectoryName(publicPath)!);
using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
File.WriteAllText(privatePath, key.ExportPkcs8PrivateKeyPem());
File.WriteAllText(publicPath, key.ExportSubjectPublicKeyInfoPem());
Console.WriteLine($"Generated ECDSA P-256 key pair. Private key saved to ignored path: {privatePath}");
return 0;
