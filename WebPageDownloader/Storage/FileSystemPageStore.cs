
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
using WebPageDownloader.Configuration;

namespace WebPageDownloader.Storage
{
    public class FileSystemPageStore : IPageStore
    {
        private readonly string _root;

        private readonly ILogger<FileSystemPageStore> _logger;

        public FileSystemPageStore(IOptions<DownloaderOptions> options, ILogger<FileSystemPageStore> logger)
        {
            _logger = logger;

            _root = Path.GetFullPath(options.Value.OutputDirectory);
            Directory.CreateDirectory(_root);

            _logger.LogInformation("Using output directory: {OutputDirectory}", _root);

            foreach (var staleFile in Directory.EnumerateFiles(_root, "*.tmp"))
            {
                _logger.LogInformation("Found stale file {FilePath}", staleFile);
                TryDelete(staleFile);
            }
        }

        public async Task<StoredPage> SaveAsync(Uri url, Stream content, CancellationToken cancellationToken)
        {
            var fileName = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(url.AbsoluteUri))) + ".html";

            var finalPath = Path.Combine(_root, fileName);
            var tempPath = $"{finalPath}.{Guid.NewGuid():N}.tmp";

            _logger.LogInformation("Saving page for URL {Url} to {TempPath}", url, tempPath);

            try
            {
                long bytes;

                await using (var file = new FileStream(
                    tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    bufferSize: 81920, useAsync: true))
                {
                    await content.CopyToAsync(file, cancellationToken);
                    await file.FlushAsync(cancellationToken);
                    bytes = file.Length;
                }

                _logger.LogInformation("Successfully saved page for URL {Url} to {TempPath} ({Bytes} bytes)", url, tempPath, bytes);

                File.Move(tempPath, finalPath, overwrite: true);
                _logger.LogInformation("Saved page for URL {Url} to {FilePath} ({Bytes} bytes)", url, finalPath, bytes);

                return new StoredPage(finalPath, bytes);
            }
            catch
            {
                _logger.LogError("Failed to save page for URL {Url} to {TempPath}", url, tempPath);
                TryDelete(tempPath);
                throw;
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
             
            }
        }
    }
}
