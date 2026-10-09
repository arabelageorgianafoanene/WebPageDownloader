

using WebPageDownloader.Models;

namespace WebPageDownloader.Services
{
    interface IWebPageDownloader
    {
        Task<IReadOnlyList<DownloadResult>> DownloadAsync(
            IEnumerable<Uri> urls,
            CancellationToken cancellationToken);
    }
}
