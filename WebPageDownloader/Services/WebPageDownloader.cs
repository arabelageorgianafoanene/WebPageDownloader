using Microsoft.Extensions.Options;
using WebPageDownloader.Configuration;
using WebPageDownloader.Models;
using WebPageDownloader.Storage;

namespace WebPageDownloader.Services
{
    internal class WebPageDownloader : IWebPageDownloader
    {
        private readonly HttpClient _httpClient;
        private readonly IPageStore _store;
        private readonly int _maxConcurrency;

        public WebPageDownloader(HttpClient httpClient, IPageStore store, IOptions<DownloaderOptions> options)
        {
            _httpClient = httpClient;
            _store = store;
            _maxConcurrency = options.Value.MaxConcurrency;
        }

        public async Task<IReadOnlyList<DownloadResult>> DownloadAsync(IEnumerable<Uri> urls, CancellationToken cancellationToken)
        {
            var list = urls.Distinct().ToList();
            var results = new DownloadResult[list.Count];

            await Parallel.ForEachAsync(
                Enumerable.Range(0, list.Count),
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = _maxConcurrency,
                    CancellationToken = cancellationToken
                },
                async (i, ct) => results[i] = await DownloadPageAsync(list[i], ct));

            return results;
        }
        
        private async Task<DownloadResult> DownloadPageAsync(Uri url, CancellationToken cancellationToken)
        {
            try
            {
                using var response = await _httpClient.GetAsync(
                    url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                var statusCode = (int)response.StatusCode;

                if (!response.IsSuccessStatusCode)
                {
                    return new DownloadResult(
                        url, false, statusCode, null, null,
                        $"HTTP {statusCode} ({response.ReasonPhrase})");
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

                var stored = await _store.SaveAsync(url, stream, cancellationToken);

                return new DownloadResult(url, true, statusCode, stored.Path, stored.Bytes, null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                return new DownloadResult(url, false, null, null, null, ex.Message);
            }
        }
    }
}
