namespace WebPageDownloader.Models
{
    public record DownloadResult(
        Uri Url,
        bool Success,
        int? StatusCode,
        string? Content,
        string? Error);
}
