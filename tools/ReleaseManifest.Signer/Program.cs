using System.Security.Cryptography;

if (args.Length != 4) { Console.Error.WriteLine("Usage: ReleaseManifest.Signer <private-key.pem> <public-key.pem> <manifest.json> <manifest.sig>"); return 2; }
byte[] manifest = await File.ReadAllBytesAsync(args[2]); using ECDsa key = ECDsa.Create(); key.ImportFromPem(await File.ReadAllTextAsync(args[0]));
byte[] signature = key.SignData(manifest, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
using ECDsa publicKey = ECDsa.Create(); publicKey.ImportFromPem(await File.ReadAllTextAsync(args[1]));
if (!publicKey.VerifyData(manifest, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) throw new CryptographicException("The signing key does not match the trusted public key.");
await File.WriteAllBytesAsync(args[3], signature); Console.WriteLine($"Signed and verified {Path.GetFileName(args[2])} against the trusted public key."); return 0;
