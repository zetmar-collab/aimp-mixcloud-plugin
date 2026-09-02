using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Mixcloud.Core.Process;
using Mixcloud.Core.Urls;

namespace Mixcloud.Core.YtDlp
{
    public sealed class YtDlpException : Exception
    {
        public string StdErr { get; }
        public YtDlpException(string message, string stdErr) : base(message)
        {
            StdErr = stdErr ?? string.Empty;
        }
    }

    public sealed class YtDlpService
    {
        // Progresywny m4a jest przewijalny i gra natywnie przez bass_aac.
        public const string FormatSelector = "http/hls-192/bestaudio";

        public static readonly TimeSpan ListingTimeout = TimeSpan.FromSeconds(120);
        public static readonly TimeSpan ResolveTimeout = TimeSpan.FromSeconds(60);
        public static readonly TimeSpan VersionTimeout = TimeSpan.FromSeconds(20);

        private readonly IProcessRunner _runner;
        private readonly string _exePath;

        public YtDlpService(IProcessRunner runner, string exePath)
        {
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
            _exePath = exePath ?? throw new ArgumentNullException(nameof(exePath));
        }

        public IReadOnlyList<string> DumpListing(MixcloudUrl url, int limit, CancellationToken ct)
        {
            Require(url, MixcloudUrlKind.Listing);
            if (limit < 1) throw new ArgumentOutOfRangeException(nameof(limit));

            // Twardy limit jest obowiazkowy: bez niego yt-dlp stronicuje bez konca
            // na duzych profilach (zaobserwowane na /NTSRadio/uploads/).
            var args = string.Format(CultureInfo.InvariantCulture,
                "--flat-playlist --dump-json -I 1:{0} --no-warnings \"{1}\"",
                limit, url.Normalized);

            var res = Execute(args, ListingTimeout, ct);

            if (string.IsNullOrWhiteSpace(res.StdOut))
                return new List<string>();

            var jsonLines = res.StdOut
                .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.StartsWith("{", StringComparison.Ordinal))
                .ToList();

            if (jsonLines.Count == 0)
                throw new YtDlpException("yt-dlp: wyjscie nie zawiera poprawnego JSON (listing)", res.StdErr);

            return jsonLines;
        }

        public string DumpCloudcast(MixcloudUrl url, CancellationToken ct)
        {
            Require(url, MixcloudUrlKind.Cloudcast);
            var args = "--dump-single-json --no-warnings \"" + url.Normalized + "\"";
            return Execute(args, ListingTimeout, ct).StdOut;
        }

        public string ResolveDirectUrl(MixcloudUrl url, CancellationToken ct)
        {
            Require(url, MixcloudUrlKind.Cloudcast);

            var args = "-g -f \"" + FormatSelector + "\" --no-warnings \"" + url.Normalized + "\"";
            var res = Execute(args, ResolveTimeout, ct);

            return res.StdOut
                .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .FirstOrDefault(l => l.StartsWith("http", StringComparison.OrdinalIgnoreCase));
        }

        public string GetVersion(CancellationToken ct)
        {
            return Execute("--version", VersionTimeout, ct).StdOut.Trim();
        }

        private static void Require(MixcloudUrl url, MixcloudUrlKind expected)
        {
            if (url == null || url.Kind != expected)
                throw new ArgumentException("Adres nie jest typu " + expected, nameof(url));
        }

        private ProcessResult Execute(string args, TimeSpan timeout, CancellationToken ct)
        {
            var res = _runner.Run(_exePath, args, timeout, ct);

            if (res.TimedOut)
                throw new YtDlpException("yt-dlp: timeout po " + timeout, res.StdErr);
            if (res.ExitCode != 0)
                throw new YtDlpException("yt-dlp: kod wyjscia " + res.ExitCode, res.StdErr);

            return res;
        }
    }
}
