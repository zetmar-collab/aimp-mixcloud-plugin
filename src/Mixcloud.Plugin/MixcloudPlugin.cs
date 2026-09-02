using System.IO;
using AIMP.SDK;
using AIMP.SDK.MenuManager;
using AIMP.SDK.MenuManager.Objects;

namespace Mixcloud.Plugin
{
    [AimpPlugin("Mixcloud", "Marek Zettel", "1.0.0",
        AimpPluginType = AimpPluginType.Addons,
        Description = "Mixcloud integration for AIMP")]
    public sealed class MixcloudPlugin : AimpPlugin
    {
        private IAimpMenuItem _probeItem;
        private Extensions.MixcloudPlayerHook _hook;

        public override void Initialize()
        {
            var created = Player.Core.CreateAimpObject<IAimpMenuItem>();
            if (created.ResultType != ActionResultType.OK) return;

            _probeItem = created.Result;
            _probeItem.Id = "Mixcloud.Probe";
            _probeItem.Name = "Mixcloud: dziala";
            _probeItem.Style = MenuItemStyle.Normal;
            Player.ServiceMenuManager.Add(ParentMenuType.PlayerMainOpen, _probeItem);

            // SPIKE (Zadanie 2): rozstrzygniecie, czy AIMP honoruje adres
            // podmieniony w OnCheckURL. Usuwane w calosci w Zadaniu 12.
            var logPath = Path.Combine(Path.GetTempPath(), "mixcloud-spike.log");
            _hook = new Extensions.MixcloudPlayerHook(logPath);
            Player.Core.RegisterExtension(_hook);
        }

        public override void Dispose()
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
    }
}
