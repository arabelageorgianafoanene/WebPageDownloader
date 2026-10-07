using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using WebPageDownloader.Configuration;
using WebPageDownloader.Models;

namespace WebPageDownloader.Storage
{
    public sealed class JsonManifestWriter : IManifestWriter
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
            var entries = results.Select(r => new ManifestEntry(r.Url.ToString(), r.FilePath is null ? null : Path.GetFileName(r.FilePath), r.Success, r.ErrorMessage)).ToList();

            _logger.LogInformation("Writing manifest to {ManifestPath} with {Count} entries", _manifestPath, entries.Count);

            await using var stream = File.Create(_manifestPath);
            
            await JsonSerializer.SerializeAsync(stream, entries, JsonOptions, cancellationToken);

            _logger.LogInformation("Successfully wrote manifest to {ManifestPath}", _manifestPath);
        }
    }
}
