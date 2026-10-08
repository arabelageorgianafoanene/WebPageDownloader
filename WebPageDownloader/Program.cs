using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebPageDownloader.Application;
using WebPageDownloader.Extensions;

const string Usage = "Usage: WebPageDownloader <urls-file> [--Downloader:MaxConcurrency=20] [--Downloader:OutputDirectory=output]";

var inputPath = args.Length > 0 && !args[0].StartsWith('-') ? args[0] : null;
if (inputPath is null)
{
    Console.WriteLine("Missing argument: path of the file with URLs.");
    Console.WriteLine(Usage);
    return 1;
}

if (!File.Exists(inputPath))
{
    Console.Error.WriteLine($"File not found: {Path.GetFullPath(inputPath)}");
    return 1;
}

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddDownloaderOptions(builder.Configuration)
    .AddDownloadResilience()
    .AddWebPageDownloader()
    .AddUrlSource(inputPath)
    .AddTransient<DownloadApplication>();

using var host = builder.Build();
var logger = host.Services.GetRequiredService<ILogger<Program>>();

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