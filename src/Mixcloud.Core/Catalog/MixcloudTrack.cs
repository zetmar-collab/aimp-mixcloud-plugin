namespace Mixcloud.Core.Catalog
{
    public sealed class MixcloudTrack
    {
        public string PageUrl { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public double DurationSeconds { get; set; }
        public string ThumbnailUrl { get; set; } = string.Empty;
    }
}
