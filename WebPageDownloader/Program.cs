using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using WebPageDownloader.Application;
using WebPageDownloader.Configuration;
using WebPageDownloader.Input;
using WebPageDownloader.Services;
using WebPageDownloader.Storage;

const string Usage = "Usage: WebPageDownloader <urls-file> [--Downloader:MaxConcurrency=20] [--Downloader:OutputDirectory=output]";

var inputPath = args.Length > 0 && !args[0].StartsWith('-') ? args[0] : null;

if (inputPath is null)
{
    Console.WriteLine("Missing argument: path of the file with URLs.");
    Console.WriteLine(Usage);
    return 1;
}

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddOptions<DownloaderOptions>()
    .Bind(builder.Configuration.GetSection(DownloaderOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddHttpClient<IWebPageDownloader, WebPageDownloader.Services.WebPageDownloader>((sp, client) =>
    {
        var options = sp.GetRequiredService<IOptions<DownloaderOptions>>().Value;
        client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("WebPageDownloader/1.0");
    })
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.All,
        MaxAutomaticRedirections = 5,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    });

builder.Services.AddSingleton<IPageStore, FileSystemStore>();

builder.Services.AddSingleton<IManifestWriter, JsonManifestWriter>();

builder.Services.AddTransient<DownloadApplication>();

using var host = builder.Build();

var logger = host.Services.GetRequiredService<ILogger<Program>>();



if (!File.Exists(inputPath))
{
    logger.LogError("File not found: {FilePath}", Path.GetFullPath(inputPath));
    return 1;
}

builder.Services.AddSingleton<IUrlSource>(sp =>
    new FileUrlSource(
        inputPath,
        sp.GetRequiredService<ILogger<FileUrlSource>>()));

try
{
    await host.StartAsync();   
}
catch (OptionsValidationException ex)
{
    logger.LogError("Invalid configuration:");
    foreach (var failure in ex.Failures)
    {
        logger.LogError("  - {Failure}", failure);
    }
    return 1;
}

var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
var application = host.Services.GetRequiredService<DownloadApplication>();

try
{
    var exitCode = await application.RunAsync(lifetime.ApplicationStopping);
    await host.StopAsync();
    return exitCode;
}
catch (OperationCanceledException)
{
    logger.LogWarning("Download cancelled by user.");
    return 130;
}
catch (Exception ex)
{
    logger.LogCritical(ex, "Unhandled error.");
    return 1;
}

