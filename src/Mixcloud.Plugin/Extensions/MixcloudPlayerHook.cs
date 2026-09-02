using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using AIMP.SDK.Player.Extensions;

namespace Mixcloud.Plugin.Extensions
{
    // SPIKE ONLY (Zadanie 2). Wyrzucane w calosci w Zadaniu 12.
    // Cel: ustalic czy AIMP wola OnCheckURL dla adresow mixcloud.com i czy
    // honoruje podmieniony adres strumienia. Rozpoznanie adresu odbywa sie
    // na zywo przez yt-dlp, zeby nie polegac na wygasajacym linku ?sig=.
    public sealed class MixcloudPlayerHook : IAimpExtensionPlayerHook
    {
        private readonly string _logPath;

        public MixcloudPlayerHook(string logPath)
        {
            _logPath = logPath;
        }

        public bool OnCheckURL(ref string url)
        {
            Log("OnCheckURL: " + url);

            if (url == null || url.IndexOf("mixcloud.com", StringComparison.OrdinalIgnoreCase) < 0)
                return false;

            string ytDlpPath;
            if (!TryFindYtDlp(out ytDlpPath))
            {
                Log("  yt-dlp.exe not found on PATH; cannot resolve.");
                return false;
            }

            string resolved;
            string error;
            if (!TryResolveWithYtDlp(ytDlpPath, url, out resolved, out error))
            {
                Log("  yt-dlp resolution FAILED for: " + url);
                if (!string.IsNullOrEmpty(error))
                    Log("  yt-dlp stderr: " + error);
                return false;
            }

            Log("  page: " + url);
            Log("  -> resolved: " + resolved);
            url = resolved;
            return true;
        }

        private static bool TryFindYtDlp(out string path)
        {
            path = null;
            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(pathEnv)) return false;

            foreach (var dir in pathEnv.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                try
                {
                    var candidate = Path.Combine(dir.Trim(), "yt-dlp.exe");
                    if (File.Exists(candidate))
                    {
                        path = candidate;
                        return true;
                    }
                }
                catch
                {
                    // Malformed PATH entry; skip it.
                }
            }

            return false;
        }

        private bool TryResolveWithYtDlp(string ytDlpPath, string pageUrl, out string resolvedUrl, out string errorOutput)
        {
            resolvedUrl = null;
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            var psi = new ProcessStartInfo
            {
                FileName = ytDlpPath,
                Arguments = "-g -f \"http/hls-192/bestaudio\" --no-warnings \"" + pageUrl + "\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using (var process = new Process { StartInfo = psi, EnableRaisingEvents = true })
            {
                var stdoutClosed = new ManualResetEventSlim(false);
                var stderrClosed = new ManualResetEventSlim(false);

                process.OutputDataReceived += (sender, e) =>
                {
                    if (e.Data == null) { stdoutClosed.Set(); return; }
                    stdout.AppendLine(e.Data);
                };
                process.ErrorDataReceived += (sender, e) =>
                {
                    if (e.Data == null) { stderrClosed.Set(); return; }
                    stderr.AppendLine(e.Data);
                };

                try
                {
                    process.Start();
                }
                catch (Exception ex)
                {
                    errorOutput = "Failed to start yt-dlp.exe: " + ex.Message;
                    return false;
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                var exited = process.WaitForExit(60000);
                if (!exited)
                {
                    try { process.Kill(); } catch { /* best effort */ }
                    errorOutput = "yt-dlp timed out after 60s for: " + pageUrl;
                    return false;
                }

                // Drain remaining async output after exit.
                stdoutClosed.Wait(2000);
                stderrClosed.Wait(2000);

                errorOutput = stderr.ToString().Trim();

                using (var reader = new StringReader(stdout.ToString()))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        {
                            resolvedUrl = line.Trim();
                            return true;
                        }
                    }
                }

                return false;
            }
        }

        private void Log(string message)
        {
            try
            {
                File.AppendAllText(_logPath, DateTime.Now.ToString("s") + " " + message + Environment.NewLine);
            }
            catch
            {
                // Logging must never crash the hook.
            }
        }
    }
}
