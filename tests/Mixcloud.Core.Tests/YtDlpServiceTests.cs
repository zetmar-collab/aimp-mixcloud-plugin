using System;
using System.Linq;
using System.Threading;
using Mixcloud.Core.Urls;
using Mixcloud.Core.YtDlp;
using Xunit;

public class YtDlpServiceTests
{
    private static YtDlpService Make(FakeProcessRunner r) => new YtDlpService(r, @"C:\yt\yt-dlp.exe");

    [Fact]
    public void ListaUzywaLeniwegoTrybuZLimitem()
    {
        var r = new FakeProcessRunner { NextStdOut = "{\"a\":1}\n{\"a\":2}\n" };
        var lines = Make(r).DumpListing(
            MixcloudUrl.Parse("https://www.mixcloud.com/spartacus/favorites/"), 50, CancellationToken.None);

        Assert.Equal(2, lines.Count);
        Assert.Contains("--flat-playlist", r.LastArguments);
        Assert.Contains("--dump-json", r.LastArguments);
        Assert.Contains("-I 1:50", r.LastArguments);
        // --dump-single-json zawiesza sie na duzych profilach - nie wolno go tu uzyc.
        Assert.DoesNotContain("--dump-single-json", r.LastArguments);
    }

    [Fact]
    public void PomijaPusteIniepoprawneLinie()
    {
        var r = new FakeProcessRunner { NextStdOut = "{\"a\":1}\n\n   \n{\"a\":2}\n" };
        var lines = Make(r).DumpListing(
            MixcloudUrl.Parse("https://www.mixcloud.com/spartacus/favorites/"), 10, CancellationToken.None);
        Assert.Equal(2, lines.Count);
    }

    [Fact]
    public void PojedynczyMiksUzywaDumpSingleJson()
    {
        var r = new FakeProcessRunner { NextStdOut = "{\"title\":\"x\"}" };
        var json = Make(r).DumpCloudcast(
            MixcloudUrl.Parse("https://www.mixcloud.com/sub88/mental-place-26/"), CancellationToken.None);

        Assert.Equal("{\"title\":\"x\"}", json.Trim());
        Assert.Contains("--dump-single-json", r.LastArguments);
    }

    [Fact]
    public void RozwiazywanieAdresuUzywaWlasciwegoSelektoraFormatu()
    {
        var r = new FakeProcessRunner { NextStdOut = "https://dl.mixcloud.stream/x.m4a?sig=abc\n" };
        var direct = Make(r).ResolveDirectUrl(
            MixcloudUrl.Parse("https://www.mixcloud.com/sub88/mental-place-26/"), CancellationToken.None);

        Assert.Equal("https://dl.mixcloud.stream/x.m4a?sig=abc", direct);
        Assert.Contains("-f \"http/hls-192/bestaudio\"", r.LastArguments);
        Assert.Contains("-g", r.LastArguments);
    }

    [Fact]
    public void RozwiazywanieBierzePierwszyAdresGdyYtDlpZwrocaKilka()
    {
        var r = new FakeProcessRunner { NextStdOut = "https://a/1.m4a\nhttps://a/2.m4a\n" };
        Assert.Equal("https://a/1.m4a", Make(r).ResolveDirectUrl(
            MixcloudUrl.Parse("https://www.mixcloud.com/a/b/"), CancellationToken.None));
    }

    [Fact]
    public void TimeoutJestBledem()
    {
        var r = new FakeProcessRunner { NextTimedOut = true, NextExitCode = -1 };
        var ex = Assert.Throws<YtDlpException>(() => Make(r).DumpListing(
            MixcloudUrl.Parse("https://www.mixcloud.com/spartacus/favorites/"), 10, CancellationToken.None));
        Assert.Contains("timeout", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NiezerowyKodWyjsciaJestBledemZeStandardowymBledem()
    {
        var r = new FakeProcessRunner { NextExitCode = 1, NextStdErr = "ERROR: nie znaleziono" };
        var ex = Assert.Throws<YtDlpException>(() => Make(r).DumpCloudcast(
            MixcloudUrl.Parse("https://www.mixcloud.com/a/b/"), CancellationToken.None));
        Assert.Contains("nie znaleziono", ex.StdErr);
    }

    [Fact]
    public void NiepoprawnyAdresJestOdrzucanyPrzedUruchomieniemProcesu()
    {
        var r = new FakeProcessRunner();
        Assert.Throws<ArgumentException>(() => Make(r).DumpListing(
            MixcloudUrl.Parse("https://youtube.com/x"), 10, CancellationToken.None));
        Assert.Null(r.LastArguments);
    }

    [Fact]
    public void KazdeWywolanieMaJawnyTimeout()
    {
        var r = new FakeProcessRunner { NextStdOut = "{}" };
        Make(r).DumpListing(MixcloudUrl.Parse("https://www.mixcloud.com/a/favorites/"), 5, CancellationToken.None);
        Assert.True(r.LastTimeout > TimeSpan.Zero);
    }

    [Fact]
    public void RozwiazywanieOdrzucaAdresInnyNizCloudcastPrzedUruchomieniemProcesu()
    {
        var r = new FakeProcessRunner();
        Assert.Throws<ArgumentException>(() => Make(r).ResolveDirectUrl(
            MixcloudUrl.Parse("https://www.mixcloud.com/spartacus/favorites/"), CancellationToken.None));
        Assert.Null(r.LastArguments);
    }

    [Fact]
    public void RozwiazywanieOdrzucaNiepoprawnyAdresPrzedUruchomieniemProcesu()
    {
        var r = new FakeProcessRunner();
        Assert.Throws<ArgumentException>(() => Make(r).ResolveDirectUrl(
            MixcloudUrl.Parse("https://youtube.com/x"), CancellationToken.None));
        Assert.Null(r.LastArguments);
    }

    [Fact]
    public void PusteWyjscieListyToPustaLista()
    {
        var r = new FakeProcessRunner { NextStdOut = "   \n  \n" };
        var lines = Make(r).DumpListing(
            MixcloudUrl.Parse("https://www.mixcloud.com/spartacus/favorites/"), 10, CancellationToken.None);
        Assert.Empty(lines);
    }

    [Fact]
    public void SamSzumBezJsonRzucaWyjatek()
    {
        var r = new FakeProcessRunner { NextStdOut = "WARNING: cos poszlo nie tak\nnot json at all\n", NextStdErr = "stderr tresc" };
        var ex = Assert.Throws<YtDlpException>(() => Make(r).DumpListing(
            MixcloudUrl.Parse("https://www.mixcloud.com/spartacus/favorites/"), 10, CancellationToken.None));
        Assert.Equal("stderr tresc", ex.StdErr);
    }

    [Fact]
    public void SzumPrzemieszanyZJsonemJestIgnorowany()
    {
        var r = new FakeProcessRunner { NextStdOut = "WARNING: cos\n{\"a\":1}\nnie json\n{\"a\":2}\n" };
        var lines = Make(r).DumpListing(
            MixcloudUrl.Parse("https://www.mixcloud.com/spartacus/favorites/"), 10, CancellationToken.None);
        Assert.Equal(2, lines.Count);
    }
}
