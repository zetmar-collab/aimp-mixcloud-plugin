using System;
using System.Threading;
using Mixcloud.Core.Process;

public sealed class FakeProcessRunner : IProcessRunner
{
    public string NextStdOut { get; set; } = string.Empty;
    public string NextStdErr { get; set; } = string.Empty;
    public int NextExitCode { get; set; }
    public bool NextTimedOut { get; set; }

    public string LastExePath { get; private set; }
    public string LastArguments { get; private set; }
    public TimeSpan LastTimeout { get; private set; }

    public ProcessResult Run(string exePath, string arguments, TimeSpan timeout, CancellationToken ct)
    {
        LastExePath = exePath;
        LastArguments = arguments;
        LastTimeout = timeout;
        return new ProcessResult
        {
            ExitCode = NextExitCode,
            StdOut = NextStdOut,
            StdErr = NextStdErr,
            TimedOut = NextTimedOut
        };
    }
}
