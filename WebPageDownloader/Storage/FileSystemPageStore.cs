
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
using WebPageDownloader.Configuration;

namespace WebPageDownloader.Storage
{
    public class FileSystemPageStore : IPageStore
    {
        private readonly string _root;

        public FileSystemPageStore(IOptions<DownloaderOptions> options)
        {
            _root = Path.GetFullPath(options.Value.OutputDirectory);
            Directory.CreateDirectory(_root);

            foreach (var staleFile in Directory.EnumerateFiles(_root, "*.tmp"))
            {
                TryDelete(staleFile);
            }
        }

        public async Task<StoredPage> SaveAsync(Uri url, Stream content, CancellationToken cancellationToken)
        {
            var fileName = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(url.AbsoluteUri))) + ".html";

            var finalPath = Path.Combine(_root, fileName);
            var tempPath = $"{finalPath}.{Guid.NewGuid():N}.tmp";

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

                File.Move(tempPath, finalPath, overwrite: true);

                return new StoredPage(finalPath, bytes);
            }
            catch
            {
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
