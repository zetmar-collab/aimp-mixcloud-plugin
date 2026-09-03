using System;
using System.Collections.Concurrent;
using System.Threading;
using AIMP.SDK;
using AIMP.SDK.FileManager.Extensions;
using AIMP.SDK.FileManager.Objects;
using AIMP.SDK.Objects;
using Mixcloud.Core.Catalog;
using Mixcloud.Core.Urls;
using Mixcloud.Core.YtDlp;

namespace Mixcloud.Plugin.Extensions
{
    public sealed class MixcloudFileInfoProvider : IAimpExtensionFileInfoProvider
    {
        private readonly IAimpCore _core;
        private readonly YtDlpService _ytDlp;

        // Klucz to url.Normalized. Wartoscia jest albo rozwiazany utwor, albo
        // null jako sentinel trwalego niepowodzenia. AIMP odpytuje o te sama
        // pozycje wielokrotnie przy kazdym przerysowaniu playlisty, a kazde
        // wywolanie yt-dlp kosztuje ok. 2 sekundy uruchomienia procesu - bez
        // cachowania porazek martwa pozycja spamowalaby yt-dlp w petli
        // malowania UI. TryGetValue zwraca true nawet dla wpisu null, wiec
        // rozroznia to od "jeszcze nie sprawdzono".
        private readonly ConcurrentDictionary<string, MixcloudTrack> _cache =
            new ConcurrentDictionary<string, MixcloudTrack>(StringComparer.OrdinalIgnoreCase);

        public MixcloudFileInfoProvider(IAimpCore core, YtDlpService ytDlp)
        {
            _core = core ?? throw new ArgumentNullException(nameof(core));
            _ytDlp = ytDlp ?? throw new ArgumentNullException(nameof(ytDlp));
        }

        public AimpActionResult GetFileInfo(string fileUri, ref IAimpFileInfo info)
        {
            try
            {
                info = null;

                var url = MixcloudUrl.Parse(fileUri);
                if (url.Kind != MixcloudUrlKind.Cloudcast)
                    return new AimpActionResult(ActionResultType.NoInterface);

                MixcloudTrack track;
                var known = _cache.TryGetValue(url.Normalized, out track);
                if (known && track == null)
                    return new AimpActionResult(ActionResultType.Fail);

                if (!known)
                {
                    try
                    {
                        track = MixcloudCatalog.ParseCloudcast(
                            _ytDlp.DumpCloudcast(url, CancellationToken.None));
                        _cache[url.Normalized] = track;
                        MixcloudPlugin.LogStartup("FileInfoProvider: rozwiazano '" + url.Normalized + "' -> '" + track.Title + "'");
                    }
                    catch (Exception ex)
                    {
                        _cache[url.Normalized] = null;
                        MixcloudPlugin.LogStartup("FileInfoProvider: nieudane rozwiazanie '" + url.Normalized + "': " + ex.Message);
                        return new AimpActionResult(ActionResultType.Fail);
                    }
                }

                var created = _core.CreateAimpObject<IAimpFileInfo>();
                if (created.ResultType != ActionResultType.OK)
                    return new AimpActionResult(created.ResultType);

                info = created.Result;
                info.FileName = url.Normalized;
                info.Title = track.Title;
                info.Artist = track.Artist;
                info.Album = "Mixcloud";
                info.Duration = track.DurationSeconds;
                return new AimpActionResult(ActionResultType.OK);
            }
            catch (Exception)
            {
                info = null;
                return new AimpActionResult(ActionResultType.Fail);
            }
        }

        public AimpActionResult GetFileInfo(IAimpStream stream, ref IAimpFileInfo info)
        {
            // Strumienie obsluguje AIMP samodzielnie; nas interesuja tylko adresy.
            info = null;
            return new AimpActionResult(ActionResultType.NotImplemented);
        }
    }
}
