using Microsoft.Extensions.Logging;
using System.Diagnostics.CodeAnalysis;

namespace WebPageDownloader.Input
{
    public class FileUrlSource : IUrlSource
    {
        private readonly string _filePath;
        private readonly ILogger<FileUrlSource> _logger;

        public FileUrlSource(string filePath, ILogger<FileUrlSource> logger)
        {
            _filePath = filePath;
            _logger = logger;   
        }
        public async Task<UrlReadResult> ReadAsync(CancellationToken cancellationToken = default)
        {
            var urls = new List<Uri>();
            var seenUrls = new HashSet<Uri>();
            var invalidLines = new List<string>();

            var lineNumber = 0;

            await foreach(var line in File.ReadLinesAsync(_filePath, cancellationToken))
            {
                lineNumber++;
                var trimmedLine = line.Trim();

                if (trimmedLine.Length == 0 || trimmedLine.StartsWith('#'))
                {
                    _logger.LogDebug("Skipping line {LineNumber}: {Line}", lineNumber, trimmedLine);
                    continue;
                }

                if (!TryParseHttpUrl(trimmedLine, out var uri))
                {
                    _logger.LogWarning("Invalid URL on line {LineNumber}: {Line}", lineNumber, trimmedLine);
                    invalidLines.Add($"Line {lineNumber}: {trimmedLine}");
                    continue;
                }

                if (seenUrls.Add(uri))
                {
                    urls.Add(uri);
                }
                else
                {
                    _logger.LogDebug("Skipping duplicate URL on line {LineNumber}: {Url}", lineNumber, uri);
                }
            }

            return new UrlReadResult(urls, invalidLines);
        }

        private static bool TryParseHttpUrl(string value, [NotNullWhen(true)] out Uri? uri) =>
            Uri.TryCreate(value, UriKind.Absolute, out uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
            !string.IsNullOrEmpty(uri.Host);
    }
}
