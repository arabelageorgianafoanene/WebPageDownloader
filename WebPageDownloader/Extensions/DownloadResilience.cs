using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using Polly.Timeout;
using System.Net;
using WebPageDownloader.Configuration;


namespace WebPageDownloader.Extensions
{
    internal static class DownloadResilience
    {
        public const string PipelineName = "downloadAndSave";
        public static IServiceCollection AddDownloadResilience(this IServiceCollection services)
        {
            services.AddResiliencePipeline(PipelineName, (pipeline, context) =>
            {
                var sp = context.ServiceProvider;
                
                var logger = context.ServiceProvider
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("DownloadRetry");

                var timeout = TimeSpan.FromSeconds(
               sp.GetRequiredService<IOptions<DownloaderOptions>>().Value.RequestTimeoutSeconds);

                pipeline
                    .AddRetry(new RetryStrategyOptions
                    {
                        MaxRetryAttempts = 3,
                        BackoffType = DelayBackoffType.Exponential,
                        UseJitter = true,
                        Delay = TimeSpan.FromSeconds(1),
                        ShouldHandle = new PredicateBuilder()
                            .Handle<HttpRequestException>(ex => IsTransient(ex.StatusCode))
                            .Handle<IOException>()
                            .Handle<TimeoutRejectedException>(),
                        OnRetry = args =>
                        {
                            logger.LogWarning(args.Outcome.Exception,
                                "Attempt {Attempt} failed, retrying in {Delay}",
                                args.AttemptNumber + 1, args.RetryDelay);
                            return default;
                        }
                    })
                    .AddTimeout(timeout);
            });
            return services;
        }

        static bool IsTransient(HttpStatusCode? s) => s is null or HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)s >= 500;
    }
}
