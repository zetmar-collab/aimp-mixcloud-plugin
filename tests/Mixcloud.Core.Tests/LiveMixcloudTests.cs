using System;
using System.Linq;
using System.Threading;
using Mixcloud.Core.Catalog;
using Mixcloud.Core.Process;
using Mixcloud.Core.Urls;
using Mixcloud.Core.YtDlp;
using Xunit;

public class LiveMixcloudTests
{
    // Uruchom: MIXCLOUD_LIVE=1 dotnet test --filter LiveMixcloudTests
    private const string Gate = "MIXCLOUD_LIVE";

    private static YtDlpService Service()
    {
        var exe = Environment.GetEnvironmentVariable("YTDLP_PATH") ?? "yt-dlp.exe";
        return new YtDlpService(new ProcessRunner(), exe);
    }

    [SkippableFact]
    public void UlubioneZwracajaPozycjeIMajaNazweListy()
    {
        Skip.If(Environment.GetEnvironmentVariable(Gate) != "1", "Test sieciowy wylaczony.");

        var url = MixcloudUrl.Parse("https://www.mixcloud.com/spartacus/favorites/");
        var listing = MixcloudCatalog.ParseFlatListing(
            Service().DumpListing(url, 5, CancellationToken.None));

        Assert.NotEmpty(listing.Tracks);
        Assert.False(string.IsNullOrWhiteSpace(listing.Name));
        Assert.All(listing.Tracks, t => Assert.StartsWith("https://www.mixcloud.com/", t.PageUrl));
    }

    [SkippableFact]
    public void RozwiazanyAdresJestBezposrednimStrumieniem()
    {
        Skip.If(Environment.GetEnvironmentVariable(Gate) != "1", "Test sieciowy wylaczony.");

        var url = MixcloudUrl.Parse("https://www.mixcloud.com/spartacus/favorites/");
        var first = MixcloudCatalog.ParseFlatListing(
            Service().DumpListing(url, 1, CancellationToken.None)).Tracks.First();

        var direct = Service().ResolveDirectUrl(MixcloudUrl.Parse(first.PageUrl), CancellationToken.None);

        Assert.NotNull(direct);
        Assert.StartsWith("http", direct);
        Assert.DoesNotContain("mixcloud.com/", direct);
    }
}
