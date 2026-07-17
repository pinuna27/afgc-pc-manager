using System.Reflection;

namespace AFGCPCManager.Setup.Core.Security;

public static class TrustedReleaseKeys
{
    public static string LoadAfgcPublicKey()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AFGCPCManager.ReleaseSigning.PublicKey.pem") ?? throw new InvalidOperationException("The embedded release verification key is missing.");
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    }
}
