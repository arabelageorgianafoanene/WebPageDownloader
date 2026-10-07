using Microsoft.Extensions.Logging;
using WebPageDownloader.Input;
using WebPageDownloader.Services;
using WebPageDownloader.Storage;

namespace WebPageDownloader.Application
{
    internal sealed class DownloadApplication
    {
        private readonly IWebPageDownloader _downloader;
        private readonly IUrlSource _urlSource;

        private readonly IManifestWriter _manifestWriter;
        private readonly ILogger<DownloadApplication> _logger;

        public DownloadApplication(IWebPageDownloader downloader, IUrlSource urlSource, IManifestWriter manifestWriter, ILogger<DownloadApplication> logger)
        {
            _downloader = downloader;
            _urlSource = urlSource;
            _manifestWriter = manifestWriter;
            _logger = logger;
        }

        public async Task<int> RunAsync(CancellationToken cancellationToken = default)
        {
            UrlReadResult urlReadResult;

            try
            {
                urlReadResult = await _urlSource.ReadAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Error reading URLs.");
                return 1;
            }

            foreach (var invalid in urlReadResult.InvalidEntries)
            {
                _logger.LogWarning("Skipping invalid entry: {Entry}", invalid);
            }

            if (urlReadResult.Urls.Count == 0)
            {
                _logger.LogError("No valid URLs to download.");
                return 1;
            }

            var results = await _downloader.DownloadAsync(urlReadResult.Urls, cancellationToken);

            var succeededResultsCount = results.Count(r => r.Success);
            var failedResultsCount = results.Count - succeededResultsCount;

            _logger.LogInformation(
            "Done: {Succeeded} succeeded, {Failed} failed, {Invalid} invalid entries skipped.",
            succeededResultsCount, failedResultsCount, urlReadResult.InvalidEntries.Count);

            try
            {
                await _manifestWriter.WriteAsync(results, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Downloads finished, but the manifest could not be written.");
                return 1;
            }

            _logger.LogInformation("Manifest written successfully.");

            return succeededResultsCount == 0 ? 1 : 0;
        }
    }
}
