using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebPageDownloader.Application;
using WebPageDownloader.Configuration;
using WebPageDownloader.Input;
using WebPageDownloader.Services;
using WebPageDownloader.Storage;

var builder = Host.CreateApplicationBuilder(args);

using var host = builder.Build();

var logger = host.Services.GetRequiredService<ILogger<Program>>();

builder.Services
    .AddOptions<DownloaderOptions>()
    .Bind(builder.Configuration.GetSection(DownloaderOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddHttpClient<IWebPageDownloader, WebPageDownloader.Services.WebPageDownloader>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<DownloaderOptions>>().Value;
    client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
});

const string Usage = "Usage: WebPageDownloader <urls-file> [--Downloader:MaxConcurrency=20] [--Downloader:OutputDirectory=output]";

var inputPath = args.FirstOrDefault(a => !a.StartsWith('-'));

if (inputPath is null)
{
    logger.LogError("Missing argument: path of the file with URLs.");
    logger.LogError(Usage);
    return 1;
}

if (!File.Exists(inputPath))
{
    logger.LogError("File not found: {FilePath}", Path.GetFullPath(inputPath));
    return 1;
}

builder.Services.AddSingleton<IPageStore, FileSystemPageStore>();

builder.Services.AddSingleton<IUrlSource>(sp =>
    new FileUrlSource(
        inputPath,
        sp.GetRequiredService<ILogger<FileUrlSource>>()));

builder.Services.AddTransient<DownloadApplication>();

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

var exitCode = await application.RunAsync(lifetime.ApplicationStopping);

await host.StopAsync();
return exitCode;

