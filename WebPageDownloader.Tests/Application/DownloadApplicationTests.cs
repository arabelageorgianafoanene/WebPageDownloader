using Castle.Core.Resource;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using WebPageDownloader.Application;
using WebPageDownloader.Input;
using WebPageDownloader.Models;
using WebPageDownloader.Services;
using WebPageDownloader.Storage;

namespace WebPageDownloaderTests;

public class DownloadApplicationTests
{
    private static readonly Uri Url1 = new("https://a.test/1");
    private static readonly Uri Url2 = new("https://a.test/2");

    private readonly IUrlSource _urlSource;
    private readonly IWebPageDownloader _webPageDownloader;
    private readonly IManifestWriter _manifest;
    private readonly ILogger<DownloadApplication> _logger;

    private readonly DownloadApplication _downloadApplication;

    public DownloadApplicationTests()
    {
        _urlSource = Substitute.For<IUrlSource>();
        _webPageDownloader = Substitute.For<IWebPageDownloader>();
        _manifest = Substitute.For<IManifestWriter>();
        _logger= Substitute.For<ILogger<DownloadApplication>>();
        _downloadApplication = new DownloadApplication(_webPageDownloader, _urlSource, _manifest, _logger);
    }

    [Fact]
    public async Task AllDownloadsSucceed_ReturnsZero_AndWritesManifest()
    {
        var urls = new[] { Url1, Url2 };
        var downloadedResults = new[] { Ok(Url1), Ok(Url2) };

        _urlSource.ReadAsync(Arg.Any<CancellationToken>())
            .Returns(new UrlReadResult(urls, new List<string>()));

         _webPageDownloader.DownloadAsync(Arg.Any<IEnumerable<Uri>>(), Arg.Any<CancellationToken>()).Returns(downloadedResults);

        var exitCode = await _downloadApplication.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);

       await _manifest.Received(1).WriteAsync(
            Arg.Any<IReadOnlyList<DownloadResult>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SomeDownloadsFail_StillReturnsZero()
    {
        var urls = new[] { Url1, Url2 };
        var downloadedResults = new[] { Ok(Url1), Fail(Url2) };

        _urlSource.ReadAsync(Arg.Any<CancellationToken>())
            .Returns(new UrlReadResult(urls, new List<string>()));

        _webPageDownloader.DownloadAsync(Arg.Any<IEnumerable<Uri>>(), Arg.Any<CancellationToken>()).Returns(downloadedResults);

        var exitCode = await _downloadApplication.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        await _manifest.Received(1).WriteAsync(
           Arg.Any<IReadOnlyList<DownloadResult>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AllDownloadsFail_ReturnsOne_ButStillWritesManifest()
    {
        var urls = new[] { Url1, Url2 };
        var downloadedResults = new[] { Fail(Url1), Fail(Url2) };

        _urlSource.ReadAsync(Arg.Any<CancellationToken>())
            .Returns(new UrlReadResult(urls, new List<string>()));

        _webPageDownloader.DownloadAsync(Arg.Any<IEnumerable<Uri>>(), Arg.Any<CancellationToken>()).Returns(downloadedResults);

        var exitCode = await _downloadApplication.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, exitCode);
        await _manifest.Received(1).WriteAsync(
           Arg.Any<IReadOnlyList<DownloadResult>>(), Arg.Any<CancellationToken>());
    }
        
    [Fact]
    public async Task UrlSourceThrows_ReturnsOne_AndDownloadsNothing()
    {
        _urlSource.ReadAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new FileNotFoundException("urls.txt missing"));

        var exitCode = await _downloadApplication.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, exitCode);
        await _webPageDownloader.DidNotReceiveWithAnyArgs().DownloadAsync(default!, TestContext.Current.CancellationToken);

        await _manifest.DidNotReceiveWithAnyArgs().WriteAsync(default!, TestContext.Current.CancellationToken);

        AssertLogged(_logger, LogLevel.Critical);
    }

    [Fact]
    public async Task NoValidUrls_ReturnsOne_AndDownloadsNothing()
    {
        var urls = Array.Empty<Uri>();

        _urlSource.ReadAsync(Arg.Any<CancellationToken>())
            .Returns(new UrlReadResult(urls, new List<string>()));

        var exitCode = await _downloadApplication.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, exitCode);
        
        await _webPageDownloader.DidNotReceiveWithAnyArgs().DownloadAsync(default!, TestContext.Current.CancellationToken);

        await _manifest.DidNotReceiveWithAnyArgs().WriteAsync(default!, TestContext.Current.CancellationToken);

