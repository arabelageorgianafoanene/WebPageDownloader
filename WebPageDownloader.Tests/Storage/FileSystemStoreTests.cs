
namespace WebPageDownloader.Tests.Storage
{
    using System.Text;
    using Microsoft.Extensions.Logging.Abstractions;
    using Microsoft.Extensions.Options;
    using WebPageDownloader.Configuration;
    using WebPageDownloader.Storage;
    using Xunit;

    public class FileSystemStoreTests : IDisposable
    {
        private static readonly Uri Url = new("https://example.test/page");
        private readonly string _dir =
            Path.Combine(Path.GetTempPath(), "-tests-" + Guid.NewGuid().ToString("N"));

        public FileSystemStoreTests() => Directory.CreateDirectory(_dir);

        public void Dispose()
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, recursive: true);
        }

        [Fact]
        public async Task Success_WritesCompleteFile_AndLeavesNoTempFiles()
        {
            var store = CreateStore();
            var content = Encoding.UTF8.GetBytes("<html>hello</html>");

            var stored = await store.SaveAsync(Url, new MemoryStream(content), CancellationToken.None);

            Assert.Equal(content, await File.ReadAllBytesAsync(stored.Path, CancellationToken.None));
            Assert.Equal(content.Length, stored.Bytes);

            Assert.Single(AllFiles());
            Assert.Empty(TempFiles());                                  
            Assert.False(stored.Path.EndsWith("temp", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task FailureMidWrite_LeavesNoFinalFile_AndNoTempFile()
        {
            var store = CreateStore();
            var stream = new FailingStream(
                firstChunk: new byte[] { 1, 2, 3, 4, 5 },
                onSecondRead: _ => throw new IOException("connection dropped"));

            await Assert.ThrowsAsync<IOException>(
                () => store.SaveAsync(Url, stream, CancellationToken.None));

            Assert.Empty(AllFiles());            
        }

        [Fact]
        public async Task FailureMidWrite_DoesNotCorruptAnExistingFile()
        {
            var store = CreateStore();
            var firstContent = Encoding.UTF8.GetBytes("ORIGINAL CONTENT");
            var firstFileSaved = await store.SaveAsync(Url, new MemoryStream(firstContent), CancellationToken.None);

            var failing = new FailingStream(
                firstChunk: Encoding.UTF8.GetBytes("NEW PARTIAL"),
                onSecondRead: _ => throw new IOException("connection dropped"));

            await Assert.ThrowsAsync<IOException>(
                () => store.SaveAsync(Url, failing, CancellationToken.None));

            Assert.Equal(firstContent, await File.ReadAllBytesAsync(firstFileSaved.Path, TestContext.Current.CancellationToken));
            Assert.Single(AllFiles());
            Assert.Empty(TempFiles());
        }

        [Fact]
        public async Task BodyOverSizeLimit_LeavesNoFiles()
        {
            var store = CreateStore(maxFileSizeBytes: 10);
            var tooBig = new MemoryStream(new byte[100]);

            await Assert.ThrowsAsync<ResponseTooLargeException>(
                () => store.SaveAsync(Url, tooBig, CancellationToken.None));

            Assert.Empty(AllFiles());
        }

        [Fact]
        public async Task CancellationMidWrite_LeavesNoFiles()
        {
            var store = CreateStore();
            using var cts = new CancellationTokenSource();
            var stream = new FailingStream(
                firstChunk: new byte[] { 1, 2, 3 },
                onSecondRead: ct =>
                {
                    cts.Cancel();
                    ct.ThrowIfCancellationRequested();
                });

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => store.SaveAsync(Url, stream, cts.Token));

            Assert.Empty(AllFiles());
        }

        private FileSystemStore CreateStore(long maxFileSizeBytes = 10_000) =>
            new(Options.Create(new DownloaderOptions
            {
                OutputDirectory = _dir,
                MaxFileSizeBytes = maxFileSizeBytes
            }),
                NullLogger<FileSystemStore>.Instance);

        private string[] AllFiles() =>
            Directory.GetFiles(_dir, "*", SearchOption.AllDirectories);

        private string[] TempFiles() =>
            AllFiles().Where(f => f.EndsWith("temp", StringComparison.OrdinalIgnoreCase)).ToArray();

        private sealed class FailingStream : Stream
        {
            private readonly byte[] _firstChunk;
            private readonly Action<CancellationToken> _onSecondRead;
            private bool _firstDone;

            public FailingStream(byte[] firstChunk, Action<CancellationToken> onSecondRead)
            {
                _firstChunk = firstChunk;
                _onSecondRead = onSecondRead;
            }

            private int ReadCore(Span<byte> destination, CancellationToken ct)
            {
                if (!_firstDone)
                {
                    _firstDone = true;
                    var n = Math.Min(destination.Length, _firstChunk.Length);
                    _firstChunk.AsSpan(0, n).CopyTo(destination);
                    return n;
                }

                _onSecondRead(ct); 
                return 0;         
            }

            public override int Read(byte[] buffer, int offset, int count) =>
                ReadCore(buffer.AsSpan(offset, count), CancellationToken.None);

            public override ValueTask<int> ReadAsync(
                Memory<byte> buffer, CancellationToken cancellationToken = default) =>
                new(ReadCore(buffer.Span, cancellationToken));

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
