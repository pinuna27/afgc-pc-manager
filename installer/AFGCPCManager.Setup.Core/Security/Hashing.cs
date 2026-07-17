using System.Security.Cryptography;

namespace AFGCPCManager.Setup.Core.Security;

public static class Hashing
{
    public static async Task<string> Sha256Async(string path, CancellationToken cancellationToken = default)
    {
        await using FileStream stream = File.OpenRead(path);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }
}
