using System;
using System.Threading;
using Mixcloud.Core.Media;
using Mixcloud.Core.YtDlp;
using Xunit;

public class StreamMediaSourceTests
{
    private static StreamMediaSource Make(FakeProcessRunner r, TimeSpan life) =>
        new StreamMediaSource(new YtDlpService(r, @"C:\yt\yt-dlp.exe"), life);

    [Fact]
    public void RozwiazujeAdresStrony()
    {
        var r = new FakeProcessRunner { NextStdOut = "https://dl.mixcloud.stream/a.m4a?sig=1\n" };
        var url = Make(r, TimeSpan.FromMinutes(10))
            .GetPlayableUrl("https://www.mixcloud.com/a/b/", CancellationToken.None);
        Assert.Equal("https://dl.mixcloud.stream/a.m4a?sig=1", url);
    }

    [Fact]
    public void SwiezyWynikJestBranyZPamieciPodrecznej()
    {
        var r = new FakeProcessRunner { NextStdOut = "https://dl.mixcloud.stream/a.m4a?sig=1\n" };
        var src = Make(r, TimeSpan.FromMinutes(10));
        src.GetPlayableUrl("https://www.mixcloud.com/a/b/", CancellationToken.None);

        r.NextStdOut = "https://dl.mixcloud.stream/INNY.m4a?sig=2\n";
        var drugi = src.GetPlayableUrl("https://www.mixcloud.com/a/b/", CancellationToken.None);

        Assert.EndsWith("a.m4a?sig=1", drugi);
    }

    [Fact]
    public void WygasnietyWynikJestRozwiazywanyPonownie()
    {
        var r = new FakeProcessRunner { NextStdOut = "https://dl.mixcloud.stream/a.m4a?sig=1\n" };
        var src = Make(r, TimeSpan.Zero);
        src.GetPlayableUrl("https://www.mixcloud.com/a/b/", CancellationToken.None);

        r.NextStdOut = "https://dl.mixcloud.stream/b.m4a?sig=2\n";
        Assert.EndsWith("b.m4a?sig=2", src.GetPlayableUrl("https://www.mixcloud.com/a/b/", CancellationToken.None));
    }

    [Fact]
    public void BladRozwiazywaniaDajeNullZamiastWyjatku()
    {
        var r = new FakeProcessRunner { NextExitCode = 1, NextStdErr = "ERROR" };
        Assert.Null(Make(r, TimeSpan.FromMinutes(10))
            .GetPlayableUrl("https://www.mixcloud.com/a/b/", CancellationToken.None));
    }
}
