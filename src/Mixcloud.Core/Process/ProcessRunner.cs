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

            // Wspolny obiekt blokady chroni zarowno bufory wyjscia, jak i flagi
            // "potok zamkniety" - zadny inny obiekt (np. ManualResetEventSlim) nie jest
            // wspoldzielony z watkami callbackow, wiec nie ma czego dispose'owac pod nimi.
            var sync = new object();
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            var outputClosed = false;
            var errorClosed = false;

            using (var proc = new System.Diagnostics.Process { StartInfo = psi })
            {
                proc.OutputDataReceived += (s, e) =>
                {
                    lock (sync)
                    {
                        if (e.Data == null)
                        {
                            outputClosed = true;
                            Monitor.PulseAll(sync);
                        }
                        else
                        {
                            stdout.AppendLine(e.Data);
                        }
                    }
                };
                proc.ErrorDataReceived += (s, e) =>
                {
                    lock (sync)
                    {
                        if (e.Data == null)
                        {
                            errorClosed = true;
                            Monitor.PulseAll(sync);
                        }
                        else
                        {
                            stderr.AppendLine(e.Data);
                        }
                    }
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

                        return BuildResult(sync, stdout, stderr, ref outputClosed, ref errorClosed, exitCode: -1, timedOut: true);
                    }
                    Thread.Sleep(50);
                }

                return BuildResult(sync, stdout, stderr, ref outputClosed, ref errorClosed, exitCode: proc.ExitCode, timedOut: false);
            }
        }

        /// <summary>
        /// Krotko i w sposob ograniczony czasowo czeka na doplyniecie juz zbuforowanych
        /// danych z potokow (rowniez po Kill()), po czym pod ta sama blokada odczytuje
        /// bufory - bez czekania na samoistne zakonczenie procesu.
        /// </summary>
        private static ProcessResult BuildResult(
            object sync,
            StringBuilder stdout,
            StringBuilder stderr,
            ref bool outputClosed,
            ref bool errorClosed,
            int exitCode,
            bool timedOut)
        {
            lock (sync)
            {
                var waitDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
                while (!outputClosed || !errorClosed)
                {
                    var remaining = waitDeadline - DateTime.UtcNow;
                    if (remaining <= TimeSpan.Zero) break;
                    Monitor.Wait(sync, remaining);
                }

                return new ProcessResult
                {
                    ExitCode = exitCode,
                    StdOut = stdout.ToString(),
                    StdErr = stderr.ToString(),
                    TimedOut = timedOut
                };
            }
        }
    }
}
