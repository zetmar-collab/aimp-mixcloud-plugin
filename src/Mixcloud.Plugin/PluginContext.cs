using System.IO;
using AIMP.SDK;
using AIMP.SDK.MessageDispatcher;
using Mixcloud.Core.Localization;
using Mixcloud.Core.Settings;
using Mixcloud.Core.YtDlp;

namespace Mixcloud.Plugin
{
    public sealed class PluginContext
    {
        public IAimpPlayer Player { get; }
        public IStringProvider Strings { get; }
        public MixcloudSettings Settings { get; }
        public YtDlpService YtDlp { get; }
        public string DataDir { get; }

        public PluginContext(IAimpPlayer player, IStringProvider strings,
            MixcloudSettings settings, YtDlpService ytDlp, string dataDir)
        {
            Player = player;
            Strings = strings;
            Settings = settings;
            YtDlp = ytDlp;
            DataDir = dataDir;
        }

        public string SettingsPath => Path.Combine(DataDir, "settings.json");

        public void SaveSettings() => Settings.Save(SettingsPath);

        public static string ResolveDataDir(IAimpPlayer player)
        {
            // Profil AIMP, nie zgadywany %APPDATA% i nie Program Files.
            var profile = player.Core.GetPath(AimpCorePathType.Profile);
            return Path.Combine(profile, "Mixcloud");
        }
    }
}
