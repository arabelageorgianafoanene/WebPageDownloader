using System.Buffers;

namespace WebPageDownloader.Storage
{
    internal static class StreamLimiter
    {
        public static async Task CopyWithLimitAsync(
        Stream source, Stream destination, long limit, Uri url, CancellationToken ct)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(81920);
            try
            {
                long total = 0;
                int read;
                while ((read = await source.ReadAsync(buffer.AsMemory(), ct)) > 0)
                {
                    total += read;
                    if (total > limit)
                        throw new ResponseTooLargeException(url, limit);

                    await destination.WriteAsync(buffer.AsMemory(0, read), ct);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }
}
