using System;
using System.Threading;

namespace Mixcloud.Core.Process
{
    public interface IProcessRunner
    {
        ProcessResult Run(string exePath, string arguments, TimeSpan timeout, CancellationToken ct);
    }
}
