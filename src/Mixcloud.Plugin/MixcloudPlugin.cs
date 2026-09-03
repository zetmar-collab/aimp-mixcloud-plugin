using System;
using System.IO;
using AIMP.SDK;
using AIMP.SDK.MenuManager;
using AIMP.SDK.MenuManager.Objects;
using Mixcloud.Core.Localization;
using Mixcloud.Plugin.Localization;

namespace Mixcloud.Plugin
{
    [AimpPlugin("Mixcloud", "Marek Zettel", "1.0.0",
        AimpPluginType = AimpPluginType.Addons,
        Description = "Mixcloud integration for AIMP")]
    public sealed class MixcloudPlugin : AimpPlugin
    {
        private IAimpMenuItem _probeItem;
        private Extensions.MixcloudPlayerHook _hook;
        private IStringProvider _strings;

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

                _strings = new MuiStringProvider(Player.ServiceMui);

                var created = Player.Core.CreateAimpObject<IAimpMenuItem>();
                LogStartup("  CreateAimpObject<IAimpMenuItem> -> " + created.ResultType);
                if (created.ResultType != ActionResultType.OK) return;

                _probeItem = created.Result;
                _probeItem.Id = "Mixcloud.Probe";
                _probeItem.Name = _strings.Get(StringKeys.MenuLoadFavorites);
                _probeItem.Style = MenuItemStyle.Normal;

                var added = Player.ServiceMenuManager.Add(ParentMenuType.PlayerMainOpen, _probeItem);
                LogStartup("  MenuManager.Add(PlayerMainOpen) -> " + added.ResultType);

                // SPIKE (Zadanie 2): rozstrzygniecie, czy AIMP honoruje adres
                // podmieniony w OnCheckURL. Usuwane w calosci w Zadaniu 12.
                var logPath = Path.Combine(Path.GetTempPath(), "mixcloud-spike.log");
                _hook = new Extensions.MixcloudPlayerHook(logPath);
                var registered = Player.Core.RegisterExtension(_hook);
                LogStartup("  RegisterExtension(PlayerHook) -> " + registered.ResultType);

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
                if (_probeItem != null)
                {
                    Player.ServiceMenuManager.Delete(_probeItem);
                    _probeItem = null;
                }

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
    }
}
