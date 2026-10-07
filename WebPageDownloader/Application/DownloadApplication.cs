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
            var input = await _urlSource.ReadAsync(cancellationToken);

            foreach (var invalid in input.InvalidEntries)
            {
                _logger.LogWarning("Skipping invalid entry: {Entry}", invalid);
            }

            if (input.Urls.Count == 0)
            {
                _logger.LogError("No valid URLs to download.");
                return 1;
            }

            var results = await _downloader.DownloadAsync(input.Urls, cancellationToken);

            var succeededResultsCount = results.Count(r => r.Success);
            var failedResultsCount = results.Count - succeededResultsCount;

            _logger.LogInformation(
            "Done: {Succeeded} succeeded, {Failed} failed, {Invalid} invalid entries skipped.",
            succeededResultsCount, failedResultsCount, input.InvalidEntries.Count);

            await _manifestWriter.WriteAsync(results, cancellationToken);
            _logger.LogInformation("Manifest written successfully.");

            return succeededResultsCount == 0 ? 1 : 0;
        }
    }
}
