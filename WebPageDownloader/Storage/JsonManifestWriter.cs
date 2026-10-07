using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using WebPageDownloader.Configuration;
using WebPageDownloader.Models;

namespace WebPageDownloader.Storage
{
    public class JsonManifestWriter : IManifestWriter
    {
        private readonly string _manifestPath;
        private readonly ILogger<JsonManifestWriter> _logger;

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public JsonManifestWriter(IOptions<DownloaderOptions> options, ILogger<JsonManifestWriter> logger)
        {
            _logger = logger;
            _manifestPath = Path.Combine(
                    Path.GetFullPath(options.Value.OutputDirectory),
                    "manifest.json");
        }

        public async Task WriteAsync(IReadOnlyList<DownloadResult> results, CancellationToken cancellationToken)
        {
            var entries = results.Select(r => new ManifestEntry(r?.Url?.ToString()?? string.Empty, r?.FilePath?? string.Empty, r?.Success?? false, r?.ErrorMessage?? string.Empty));

            _logger.LogInformation("Writing manifest to {ManifestPath} with {Count} entries", _manifestPath, entries.Count());

            await using var stream = File.Create(_manifestPath);
            
            await JsonSerializer.SerializeAsync(stream, entries, JsonOptions, cancellationToken);

            _logger.LogInformation("Successfully wrote manifest to {ManifestPath}", _manifestPath);
        }
    }
}
