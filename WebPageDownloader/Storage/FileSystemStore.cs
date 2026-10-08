
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
using WebPageDownloader.Configuration;

namespace WebPageDownloader.Storage
{
    public class FileSystemStore : IPageStore
    {
        private readonly string _root;

        private readonly ILogger<FileSystemStore> _logger;

        public FileSystemStore(IOptions<DownloaderOptions> options, ILogger<FileSystemStore> logger)
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

        public async Task<StoredPage> SaveAsync(Uri url, Stream content, long limit, CancellationToken cancellationToken)
        {
            var saved = false;

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
                    await StreamLimiter.CopyWithLimitAsync(content, file, limit, url, cancellationToken);
                    await file.FlushAsync(cancellationToken);
                    bytes = file.Length;
                }

                _logger.LogInformation("Successfully saved page for URL {Url} to {TempPath} ({Bytes} bytes)", url, tempPath, bytes);

                File.Move(tempPath, finalPath, overwrite: true);
                _logger.LogInformation("Saved page for URL {Url} to {FilePath} ({Bytes} bytes)", url, finalPath, bytes);

                saved = true;
                return new StoredPage(finalPath, bytes);
            }
            catch(Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError("Failed to save page for URL {Url} to {TempPath}", url, tempPath);
                throw;
            }
            finally
            {
                if(!saved)
                {
                    TryDelete(tempPath);
                }   
            }
        }

        private void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "Failed to delete file {FilePath}", path);
            }
        }
    }
}
