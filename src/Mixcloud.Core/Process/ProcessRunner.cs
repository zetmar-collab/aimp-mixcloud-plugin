using System;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace Mixcloud.Core.Process
{
    public sealed class ProcessRunner : IProcessRunner
    {
        public ProcessResult Run(string exePath, string arguments, TimeSpan timeout, CancellationToken ct)
        {
            var psi = new ProcessStartInfo(exePath, arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            using (var proc = new System.Diagnostics.Process { StartInfo = psi })
            using (var outDone = new ManualResetEventSlim(false))
            using (var errDone = new ManualResetEventSlim(false))
            {
                proc.OutputDataReceived += (s, e) =>
                {
                    if (e.Data == null) outDone.Set(); else stdout.AppendLine(e.Data);
                };
                proc.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data == null) errDone.Set(); else stderr.AppendLine(e.Data);
                };

                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                var deadline = DateTime.UtcNow + timeout;
                while (!proc.HasExited)
                {
                    if (ct.IsCancellationRequested || DateTime.UtcNow > deadline)
                    {
                        try { proc.Kill(); } catch { /* juz zakonczony */ }
                        return new ProcessResult
                        {
                            ExitCode = -1,
                            StdOut = stdout.ToString(),
                            StdErr = stderr.ToString(),
                            TimedOut = true
                        };
                    }
                    Thread.Sleep(50);
                }

                outDone.Wait(TimeSpan.FromSeconds(2));
                errDone.Wait(TimeSpan.FromSeconds(2));

                return new ProcessResult
                {
                    ExitCode = proc.ExitCode,
                    StdOut = stdout.ToString(),
                    StdErr = stderr.ToString(),
                    TimedOut = false
                };
            }
        }
    }
}
