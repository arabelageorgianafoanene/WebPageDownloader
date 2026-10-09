namespace WebPageDownloaderTests.Services
{
    using System.Net;
    using Microsoft.Extensions.Logging.Abstractions;
    using Microsoft.Extensions.Options;
    using Polly;
    using Polly.Registry;
    using Polly.Retry;
    using WebPageDownloader.Configuration;
    using WebPageDownloader.Extensions;
    using WebPageDownloader.Storage;
    using Xunit;

    using WebPageDownloader = WebPageDownloader.Services.WebPageDownloader;

    public class WebPageDownloaderTests
    {
        private const int MaxRetries = 2;
        private static readonly Uri Ok1 = new("https://example.test/1");
        private static readonly Uri Ok2 = new("https://example.test/2");
        private static readonly Uri Bad = new("https://example.test/bad");

        [Fact]
        public async Task Success_ReturnsResultsInInputOrder()
        {
            var HttpClientHandler = new StubHandler((_, _) => Respond(HttpStatusCode.OK, 10));
            var webPageDownloader = CreateWebPageDownloader(HttpClientHandler, out _);

            var results = await webPageDownloader.DownloadAsync(new[] { Ok1, Ok2 }, CancellationToken.None);

            Assert.Equal(2, results.Count);
            Assert.All(results, r => Assert.True(r.Success));
            Assert.Equal(Ok1, results[0].Url);
            Assert.Equal(Ok2, results[1].Url);
        }

        [Fact]
        public async Task EmptyInput_ReturnsEmptyList()
        {
            var webPageDownloader = CreateWebPageDownloader(new StubHandler((_, _) => Respond(HttpStatusCode.OK, 1)), out _);

            var results = await webPageDownloader.DownloadAsync(Array.Empty<Uri>(), CancellationToken.None);

            Assert.Empty(results);
        }

        [Fact]
        public async Task TransientFailure_IsRetriedAndThenSucceeds()
        {
            var calls = 0;
            var HttpClientHandler = new StubHandler((_, _) =>
                Respond(Interlocked.Increment(ref calls) == 1
                    ? HttpStatusCode.ServiceUnavailable
                    : HttpStatusCode.OK, 10));
            var webPageDownloader = CreateWebPageDownloader(HttpClientHandler, out _);

            var results = await webPageDownloader.DownloadAsync(new[] { Ok1 }, CancellationToken.None);

            Assert.True(results[0].Success);
            Assert.Equal(2, calls);
        }

        [Fact]
        public async Task PersistentFailure_RetriesThenReportsFailure()
        {
            var calls = 0;
            var HttpClientHandler = new StubHandler((_, _) =>
            {
                Interlocked.Increment(ref calls);
                return Respond(HttpStatusCode.InternalServerError, 0);
            });
            var webPageDownloader = CreateWebPageDownloader(HttpClientHandler, out _);

            var results = await webPageDownloader.DownloadAsync(new[] { Bad }, CancellationToken.None);

            Assert.False(results[0].Success);
            Assert.Equal(500, results[0].StatusCode);
            Assert.Equal(1 + MaxRetries, calls); 
        }

        [Fact]
        public async Task OneFailure_DoesNotStopTheOthers_AndKeepsOrder()
        {
            var HttpClientHandler = new StubHandler((req, _) =>
                req.RequestUri == Bad
                    ? Respond(HttpStatusCode.NotFound, 0)
                    : Respond(HttpStatusCode.OK, 10));
            var webPageDownloader = CreateWebPageDownloader(HttpClientHandler, out _);

            var results = await webPageDownloader.DownloadAsync(new[] { Ok1, Bad, Ok2 }, CancellationToken.None);

            Assert.True(results[0].Success);
            Assert.False(results[1].Success);
            Assert.Equal(404, results[1].StatusCode);
            Assert.True(results[2].Success);
        }

        [Fact]
        public async Task ResponseOverSizeLimit_FailsWithoutSaving()
        {
            var HttpClientHandler = new StubHandler((_, _) => Respond(HttpStatusCode.OK, 200));
            var webPageDownloader = CreateWebPageDownloader(HttpClientHandler, out var store, maxFileSizeBytes: 100);

            var results = await webPageDownloader.DownloadAsync(new[] { Ok1 }, CancellationToken.None);

            Assert.False(results[0].Success);
            Assert.Equal(0, store.SaveCalls);
        }

        [Fact]
        public async Task Cancellation_StopsAndThrows()
        {
            using var cts = new CancellationTokenSource();
            var HttpClientHandler = new StubHandler(async (_, ct) =>
            {
                cts.Cancel();
                await Task.Delay(Timeout.Infinite, ct); 
                return await Respond(HttpStatusCode.OK, 1);
            });
            var webPageDownloader = CreateWebPageDownloader(HttpClientHandler, out _);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => webPageDownloader.DownloadAsync(new[] { Ok1, Ok2 }, cts.Token));
        }

        [Fact]
        public async Task Concurrency_NeverExceedsConfiguredMaximum()
        {
            var current = 0;
            var max = 0;
            var HttpClientHandler = new StubHandler(async (_, _) =>
            {
                var now = Interlocked.Increment(ref current);
                InterlockedMax(ref max, now);
                await Task.Delay(25);
                Interlocked.Decrement(ref current);
                return await Respond(HttpStatusCode.OK, 10);
            });
            var webPageDownloader = CreateWebPageDownloader(HttpClientHandler, out _, maxConcurrency: 3);
            var urls = Enumerable.Range(0, 12).Select(i => new Uri($"https://example.test/{i}")).ToList();

            var results = await webPageDownloader.DownloadAsync(urls, CancellationToken.None);

            Assert.All(results, r => Assert.True(r.Success));
            Assert.InRange(max, 1, 3);
        }

        private static WebPageDownloader CreateWebPageDownloader(
            StubHandler HttpClientHandler,
            out FakeStore store,
            int maxConcurrency = 4,
            long maxFileSizeBytes = 10_000)
        {
            store = new FakeStore();
            var options = Options.Create(new DownloaderOptions
            {
                MaxConcurrency = maxConcurrency,
                MaxFileSizeBytes = maxFileSizeBytes
            });

            var registry = new ResiliencePipelineRegistry<string>();
            registry.TryAddBuilder(DownloadResilience.PipelineName, (builder, _) =>
                builder.AddRetry(new RetryStrategyOptions
                {
                    MaxRetryAttempts = MaxRetries,
                    Delay = TimeSpan.Zero,
                    BackoffType = DelayBackoffType.Constant,
                    ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>()
                }));

            return new WebPageDownloader(
                new HttpClient(HttpClientHandler),
                store,
                options,
                registry,
                NullLogger<WebPageDownloader>.Instance);
        }

        private static Task<HttpResponseMessage> Respond(HttpStatusCode code, int bodyBytes) =>
            Task.FromResult(new HttpResponseMessage(code)
            {
                Content = new ByteArrayContent(new byte[bodyBytes])
            });

        private static void InterlockedMax(ref int target, int value)
        {
            int seen;
            while (value > (seen = Volatile.Read(ref target)))
            {
                if (Interlocked.CompareExchange(ref target, value, seen) == seen) return;
            }
        }

        private sealed class FakeStore : IPageStore
        {
            private int _saveCalls;
            public int SaveCalls => _saveCalls;

            public async Task<StoredPage> SaveAsync(Uri url, Stream content, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref _saveCalls);
                using var ms = new MemoryStream();
                await content.CopyToAsync(ms, cancellationToken);
                return new StoredPage($"fake/{url.Host}{url.AbsolutePath}", ms.Length);
            }
        }

        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;

            public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
                => _send = send;

            public StubHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> send)
                => _send = (r, ct) => Task.FromResult(send(r, ct));

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
                => _send(request, cancellationToken);
        }
    }
}
