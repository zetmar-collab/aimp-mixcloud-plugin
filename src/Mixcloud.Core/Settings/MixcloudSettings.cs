using System;
using System.IO;
using Newtonsoft.Json;

namespace Mixcloud.Core.Settings
{
    public sealed class MixcloudSettings
    {
        public const int DefaultListingLimit = 200;
        public const long DefaultCacheLimitBytes = 5L * 1024 * 1024 * 1024;

        public string Handle { get; set; } = string.Empty;
        public int ListingLimit { get; set; } = DefaultListingLimit;
        public bool AutoUpdateYtDlp { get; set; } = true;
        public long CacheLimitBytes { get; set; } = DefaultCacheLimitBytes;
        public DateTime LastUpdateCheckUtc { get; set; } = DateTime.MinValue;
        public string LastKnownYtDlpTag { get; set; } = string.Empty;

        public static MixcloudSettings Load(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    var loaded = JsonConvert.DeserializeObject<MixcloudSettings>(File.ReadAllText(path));
                    if (loaded != null) return loaded.Normalized();
                }
            }
            catch (Exception)
            {
                // Uszkodzone ustawienia nie moga blokowac startu wtyczki.
            }
            return new MixcloudSettings();
        }

        public void Save(string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonConvert.SerializeObject(Normalized(), Formatting.Indented));
        }

        private MixcloudSettings Normalized()
        {
            if (ListingLimit < 1) ListingLimit = DefaultListingLimit;
            if (CacheLimitBytes < 1) CacheLimitBytes = DefaultCacheLimitBytes;
            if (Handle == null) Handle = string.Empty;
            if (LastKnownYtDlpTag == null) LastKnownYtDlpTag = string.Empty;
            return this;
        }
    }
}
