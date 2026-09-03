using System;
using System.Collections.Concurrent;
using System.Threading;
using Mixcloud.Core.YtDlp;

namespace Mixcloud.Core.Media
{
    public sealed class StreamMediaSource : IMediaSource
    {
        private sealed class Entry
        {
            public string Url;
            public DateTime ResolvedUtc;
        }

        private readonly YtDlpService _ytDlp;
        private readonly TimeSpan _lifetime;
        private readonly ConcurrentDictionary<string, Entry> _cache =
            new ConcurrentDictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        public StreamMediaSource(YtDlpService ytDlp, TimeSpan cacheLifetime)
        {
            _ytDlp = ytDlp ?? throw new ArgumentNullException(nameof(ytDlp));
            _lifetime = cacheLifetime;
        }

        public string GetPlayableUrl(string pageUrl, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(pageUrl)) return null;

            Entry cached;
            if (_cache.TryGetValue(pageUrl, out cached) &&
                DateTime.UtcNow - cached.ResolvedUtc < _lifetime)
            {
                return cached.Url;
            }

            try
            {
                // Adres zawiera parametr ?sig= i wygasa, dlatego trzymamy go
                // tylko przez _lifetime, a potem rozwiazujemy od nowa.
                var resolved = _ytDlp.ResolveDirectUrl(Urls.MixcloudUrl.Parse(pageUrl), ct);
                if (string.IsNullOrEmpty(resolved)) return null;

                _cache[pageUrl] = new Entry { Url = resolved, ResolvedUtc = DateTime.UtcNow };
                return resolved;
            }
            catch (YtDlpException)
            {
                return null;
            }
        }
    }
}
