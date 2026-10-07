using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebPageDownloader.Configuration;
using WebPageDownloader.Models;
using WebPageDownloader.Storage;

namespace WebPageDownloader.Services
{
    internal sealed class WebPageDownloader : IWebPageDownloader
    {
        private readonly HttpClient _httpClient;
        private readonly IPageStore _store;
        private readonly int _maxConcurrency;
        private readonly int _timeout;

        private readonly ILogger<WebPageDownloader> _logger;

        public WebPageDownloader(HttpClient httpClient, IPageStore store, IOptions<DownloaderOptions> options, ILogger<WebPageDownloader> logger)
        {
            _httpClient = httpClient;
            _store = store;
            _maxConcurrency = options.Value.MaxConcurrency;
            _timeout = options.Value.RequestTimeoutSeconds;
            _logger = logger;
        }

        public async Task<IReadOnlyList<DownloadResult>> DownloadAsync(IEnumerable<Uri> urls, CancellationToken cancellationToken)
        {
            var list = urls.ToList();
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

                _logger.LogInformation("Downloaded {Url} with status code {StatusCode}", url, (int)response.StatusCode);

                var statusCode = (int)response.StatusCode;

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Failed to download {Url} with status code {StatusCode} and reason {Reason}", url, statusCode, response.ReasonPhrase);

                    return new DownloadResult(
                        url, false, statusCode, null, null,
                        $"HTTP {statusCode} ({response.ReasonPhrase})");
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

                var stored = await _store.SaveAsync(url, stream, cancellationToken);

                _logger.LogInformation("Saved {Url} to {FilePath} ({FileSize} bytes)", url, stored.Path, stored.Bytes);

                return new DownloadResult(url, true, statusCode, stored.Path, stored.Bytes, null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Download of {Url} was canceled", url);
                throw;
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogWarning(ex, "Timed out downloading {Url}", url);
                return new DownloadResult(url, false, null, null, null,
                    $"Request timed out after {_timeout:0}s");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to download {Url}: {Message}", url, ex.Message);
                return new DownloadResult(url, false, null, null, null, ex.Message);
            }
        }
    }
}
