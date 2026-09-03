using System;
using System.Linq;

namespace Mixcloud.Core.Urls
{
    public enum MixcloudUrlKind { Invalid, Cloudcast, Listing }

    public sealed class MixcloudUrl
    {
        private static readonly string[] ListingSegments =
            { "uploads", "favorites", "listens", "stream", "playlists" };

        public MixcloudUrlKind Kind { get; private set; }
        public string Normalized { get; private set; }
        public string UserSlug { get; private set; }
        public string CloudcastSlug { get; private set; }

        private MixcloudUrl() { }

        private static readonly MixcloudUrl InvalidUrl =
            new MixcloudUrl { Kind = MixcloudUrlKind.Invalid };

        public static MixcloudUrl ForFavorites(string handle)
        {
            if (string.IsNullOrWhiteSpace(handle)) return InvalidUrl;
            return Parse("https://www.mixcloud.com/" + handle.Trim() + "/favorites/");
        }

        // Uzytkownicy czesto wklejaja caly adres profilu zamiast samej nazwy
        // (np. "https://www.mixcloud.com/spartacus/") - pole ustawien ma to
        // akceptowac zamiast wywalac sie pozniej w ForFavorites. Dopasowanie
        // hosta jest dokladne, tak samo jak w Parse - wersja bez schematu
        // ("mixcloud.com/spartacus") jest dopuszczona przez doklejenie
        // "https://" przed proba parsowania jako URL.
        public static string NormalizeHandle(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            var trimmed = input.Trim();

            var candidate = trimmed;
            if (!candidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !candidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                candidate.IndexOf("mixcloud.com", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                candidate = "https://" + candidate;
            }

            Uri uri;
            if (Uri.TryCreate(candidate, UriKind.Absolute, out uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                var host = uri.Host.ToLowerInvariant();
                if (host != "mixcloud.com" && host != "www.mixcloud.com") return string.Empty;

                var seg = uri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                return seg.Length > 0 ? seg[0] : string.Empty;
            }

            return trimmed.TrimStart('@').Trim('/');
        }

        public static MixcloudUrl Parse(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return InvalidUrl;

            Uri uri;
            if (!Uri.TryCreate(raw.Trim(), UriKind.Absolute, out uri)) return InvalidUrl;
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                return InvalidUrl;

            // Dokladne dopasowanie hosta - "mixcloud.com.evil.example" musi odpasc.
            var host = uri.Host.ToLowerInvariant();
            if (host != "mixcloud.com" && host != "www.mixcloud.com") return InvalidUrl;

            var seg = uri.AbsolutePath
                .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
                .ToArray();
            if (seg.Length == 0 || seg.Length > 2) return InvalidUrl;

            var user = seg[0];
            if (seg.Length == 1)
                return Listing(user, "https://www.mixcloud.com/" + user + "/");

            var second = seg[1];
            if (ListingSegments.Contains(second, StringComparer.OrdinalIgnoreCase))
                return Listing(user, "https://www.mixcloud.com/" + user + "/" + second + "/");

            return new MixcloudUrl
            {
                Kind = MixcloudUrlKind.Cloudcast,
                UserSlug = user,
                CloudcastSlug = second,
                Normalized = "https://www.mixcloud.com/" + user + "/" + second + "/"
            };
        }

        private static MixcloudUrl Listing(string user, string normalized)
        {
            return new MixcloudUrl
            {
                Kind = MixcloudUrlKind.Listing,
                UserSlug = user,
                CloudcastSlug = null,
                Normalized = normalized
            };
        }
    }
}
