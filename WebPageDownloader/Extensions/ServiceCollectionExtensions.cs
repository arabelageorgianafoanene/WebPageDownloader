using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using WebPageDownloader.Configuration;
using WebPageDownloader.Input;
using WebPageDownloader.Services;
using WebPageDownloader.Storage;

namespace WebPageDownloader.Extensions
{
    internal static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddDownloaderOptions(this IServiceCollection services, IConfiguration configuration)
        {
             services
                        .AddOptions<DownloaderOptions>()
                        .Bind(configuration.GetSection(DownloaderOptions.SectionName))
                        .ValidateDataAnnotations()
                        .ValidateOnStart();
            return services;
        }

        public static IServiceCollection AddWebPageDownloader(this IServiceCollection services)
        {
            services
                .AddHttpClient<IWebPageDownloader, Services.WebPageDownloader>((sp, client) =>
                {
                     var options = sp.GetRequiredService<IOptions<DownloaderOptions>>().Value;
                     client.Timeout = Timeout.InfiniteTimeSpan;
                     client.DefaultRequestHeaders.UserAgent.ParseAdd("WebPageDownloader/1.0");
                })
                 .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                 {
                     AutomaticDecompression = DecompressionMethods.All,
                     MaxAutomaticRedirections = 5,
                     PooledConnectionLifetime = TimeSpan.FromMinutes(5)
                 });

            services.AddSingleton<IPageStore, FileSystemStore>();

            services.AddSingleton<IManifestWriter, JsonManifestWriter>();

            return services;
        }

        public static IServiceCollection AddUrlSource(this IServiceCollection services, string inputPath)
        {
            services.AddSingleton<IUrlSource>(sp =>
                new FileUrlSource(
                inputPath,
                sp.GetRequiredService<ILogger<FileUrlSource>>()));

            return services;
        }
    }
}
