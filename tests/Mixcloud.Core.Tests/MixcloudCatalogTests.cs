using System;
using System.IO;
using System.Linq;
using Mixcloud.Core.Catalog;
using Xunit;

public class MixcloudCatalogTests
{
    private static string Fixture(string name) =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fixtures", name);

    [Fact]
    public void ParsujeListeUlubionychZPrawdziwejOdpowiedzi()
    {
        var lines = File.ReadAllLines(Fixture("favorites-flat.jsonl"));
        var listing = MixcloudCatalog.ParseFlatListing(lines);

        Assert.Equal("Spartacus (favorites)", listing.Name);
        Assert.Equal(2, listing.Tracks.Count);

        var first = listing.Tracks[0];
        Assert.Equal("https://www.mixcloud.com/sub88/mental-place-26/", first.PageUrl);
        Assert.Equal("Mental Place 26", first.Title);
        Assert.Equal("sub88", first.Artist);
        Assert.Equal(0d, first.DurationSeconds);
    }

    [Fact]
    public void WykonawcaPochodziZeSciezkiAdresuNieZPrefiksuId()
    {
        // Handle uzytkownika moze zawierac podkreslenie, wiec dzielenie id
        // po pierwszym '_' bylo by bledne. Zrodlem prawdy jest sciezka adresu.
        var line = "{\"_type\":\"url\",\"id\":\"a_b_c\"," +
                   "\"url\":\"https://www.mixcloud.com/a_b/c/\"," +
                   "\"playlist_title\":\"X\"}";
        var listing = MixcloudCatalog.ParseFlatListing(new[] { line });
        Assert.Equal("a_b", listing.Tracks[0].Artist);
        Assert.Equal("C", listing.Tracks[0].Title);
    }

    [Fact]
    public void ParsujePojedynczyMiksZPrawdziwejOdpowiedzi()
    {
        var track = MixcloudCatalog.ParseCloudcast(File.ReadAllText(Fixture("cloudcast-single.json")));

        Assert.Equal("Loraine James - 1st September 2026", track.Title);
        Assert.Equal("NTSRadio", track.Artist);
        Assert.Equal(3949d, track.DurationSeconds);
        Assert.Equal("https://www.mixcloud.com/NTSRadio/loraine-james-1st-september-2026/", track.PageUrl);
        Assert.StartsWith("https://thumbnailer.mixcloud.com/", track.ThumbnailUrl);
    }

    [Fact]
    public void PustaListaDajePustyWynikBezWyjatku()
    {
        var listing = MixcloudCatalog.ParseFlatListing(Enumerable.Empty<string>());
        Assert.Empty(listing.Tracks);
        Assert.Equal(string.Empty, listing.Name);
    }

    [Fact]
    public void PomijaUszkodzoneLinieZamiastPrzerywacCaleParsowanie()
    {
        var good = "{\"_type\":\"url\",\"url\":\"https://www.mixcloud.com/a/b/\",\"playlist_title\":\"X\"}";
        var listing = MixcloudCatalog.ParseFlatListing(new[] { "to nie json", good, "{ nadgryziony" });
        Assert.Single(listing.Tracks);
        Assert.Equal("X", listing.Name);
    }

    [Fact]
    public void PomijaPozycjeBezAdresu()
    {
        var listing = MixcloudCatalog.ParseFlatListing(new[] { "{\"_type\":\"url\",\"playlist_title\":\"X\"}" });
        Assert.Empty(listing.Tracks);
    }
}
