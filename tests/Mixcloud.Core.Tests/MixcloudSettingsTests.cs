using System;
using System.IO;
using Mixcloud.Core.Settings;
using Xunit;

public class MixcloudSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mcset-" + Guid.NewGuid().ToString("N"));
    private string Path_ => Path.Combine(_dir, "settings.json");

    public MixcloudSettingsTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void BrakPlikuDajeWartosciDomyslne()
    {
        var s = MixcloudSettings.Load(Path_);
        Assert.Equal(string.Empty, s.Handle);
        Assert.Equal(MixcloudSettings.DefaultListingLimit, s.ListingLimit);
        Assert.True(s.AutoUpdateYtDlp);
        Assert.Equal(MixcloudSettings.DefaultCacheLimitBytes, s.CacheLimitBytes);
    }

    [Fact]
    public void ZapisIOdczytZachowujaWartosci()
    {
        var s = MixcloudSettings.Load(Path_);
        s.Handle = "spartacus";
        s.ListingLimit = 42;
        s.AutoUpdateYtDlp = false;
        s.LastKnownYtDlpTag = "2026.06.09";
        s.Save(Path_);

        var back = MixcloudSettings.Load(Path_);
        Assert.Equal("spartacus", back.Handle);
        Assert.Equal(42, back.ListingLimit);
        Assert.False(back.AutoUpdateYtDlp);
        Assert.Equal("2026.06.09", back.LastKnownYtDlpTag);
    }

    [Fact]
    public void UszkodzonyPlikDajeWartosciDomyslneZamiastWyjatku()
    {
        File.WriteAllText(Path_, "{ to nie jest json");
        var s = MixcloudSettings.Load(Path_);
        Assert.Equal(MixcloudSettings.DefaultListingLimit, s.ListingLimit);
    }

    [Fact]
    public void ZapisTworzyBrakujacyKatalog()
    {
        var nested = Path.Combine(_dir, "a", "b", "settings.json");
        new MixcloudSettings { Handle = "x" }.Save(nested);
        Assert.True(File.Exists(nested));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void NiedodatniLimitJestKorygowanyDoDomyslnego(int zly)
    {
        var s = new MixcloudSettings { ListingLimit = zly };
        s.Save(Path_);
        Assert.Equal(MixcloudSettings.DefaultListingLimit, MixcloudSettings.Load(Path_).ListingLimit);
    }
}
