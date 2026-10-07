using Microsoft.Extensions.Logging;
using WebPageDownloader.Input;
using WebPageDownloader.Services;

namespace WebPageDownloader.Application
{
    internal sealed class DownloadApplication
    {
        private readonly IWebPageDownloader _downloader;
        private readonly IUrlSource _urlSource;
        private readonly ILogger<DownloadApplication> _logger;

        public DownloadApplication(IWebPageDownloader downloader, IUrlSource urlSource, ILogger<DownloadApplication> logger)
        {
            _downloader = downloader;
            _urlSource = urlSource;
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

            var succeeded = results.Count(r => r.Success);
            var failed = results.Count - succeeded;

            _logger.LogInformation(
            "Done: {Succeeded} succeeded, {Failed} failed, {Invalid} invalid entries skipped.",
            succeeded, failed, input.InvalidEntries.Count);

            return succeeded == 0 ? 1 : 0;
        }
    }
}
