using Mixcloud.Core.Urls;
using Xunit;

public class MixcloudUrlTests
{
    [Theory]
    [InlineData("https://www.mixcloud.com/sub88/mental-place-26/")]
    [InlineData("https://mixcloud.com/sub88/mental-place-26")]
    [InlineData("  https://www.mixcloud.com/sub88/mental-place-26/  ")]
    public void RozpoznajePojedynczyMiks(string raw)
    {
        var u = MixcloudUrl.Parse(raw);
        Assert.Equal(MixcloudUrlKind.Cloudcast, u.Kind);
        Assert.Equal("sub88", u.UserSlug);
        Assert.Equal("mental-place-26", u.CloudcastSlug);
        Assert.Equal("https://www.mixcloud.com/sub88/mental-place-26/", u.Normalized);
    }

    [Theory]
    [InlineData("https://www.mixcloud.com/spartacus/favorites/")]
    [InlineData("https://www.mixcloud.com/spartacus/uploads/")]
    [InlineData("https://www.mixcloud.com/spartacus/listens/")]
    [InlineData("https://www.mixcloud.com/spartacus/stream/")]
    [InlineData("https://www.mixcloud.com/spartacus/")]
    public void RozpoznajeListy(string raw)
    {
        var u = MixcloudUrl.Parse(raw);
        Assert.Equal(MixcloudUrlKind.Listing, u.Kind);
        Assert.Equal("spartacus", u.UserSlug);
        Assert.Null(u.CloudcastSlug);
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=abc")]
    [InlineData("https://mixcloud.com.evil.example/sub88/mix/")]
    [InlineData("nie-adres")]
    [InlineData("")]
    [InlineData(null)]
    public void OdrzucaObceIZepsuteAdresy(string raw)
    {
        Assert.Equal(MixcloudUrlKind.Invalid, MixcloudUrl.Parse(raw).Kind);
    }

    [Fact]
    public void BudujeAdresUlubionychZHandle()
    {
        var u = MixcloudUrl.ForFavorites("spartacus");
        Assert.Equal(MixcloudUrlKind.Listing, u.Kind);
        Assert.Equal("https://www.mixcloud.com/spartacus/favorites/", u.Normalized);
    }

    [Theory]
    [InlineData("spartacus", "spartacus")]
    [InlineData("  spartacus  ", "spartacus")]
    [InlineData("@spartacus", "spartacus")]
    [InlineData("https://www.mixcloud.com/spartacus/", "spartacus")]
    [InlineData("https://mixcloud.com/spartacus", "spartacus")]
    [InlineData("https://www.mixcloud.com/spartacus/favorites/", "spartacus")]
    [InlineData("www.mixcloud.com/spartacus/", "spartacus")]
    [InlineData("mixcloud.com/spartacus", "spartacus")]
    [InlineData("  https://www.mixcloud.com/spartacus/  ", "spartacus")]
    public void NormalizeHandleWyciagaSamaNazweZRoznychFormatow(string input, string expected)
    {
        Assert.Equal(expected, MixcloudUrl.NormalizeHandle(input));
    }

    [Theory]
    [InlineData("https://www.youtube.com/spartacus/")]
    [InlineData("https://mixcloud.com.evil.example/spartacus/")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void NormalizeHandleOdrzucaObceAdresyIPusteWartosci(string input)
    {
        Assert.Equal(string.Empty, MixcloudUrl.NormalizeHandle(input));
    }
}
