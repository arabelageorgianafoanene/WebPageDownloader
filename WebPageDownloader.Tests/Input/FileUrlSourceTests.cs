using Microsoft.Extensions.Logging.Abstractions;
using System.Text;
using WebPageDownloader.Input;

namespace WebPageDownloader.Tests;

public class FileUrlSourceTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "wpd-urlsource-" + Guid.NewGuid().ToString("N"));

    public FileUrlSourceTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public async Task ValidUrls_AreReturnedInFileOrder()
    {
        var result = await ReadAsync("https://a.test/1\nhttp://b.test/2\nhttps://c.test/3");

        Assert.Equal(
            new[] { new Uri("https://a.test/1"), new Uri("http://b.test/2"), new Uri("https://c.test/3") },
            result.Urls);
        Assert.Empty(result.InvalidEntries);
    }

    [Fact]
    public async Task EmptyFile_ReturnsNothing()
    {
        var result = await ReadAsync("");

        Assert.Empty(result.Urls);
        Assert.Empty(result.InvalidEntries);
    }

    [Fact]
    public async Task SurroundingWhitespace_IsTrimmed()
    {
        var result = await ReadAsync("   https://a.test/x  \t");

        Assert.Equal(new[] { new Uri("https://a.test/x") }, result.Urls);
        Assert.Empty(result.InvalidEntries);
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("example.com")]               // no scheme
    [InlineData("ftp://a.test/file")]         // wrong scheme
    [InlineData("file:///c:/temp/x.html")]    // wrong scheme
    [InlineData("mailto:someone@a.test")]     // wrong scheme
    [InlineData("javascript:alert(1)")]       // wrong scheme
    [InlineData("http://")]                   // no host
    [InlineData("http://exa mple.com")]       // space in host
    public async Task InvalidLine_IsReported_AndNotReturnedAsUrl(string line)
    {
        var result = await ReadAsync(line);

        Assert.Empty(result.Urls);
        Assert.Equal(new[] { $"Line 1: {line}" }, result.InvalidEntries);
    }

    [Fact]
    public async Task InvalidLine_ReportsOriginalLineNumber_CountingBlankAndCommentLines()
    {
        var result = await ReadAsync(
            "# comment\n" +      
            "\n" +              
            "https://a.test/1\n" + 
            "nonsense");        

        Assert.Single(result.Urls);
        Assert.Equal(new[] { "Line 4: nonsense" }, result.InvalidEntries);
    }

    [Fact]
    public async Task InvalidLines_DoNotStopValidOnesAroundThem()
    {
        var result = await ReadAsync("https://a.test/1\nbad\nhttps://a.test/2");

        Assert.Equal(2, result.Urls.Count);
        Assert.Single(result.InvalidEntries);
    }

    
    [Fact]
    public async Task ExactDuplicates_AreReturnedOnce_KeepingFirstPosition()
    {
        var result = await ReadAsync("https://a.test/1\nhttps://b.test/2\nhttps://a.test/1");

        Assert.Equal(
            new[] { new Uri("https://a.test/1"), new Uri("https://b.test/2") },
            result.Urls);
        Assert.Empty(result.InvalidEntries); 
    }
       
    [Fact]
    public async Task MissingFile_Throws()
    {
        var source = new FileUrlSource(Path.Combine(_dir, "does-not-exist.txt"), NullLogger<FileUrlSource>.Instance);

        await Assert.ThrowsAsync<FileNotFoundException>(() => source.ReadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CancelledToken_Throws()
    {
        var source = new FileUrlSource(WriteFile("https://a.test/1\nhttps://a.test/2"), NullLogger<FileUrlSource>.Instance);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.ReadAsync(cts.Token));
    }

    private string WriteFile(string content, Encoding? encoding = null)
    {
        var path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, content, encoding ?? new UTF8Encoding(false));
        return path;
    }

    private Task<UrlReadResult> ReadAsync(string content, Encoding? encoding = null)
    {
        var source = new FileUrlSource(WriteFile(content, encoding), NullLogger<FileUrlSource>.Instance);
        return source.ReadAsync();
    }
}
