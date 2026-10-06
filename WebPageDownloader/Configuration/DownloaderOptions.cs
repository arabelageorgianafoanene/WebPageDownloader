using System.ComponentModel.DataAnnotations;

namespace WebPageDownloader.Configuration
{
    public sealed record DownloaderOptions
    {
        public const string SectionName = "Downloader";

        [Range(1, 200)]
        public int MaxConcurrency { get; init; } = 10;

        [Range(1, 300)]
        public int RequestTimeoutSeconds { get; init; } = 30;

        [Required]
        public string OutputDirectory { get; init; } = "output";
    }
}
