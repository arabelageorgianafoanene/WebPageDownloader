

using WebPageDownloader.Models;

namespace WebPageDownloader.Services
{
  public interface IWebPageDownloader
    {
        public Task<IReadOnlyList<DownloadResult>> DownloadAsync(
            IEnumerable<Uri> urls,
            CancellationToken cancellationToken);
    }
}
