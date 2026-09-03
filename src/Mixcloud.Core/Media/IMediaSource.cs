using System.Threading;

namespace Mixcloud.Core.Media
{
    public interface IMediaSource
    {
        string GetPlayableUrl(string pageUrl, CancellationToken ct);
    }
}
