using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using AIMP.SDK;
using AIMP.SDK.MenuManager;
using AIMP.SDK.MenuManager.Objects;
using Mixcloud.Core.Catalog;
using Mixcloud.Core.Localization;
using Mixcloud.Core.Process;
using Mixcloud.Core.Settings;
using Mixcloud.Core.Urls;
using Mixcloud.Core.YtDlp;
using Mixcloud.Plugin.Localization;
using Mixcloud.Plugin.Playlists;
using Mixcloud.Plugin.Ui;

namespace Mixcloud.Plugin
{
    [AimpPlugin("Mixcloud", "Marek Zettel", "1.0.0",
        AimpPluginType = AimpPluginType.Addons,
        Description = "Mixcloud integration for AIMP")]
    public sealed class MixcloudPlugin : AimpPlugin
    {
        private PluginContext _ctx;
        private YtDlpInstaller _installer;
        private IAimpMenuItem _openUrlItem;
        private IAimpMenuItem _favoritesItem;
        private Extensions.MixcloudPlayerHook _hook;

        // Wyjatek rzucony z Initialize sprawia, ze AIMP po cichu porzuca wtyczke
        // i nie pokazuje zadnego bledu. Bez tego dziennika kazda awaria startu
        // jest nie do odroznienia od "AIMP w ogole nie zaladowal wtyczki".
        private static readonly string StartupLogPath =
            Path.Combine(Path.GetTempPath(), "mixcloud-startup.log");

        internal static void LogStartup(string message)
        {
            try
            {
                File.AppendAllText(StartupLogPath,
                    DateTime.Now.ToString("HH:mm:ss.fff") + "  " + message + Environment.NewLine);
            }
            catch (Exception)
            {
                // Dziennik diagnostyczny nigdy nie moze wywrocic wtyczki.
            }
        }

        public override void Initialize()
        {
            LogStartup("Initialize() START");
            try
            {
                LogStartup("  Player = " + (Player == null ? "NULL" : Player.GetType().FullName));

                var strings = new MuiStringProvider(Player.ServiceMui);
                var dataDir = PluginContext.ResolveDataDir(Player);
                Directory.CreateDirectory(dataDir);

                var settings = MixcloudSettings.Load(Path.Combine(dataDir, "settings.json"));
                _installer = new YtDlpInstaller(new HttpDownloader(), dataDir);
                _installer.ApplyPendingUpdate();

                var ytDlp = new YtDlpService(new ProcessRunner(), _installer.ExePath);
                _ctx = new PluginContext(Player, strings, settings, ytDlp, dataDir);

                _openUrlItem = AddMenuItem("Mixcloud.OpenUrl", StringKeys.MenuOpenUrl, OnOpenUrl);
                LogStartup("  AddMenuItem(OpenUrl) -> " + (_openUrlItem == null ? "NULL" : "OK"));
                _favoritesItem = AddMenuItem("Mixcloud.Favorites", StringKeys.MenuLoadFavorites, OnLoadFavorites);
                LogStartup("  AddMenuItem(Favorites) -> " + (_favoritesItem == null ? "NULL" : "OK"));

                // SPIKE (Zadanie 2): rozstrzygniecie, czy AIMP honoruje adres
                // podmieniony w OnCheckURL. Usuwane w calosci w Zadaniu 12.
                var logPath = Path.Combine(Path.GetTempPath(), "mixcloud-spike.log");
                _hook = new Extensions.MixcloudPlayerHook(logPath);
                var registered = Player.Core.RegisterExtension(_hook);
                LogStartup("  RegisterExtension(PlayerHook) -> " + registered.ResultType);

                StartBackgroundSetup();

                LogStartup("Initialize() KONIEC OK");
            }
            catch (Exception ex)
            {
                LogStartup("Initialize() WYJATEK [" + ex.GetType().FullName + "]: " + ex.Message);
                LogStartup(ex.ToString());
                throw;
            }
        }

        public override void Dispose()
        {
            LogStartup("Dispose() START");
            try
            {
                if (_openUrlItem != null) { Player.ServiceMenuManager.Delete(_openUrlItem); _openUrlItem = null; }
                if (_favoritesItem != null) { Player.ServiceMenuManager.Delete(_favoritesItem); _favoritesItem = null; }

                if (_hook != null)
                {
                    Player.Core.UnregisterExtension(_hook);
                    _hook = null;
                }
            }
            catch (Exception ex)
            {
                LogStartup("Dispose() WYJATEK: " + ex);
            }
        }

