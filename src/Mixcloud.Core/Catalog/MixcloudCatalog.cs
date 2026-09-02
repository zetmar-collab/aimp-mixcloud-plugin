using System;
using System.Collections.Generic;
using System.Linq;
using Mixcloud.Core.Urls;
using Newtonsoft.Json.Linq;

namespace Mixcloud.Core.Catalog
{
    public static class MixcloudCatalog
    {
        public static MixcloudListing ParseFlatListing(IEnumerable<string> jsonLines)
        {
            var tracks = new List<MixcloudTrack>();
            var name = string.Empty;

            foreach (var line in jsonLines ?? Enumerable.Empty<string>())
            {
                JObject o;
                // Uszkodzona linia nie moze przerwac calej listy.
                try { o = JObject.Parse(line); } catch (Exception) { continue; }

                if (name.Length == 0)
                    name = (string)o["playlist_title"] ?? string.Empty;

                var url = (string)o["url"] ?? (string)o["webpage_url"];
                if (string.IsNullOrWhiteSpace(url)) continue;

                var parsed = MixcloudUrl.Parse(url);
                if (parsed.Kind != MixcloudUrlKind.Cloudcast) continue;

                tracks.Add(new MixcloudTrack
                {
                    PageUrl = parsed.Normalized,
                    // Tryb flat nie zwraca tytulow - wyprowadzamy je ze slugu,
                    // a AIMP uzupelni prawdziwe leniwie przez FileInfoProvider.
                    Title = SlugTitle.FromSlug(parsed.CloudcastSlug),
                    Artist = parsed.UserSlug,
                    DurationSeconds = 0d,
                    ThumbnailUrl = string.Empty
                });
            }

            return new MixcloudListing { Name = name, Tracks = tracks };
        }

        public static MixcloudTrack ParseCloudcast(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException("Pusta odpowiedz", nameof(json));

            var o = JObject.Parse(json);
            var pageUrl = (string)o["webpage_url"] ?? string.Empty;
            var parsed = MixcloudUrl.Parse(pageUrl);

            return new MixcloudTrack
            {
                PageUrl = parsed.Kind == MixcloudUrlKind.Cloudcast ? parsed.Normalized : pageUrl,
                Title = (string)o["title"] ?? string.Empty,
                Artist = (string)o["uploader_id"] ?? (string)o["uploader"] ?? string.Empty,
                DurationSeconds = (double?)o["duration"] ?? 0d,
                ThumbnailUrl = (string)o["thumbnail"] ?? string.Empty
            };
        }
    }
}
