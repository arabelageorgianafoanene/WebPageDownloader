

using WebPageDownloader.Models;

namespace WebPageDownloader.Services
{
    internal interface IWebPageDownloader
    {
        Task<IReadOnlyList<DownloadResult>> DownloadAsync(
            IEnumerable<Uri> urls,
            CancellationToken cancellationToken);
    }
}