        private IAimpMenuItem AddMenuItem(string id, string labelKey, Action onClick)
        {
            var created = Player.Core.CreateAimpObject<IAimpMenuItem>();
            if (created.ResultType != ActionResultType.OK) return null;

            var item = created.Result;
            item.Id = id;
            item.Name = _ctx.Strings.Get(labelKey);
            item.Style = MenuItemStyle.Normal;

            // Klikniecie obsluguje IAimpAction: dziedziczy po IAimpActionEvent,
            // wiec ma zdarzenie OnExecute. Wlasciwosc IAimpMenuItem.Custom jest
            // typu string i sluzy do czego innego - nie wolno jej tu uzyc.
            // ServiceActionManager.CreateAction() jest przestarzale (CS0618) -
            // wspolczesny odpowiednik to Core.CreateAimpObject<IAimpAction>().
            var createdAction = Player.Core.CreateAimpObject<AIMP.SDK.Actions.Objects.IAimpAction>();
            if (createdAction.ResultType != ActionResultType.OK) return null;

            var action = createdAction.Result;
            action.Id = id + ".Action";
            action.Name = item.Name;
            action.GroupName = "Mixcloud";
            action.Enabled = true;
            action.OnExecute += (s, e) => onClick();
            Player.ServiceActionManager.Register(action);
            item.Action = action;

            Player.ServiceMenuManager.Add(ParentMenuType.PlayerMainOpen, item);
            return item;
        }

        private void OnOpenUrl()
        {
            var raw = OpenUrlDialog.Show(_ctx.Strings);
            if (raw == null) return;

            var url = MixcloudUrl.Parse(raw);
            if (url.Kind == MixcloudUrlKind.Invalid)
            {
                ShowError(StringKeys.MsgInvalidUrl);
                return;
            }
            LoadAsync(url);
        }

        private void OnLoadFavorites()
        {
            if (string.IsNullOrWhiteSpace(_ctx.Settings.Handle))
            {
                ShowError(StringKeys.MsgNoHandle);
                return;
            }
            LoadAsync(MixcloudUrl.ForFavorites(_ctx.Settings.Handle));
        }

        private void LoadAsync(MixcloudUrl url)
        {
            Task.Run(() =>
            {
                try
                {
                    var listing = url.Kind == MixcloudUrlKind.Listing
                        ? MixcloudCatalog.ParseFlatListing(
                            _ctx.YtDlp.DumpListing(url, _ctx.Settings.ListingLimit, CancellationToken.None))
                        : SingleTrackListing(url);

                    if (listing.Tracks.Count == 0)
                    {
                        OnMainThread(() => ShowError(StringKeys.MsgEmptyResult));
                        return;
                    }
                    OnMainThread(() => new PlaylistBuilder(_ctx).Build(listing));
                }
                catch (YtDlpException)
                {
                    OnMainThread(() => ShowError(StringKeys.MsgYtDlpFailed));
                }
            });
        }

        private MixcloudListing SingleTrackListing(MixcloudUrl url)
        {
            var track = MixcloudCatalog.ParseCloudcast(
                _ctx.YtDlp.DumpCloudcast(url, CancellationToken.None));
            return new MixcloudListing
            {
                Name = track.Title,
                Tracks = new[] { track }
            };
        }

        private void StartBackgroundSetup()
        {
            Task.Run(() =>
            {
                try
                {
                    _installer.EnsureInstalled(CancellationToken.None);
                    if (_ctx.Settings.AutoUpdateYtDlp &&
                        DateTime.UtcNow - _ctx.Settings.LastUpdateCheckUtc > TimeSpan.FromHours(24))
                    {
                        _installer.CheckForUpdate(_ctx.Settings, CancellationToken.None);
                        _ctx.SaveSettings();
                    }
                }
                catch (Exception)
                {
                    // Awaria przygotowania yt-dlp zglosi sie dopiero przy probie uzycia.
                }
            });
        }

        private void OnMainThread(Action action)
        {
            // ExecuteInMainThread przyjmuje IAimpTask, nie Action - stad opakowanie.
            Player.ServiceSynchronizer.ExecuteInMainThread(new DelegateTask(action), true);
        }

        private sealed class DelegateTask : AIMP.SDK.Threading.IAimpTask
        {
            private readonly Action _action;
            public DelegateTask(Action action) { _action = action; }
            public void Execute(AIMP.SDK.Threading.IAimpTaskOwner owner) { _action(); }
        }

        private void ShowError(string messageKey)
        {
            MessageBox.Show(_ctx.Strings.Get(messageKey),
                _ctx.Strings.Get(StringKeys.MsgError),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
