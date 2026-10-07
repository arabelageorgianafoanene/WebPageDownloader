

namespace WebPageDownloader.Input
{
    public interface IUrlSource
    {
        Task<UrlReadResult> ReadAsync(CancellationToken cancellationToken = default);
    }
}
