using WebPageDownloader.Models;

namespace WebPageDownloader.Storage
{
    public interface IManifestWriter
    {
        Task WriteAsync(IReadOnlyList<DownloadResult> results, CancellationToken cancellationToken);
    }
}
