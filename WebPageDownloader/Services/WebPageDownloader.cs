using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Registry;
using Polly.Timeout;
using WebPageDownloader.Configuration;
using WebPageDownloader.Extensions;
using WebPageDownloader.Models;
using WebPageDownloader.Storage;

namespace WebPageDownloader.Services
{
    public sealed class WebPageDownloader : IWebPageDownloader
    {
        private readonly HttpClient _httpClient;
        private readonly IPageStore _store;
        private readonly int _maxConcurrency;

        private readonly long _maxFileSizeBytes;
        private readonly ResiliencePipeline _pipeline;

        private readonly ILogger<WebPageDownloader> _logger;

        public WebPageDownloader(HttpClient httpClient, IPageStore store, IOptions<DownloaderOptions> options, ResiliencePipelineProvider<string> provider, ILogger<WebPageDownloader> logger)
        {
            _httpClient = httpClient;
            _store = store;
            _maxConcurrency = options.Value.MaxConcurrency;
            _pipeline = provider.GetPipeline(DownloadResilience.PipelineName);
            _maxFileSizeBytes = options.Value.MaxFileSizeBytes;
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
               
               async (i, token) =>
               {
                   try
                   {
                       results[i] = await _pipeline.ExecuteAsync(async t => await DownloadPageAsync(list[i], t), token);
                   }
                   catch (OperationCanceledException) when (token.IsCancellationRequested)
                   {
                       throw;
                   }
                   catch (Exception ex)
                   {
                       var status = (ex as HttpRequestException)?.StatusCode;
                       var message = ex is TimeoutRejectedException
                           ? "Request timed out"
                           : ex.Message;

                       _logger.LogWarning(ex, "Failed to download {Url} after retries", list[i]);

                       results[i] = new DownloadResult(
                            list[i], false, status is null ? null : (int)status, null, null, message);
                   }
               });

            return results;
        }
        
        private async Task<DownloadResult> DownloadPageAsync(Uri url, CancellationToken cancellationToken)
        {
            try
            {
                using var response = await _httpClient.GetAsync(
                    url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                response.EnsureSuccessStatusCode();
                var statusCode = (int)response.StatusCode;

                if(response.Content.Headers.ContentLength > _maxFileSizeBytes)
                {
                    throw new ResponseTooLargeException(url, _maxFileSizeBytes);
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
        }
    }
}