        AssertLogged(_logger, LogLevel.Error);
    }

    [Fact]
    public async Task OnlyInvalidEntries_IsTreatedAsNoValidUrls()
    {
        var urls = Array.Empty<Uri>();
        var invalidEntries = new[] { "Line 1: nonsense" };

        _urlSource.ReadAsync(Arg.Any<CancellationToken>())
            .Returns(new UrlReadResult(urls, invalidEntries));

        var exitCode = await _downloadApplication.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, exitCode);
        await _webPageDownloader.DidNotReceiveWithAnyArgs().DownloadAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task EachInvalidEntry_IsLoggedAsAWarning_AndDoesNotChangeTheExitCode()
    {
        var urls = new[] { Url1 };
        var invalidEntries = new[] { "Line 2: nonsense", "Line 5: also bad" };

        var downloadedResults = new[] { Ok(Url1) };

        _urlSource.ReadAsync(Arg.Any<CancellationToken>())
            .Returns(new UrlReadResult(urls, invalidEntries));

         _webPageDownloader.DownloadAsync(urls, Arg.Any<CancellationToken>()).Returns(downloadedResults);

        var exitCode = await _downloadApplication.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        AssertLogged(_logger, LogLevel.Warning, times: 2);
    }
        

    [Fact]
    public async Task DownloaderReceivesExactlyTheUrlsFromTheSource()
    {
        var urls = new[] { Url1, Url2 };

        var downloadedResults = new[] { Ok(Url1), Ok(Url2) };

        _urlSource.ReadAsync(Arg.Any<CancellationToken>())
            .Returns(new UrlReadResult(urls, new List<string>()));

        _webPageDownloader.DownloadAsync(urls, Arg.Any<CancellationToken>()).Returns(downloadedResults);

        await _downloadApplication.RunAsync(TestContext.Current.CancellationToken);

        await _webPageDownloader.Received(1).DownloadAsync(
            Arg.Is<IEnumerable<Uri>>(urls => urls.SequenceEqual(urls)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ManifestReceivesTheDownloaderResults()
    {
        IReadOnlyList<DownloadResult> downloadedResults = new[] { Ok(Url1), Fail(Url2) };

        var urls = new[] { Url1, Url2 };

        _urlSource.ReadAsync(Arg.Any<CancellationToken>())
            .Returns(new UrlReadResult(urls, new List<string>()));

        _webPageDownloader.DownloadAsync(urls, Arg.Any<CancellationToken>()).Returns(downloadedResults);

        await _downloadApplication.RunAsync(TestContext.Current.CancellationToken);

        await _manifest.Received(1).WriteAsync(
            Arg.Is<IReadOnlyList<DownloadResult>>(r => ReferenceEquals(r, downloadedResults)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ManifestWriteFails_ReturnsOne_EvenIfAllDownloadsSucceeded()
    {
        var urls = new[] { Url1 };

        IReadOnlyList<DownloadResult> downloadedResults = new[] { Ok(Url1) };

        _urlSource.ReadAsync(Arg.Any<CancellationToken>())
           .Returns(new UrlReadResult(urls, new List<string>()));

        _webPageDownloader.DownloadAsync(urls, Arg.Any<CancellationToken>()).Returns(downloadedResults);

        _manifest.WriteAsync(Arg.Any<IReadOnlyList<DownloadResult>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException("disk full"));

        var exitCode = await _downloadApplication.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, exitCode);
        AssertLogged(_logger, LogLevel.Error);
    }
    
    [Fact]
    public async Task CancellationToken_IsPassedToAllCollaborators()
    {
        var urls = new[] { Url1 };

        IReadOnlyList<DownloadResult> downloadedResults = new[] { Ok(Url1) };

        using var cts = new CancellationTokenSource();
        
        _urlSource.ReadAsync(Arg.Any<CancellationToken>())
           .Returns(new UrlReadResult(urls, new List<string>()));

        _webPageDownloader.DownloadAsync(urls, Arg.Any<CancellationToken>()).Returns(downloadedResults);

        await _downloadApplication.RunAsync(cts.Token);

        await _urlSource.Received(1).ReadAsync(cts.Token);
        await _webPageDownloader.Received(1).DownloadAsync(Arg.Any<IEnumerable<Uri>>(), cts.Token);
        await _manifest.Received(1).WriteAsync(Arg.Any<IReadOnlyList<DownloadResult>>(), cts.Token);
    }
    
    private static DownloadResult Ok(Uri url) => new(url, true, 200, "file.html", 10, null);
    private static DownloadResult Fail(Uri url) => new(url, false, 500, null, null, "boom");

    private static void AssertLogged(ILogger logger, LogLevel level, int times = 1) =>
        logger.Received(times).Log(
            level,
            Arg.Any<EventId>(),
            Arg.Any<Arg.AnyType>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<Arg.AnyType, Exception?, string>>());        
}