namespace WebPageDownloader.Storage
{
    public interface IPageStore
    {
        Task<StoredPage> SaveAsync(Uri url, Stream content, long limit, CancellationToken ct);
    }
}
