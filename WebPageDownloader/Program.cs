using WebPageDownloader.Configuration;
using WebPageDownloader.Services;
using WebPageDownloader.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

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

builder.Services.AddSingleton<IPageStore, FileSystemPageStore>();

using var host = builder.Build();

