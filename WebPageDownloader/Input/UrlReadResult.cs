namespace WebPageDownloader.Input
{
    public record UrlReadResult(IReadOnlyList<Uri> Urls, IReadOnlyList<string> InvalidEntries);    
}
