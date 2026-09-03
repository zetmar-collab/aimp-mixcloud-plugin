using System;
using System.IO;
using System.Net;
using System.Threading;
using Mixcloud.Core.Settings;
using Newtonsoft.Json.Linq;

namespace Mixcloud.Core.YtDlp
{
    public interface IHttpDownloader
    {
        string GetString(string url, CancellationToken ct);
        void DownloadFile(string url, string destPath, CancellationToken ct);
    }

    public sealed class HttpDownloader : IHttpDownloader
    {
        private const string UserAgent = "AIMP-Mixcloud-Plugin";

        static HttpDownloader()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        }

        private static WebClient Create()
        {
            var wc = new WebClient();
            wc.Headers.Add("User-Agent", UserAgent);
            return wc;
        }

        public string GetString(string url, CancellationToken ct)
        {
            using (var wc = Create()) return wc.DownloadString(url);
        }

        public void DownloadFile(string url, string destPath, CancellationToken ct)
        {
            using (var wc = Create()) wc.DownloadFile(url, destPath);
        }
    }

    public sealed class YtDlpInstaller
    {
        private const string LatestReleaseApi =
            "https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest";
        private const string LatestExeUrl =
            "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";

        private readonly IHttpDownloader _http;
        private readonly string _dataDir;

        public YtDlpInstaller(IHttpDownloader http, string dataDir)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _dataDir = dataDir ?? throw new ArgumentNullException(nameof(dataDir));
        }

        // Minimalny rozmiar pliku, ponizej ktorego nie traktujemy go jako
        // wiarygodnej binarki yt-dlp.exe (chroni przed obcietym/pustym pobraniem).
        private const long MinPlausibleExeBytes = 1024;

        public string ExePath => Path.Combine(_dataDir, "yt-dlp.exe");
        private string PendingPath => ExePath + ".new";
        private string BackupPath => ExePath + ".bak";

        public string EnsureInstalled(CancellationToken ct)
        {
            Directory.CreateDirectory(_dataDir);
            if (!File.Exists(ExePath))
                _http.DownloadFile(LatestExeUrl, ExePath, ct);
            return ExePath;
        }

        public void ApplyPendingUpdate()
        {
            if (!File.Exists(PendingPath)) return;

            if (!IsPlausibleExecutable(PendingPath))
            {
                // Obcieta/pusta binarka - nie wolno jej nigdy podmienic na dzialajaca wersje.
                TryDelete(PendingPath);
                return;
            }

            try
            {
                TryDelete(BackupPath);

                bool hadExisting = File.Exists(ExePath);
                if (hadExisting)
                    File.Move(ExePath, BackupPath);

                try
                {
                    File.Move(PendingPath, ExePath);
                }
                catch
                {
                    // Podmiana sie nie powiodla - przywroc poprzednia dzialajaca binarke,
                    // zeby plugin nigdy nie zostal bez zadnej wersji yt-dlp.
                    if (hadExisting && !File.Exists(ExePath) && File.Exists(BackupPath))
                        File.Move(BackupPath, ExePath);
                    throw;
                }

                if (hadExisting) TryDelete(BackupPath);
            }
            catch (Exception)
            {
                // Binarka wciaz zablokowana (IOException) albo brak uprawnien
                // (np. antywirus trzymajacy swiezo pobrany .exe -
                // UnauthorizedAccessException nie dziedziczy po IOException) -
                // sprobujemy przy nastepnym starcie. Ta metoda nie moze nigdy
                // wyrzucic wyjatku, bo Initialize() plugin wyzej go przepuszcza.
            }
        }

        public bool CheckForUpdate(MixcloudSettings settings, CancellationToken ct)
        {
            try
            {
                var tag = (string)JObject.Parse(_http.GetString(LatestReleaseApi, ct))["tag_name"];
                settings.LastUpdateCheckUtc = DateTime.UtcNow;

                if (string.IsNullOrWhiteSpace(tag)) return false;
                if (string.Equals(tag, settings.LastKnownYtDlpTag, StringComparison.Ordinal)) return false;

                Directory.CreateDirectory(_dataDir);
                try
                {
                    // Pobieramy obok. Podmiana nastapi dopiero przy nastepnym starcie.
                    _http.DownloadFile(LatestExeUrl, PendingPath, ct);
                }
                catch
                {
                    // Przerwane/nieudane pobranie moglo zostawic obciety plik - usun go,
                    // zeby ApplyPendingUpdate nigdy go nie zobaczyl.
                    TryDelete(PendingPath);
                    throw;
                }

                settings.LastKnownYtDlpTag = tag;
                return true;
            }
            catch (Exception)
            {
                // Brak sieci to cichy no-op: gramy dalej na dotychczasowej wersji.
                return false;
            }
        }

        private static bool IsPlausibleExecutable(string path)
        {
            try
            {
                var info = new FileInfo(path);
                return info.Exists && info.Length >= MinPlausibleExeBytes;
            }
            catch (IOException)
            {
                return false;
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException)
            {
                // Najlepszy wysilek - jesli plik jest zablokowany, zostanie posprzatany pozniej.
            }
        }
    }
}
