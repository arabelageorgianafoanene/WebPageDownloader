namespace WebPageDownloader.Storage
{
    public interface IPageStore
    {
        Task<StoredPage> SaveAsync(Uri url, Stream content, CancellationToken ct);
    }
}
