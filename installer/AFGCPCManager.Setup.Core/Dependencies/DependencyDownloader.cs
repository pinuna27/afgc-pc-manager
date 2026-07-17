using AFGCPCManager.Setup.Core.Models;
using AFGCPCManager.Setup.Core.Security;

namespace AFGCPCManager.Setup.Core.Dependencies;

public sealed class DependencyDownloader(HttpClient client, PackageVerifier verifier)
{
    public async Task<string> DownloadVerifiedAsync(DependencyManifestEntry entry, string directory, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(directory);
        string destination = Path.Combine(directory, entry.FileName), temporary = destination + ".download";
        using HttpResponseMessage response = await client.GetAsync(entry.DownloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        long? total = response.Content.Headers.ContentLength; long copied = 0;
        await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using FileStream target = new(temporary, FileMode.Create, FileAccess.Write, FileShare.None);
        byte[] buffer = new byte[81920]; int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0) { await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken); copied += read; if (total > 0) progress?.Report((double)copied / total.Value); }
        await target.FlushAsync(cancellationToken); target.Close();
        VerificationResult result = await verifier.VerifyAsync(temporary, entry, cancellationToken);
        if (!result.IsValid) { File.Delete(temporary); throw new InvalidDataException($"{entry.Name}: {result.FailureReason}"); }
        File.Move(temporary, destination, true); return destination;
    }
}
