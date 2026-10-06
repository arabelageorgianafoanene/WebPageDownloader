using WebPageDownloader.Models;

namespace WebPageDownloader.Services
{
    internal class WebPageDownloader : IWebPageDownloader
    {
        private const int MaxConcurrency = 10;
        private readonly HttpClient _httpClient;

        public WebPageDownloader(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<IReadOnlyList<DownloadResult>> DownloadAsync(IEnumerable<Uri> urls, CancellationToken cancellationToken)
        {
            using var throttler = new SemaphoreSlim(MaxConcurrency);   
                        
            var tasks = urls
                .Select(url => DownloadThrottledAsync(url, throttler, cancellationToken))  
                .ToList();

            return await Task.WhenAll(tasks);
        }

        private async Task<DownloadResult> DownloadThrottledAsync(Uri url, SemaphoreSlim throttler, CancellationToken cancellationToken)
        {
            await throttler.WaitAsync(cancellationToken);

            try
            {
                return await DownloadPageAsync(url, cancellationToken);
            }
            finally
            {
                throttler.Release();                        
            }
        }

        private async Task<DownloadResult> DownloadPageAsync(Uri url, CancellationToken cancellationToken)
        {
            try
            {
                using var response = await _httpClient.GetAsync(
                    url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                var content = await response.Content.ReadAsStringAsync(cancellationToken);

                return new DownloadResult(
                    url, response.IsSuccessStatusCode, (int)response.StatusCode, content, null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                return new DownloadResult(url, false, null, null, ex.Message);
            }
        }
    }
}
