namespace WebPageDownloader.Storage
{
    internal sealed class ResponseTooLargeException(Uri Url, long limit) : Exception($"Response from {Url} is too large. Limit: {limit} bytes.");
    
}
