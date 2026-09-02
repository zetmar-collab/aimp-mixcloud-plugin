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

        public string ExePath => Path.Combine(_dataDir, "yt-dlp.exe");
        private string PendingPath => ExePath + ".new";

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
            try
            {
                if (File.Exists(ExePath)) File.Delete(ExePath);
                File.Move(PendingPath, ExePath);
            }
            catch (IOException)
            {
                // Binarka wciaz zablokowana - sprobujemy przy nastepnym starcie.
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
                // Pobieramy obok. Podmiana nastapi dopiero przy nastepnym starcie.
                _http.DownloadFile(LatestExeUrl, PendingPath, ct);
                settings.LastKnownYtDlpTag = tag;
                return true;
            }
            catch (Exception)
            {
                // Brak sieci to cichy no-op: gramy dalej na dotychczasowej wersji.
                return false;
            }
        }
    }
}
