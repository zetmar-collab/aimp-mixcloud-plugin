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

        public override void Initialize()
        {
            var created = Player.Core.CreateAimpObject<IAimpMenuItem>();
            if (created.ResultType != ActionResultType.OK) return;

            _probeItem = created.Result;
            _probeItem.Id = "Mixcloud.Probe";
            _probeItem.Name = "Mixcloud: dziala";
            _probeItem.Style = MenuItemStyle.Normal;
            Player.ServiceMenuManager.Add(ParentMenuType.PlayerMainOpen, _probeItem);
        }

        public override void Dispose()
        {
            if (_probeItem != null)
            {
                Player.ServiceMenuManager.Delete(_probeItem);
                _probeItem = null;
            }
        }
    }
}
