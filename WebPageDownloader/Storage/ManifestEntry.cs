namespace WebPageDownloader.Storage
{
    internal record ManifestEntry(string Url, string FilePath, bool Success, string ErrorMessage);
}
