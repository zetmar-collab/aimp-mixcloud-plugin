using System;
using System.IO;
using System.Threading;
using Mixcloud.Core.Settings;
using Mixcloud.Core.YtDlp;
using Xunit;

public sealed class FakeDownloader : IHttpDownloader
{
    public string NextString { get; set; } = "{}";
    public string FileContent { get; set; } = "UDAWANY-EXE";
    public int DownloadCount { get; private set; }
    public string LastFileUrl { get; private set; }
    public Exception ThrowOnDownload { get; set; }

    public string GetString(string url, CancellationToken ct) => NextString;

    public void DownloadFile(string url, string destPath, CancellationToken ct)
    {
        if (ThrowOnDownload != null) throw ThrowOnDownload;
        LastFileUrl = url;
        DownloadCount++;
        File.WriteAllText(destPath, FileContent);
    }
}

public class YtDlpInstallerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mcinst-" + Guid.NewGuid().ToString("N"));
    public YtDlpInstallerTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private string Exe => Path.Combine(_dir, "yt-dlp.exe");

    [Fact]
    public void PierwszeUruchomieniePobieraBinarke()
    {
        var http = new FakeDownloader();
        var path = new YtDlpInstaller(http, _dir).EnsureInstalled(CancellationToken.None);

        Assert.Equal(Exe, path);
        Assert.True(File.Exists(Exe));
        Assert.Equal(1, http.DownloadCount);
    }

    [Fact]
    public void IstniejacaBinarkaNieJestPobieranaPonownie()
    {
        File.WriteAllText(Exe, "juz-jest");
        var http = new FakeDownloader();
        new YtDlpInstaller(http, _dir).EnsureInstalled(CancellationToken.None);
        Assert.Equal(0, http.DownloadCount);
    }

    [Fact]
    public void NowaWersjaLadujeObokJakoOczekujacaAktualizacja()
    {
        File.WriteAllText(Exe, "stara");
        var http = new FakeDownloader
        {
            NextString = "{\"tag_name\":\"2026.09.01\"}",
            FileContent = "nowa"
        };
        var settings = new MixcloudSettings { LastKnownYtDlpTag = "2026.06.09" };

        var updated = new YtDlpInstaller(http, _dir).CheckForUpdate(settings, CancellationToken.None);

        Assert.True(updated);
        // Dzialajaca binarka nie moze zostac podmieniona w locie.
        Assert.Equal("stara", File.ReadAllText(Exe));
        Assert.True(File.Exists(Exe + ".new"));
        Assert.Equal("2026.09.01", settings.LastKnownYtDlpTag);
    }

    [Fact]
    public void TaSamaWersjaNiePobieraNiczego()
    {
        File.WriteAllText(Exe, "stara");
        var http = new FakeDownloader { NextString = "{\"tag_name\":\"2026.06.09\"}" };
        var settings = new MixcloudSettings { LastKnownYtDlpTag = "2026.06.09" };

        Assert.False(new YtDlpInstaller(http, _dir).CheckForUpdate(settings, CancellationToken.None));
        Assert.Equal(0, http.DownloadCount);
    }

    [Fact]
    public void OczekujacaAktualizacjaJestStosowanaPrzyStarcie()
    {
        File.WriteAllText(Exe, "stara");
        File.WriteAllText(Exe + ".new", "nowa");

        new YtDlpInstaller(new FakeDownloader(), _dir).ApplyPendingUpdate();

        Assert.Equal("nowa", File.ReadAllText(Exe));
        Assert.False(File.Exists(Exe + ".new"));
    }

    [Fact]
    public void BladSieciPrzySprawdzaniuNiePrzerywaDzialania()
    {
        File.WriteAllText(Exe, "stara");
        var http = new FakeDownloader
        {
            NextString = "{\"tag_name\":\"2026.09.01\"}",
            ThrowOnDownload = new InvalidOperationException("brak sieci")
        };
        var settings = new MixcloudSettings { LastKnownYtDlpTag = "2026.06.09" };

        // Awaria aktualizacji nie moze psuc odtwarzania.
        Assert.False(new YtDlpInstaller(http, _dir).CheckForUpdate(settings, CancellationToken.None));
        Assert.Equal("stara", File.ReadAllText(Exe));
    }
}
