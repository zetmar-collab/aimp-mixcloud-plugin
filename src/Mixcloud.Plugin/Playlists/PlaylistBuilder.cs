using System;
using System.Collections.Generic;
using System.Linq;
using AIMP.SDK;
using AIMP.SDK.Playlist.Objects;
using Mixcloud.Core.Catalog;
using Mixcloud.Core.Localization;

namespace Mixcloud.Plugin.Playlists
{
    public sealed class PlaylistBuilder
    {
        private readonly PluginContext _ctx;

        public PlaylistBuilder(PluginContext ctx)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
        }

        public void Build(MixcloudListing listing)
        {
            if (listing == null || listing.Tracks.Count == 0) return;

            var name = string.IsNullOrWhiteSpace(listing.Name) ? "Mixcloud" : listing.Name;

            // Zawsze nowa playlista - nigdy nie dopisujemy do tej,
            // ktorej uzytkownik wlasnie slucha.
            var created = _ctx.Player.ServicePlaylistManager.CreatePlaylist(name, true);
            if (created.ResultType != ActionResultType.OK)
            {
                System.Windows.Forms.MessageBox.Show(
                    _ctx.Strings.Get(StringKeys.MsgPlaylistFailed),
                    _ctx.Strings.Get(StringKeys.MsgError),
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Warning);
                return;
            }

            var playlist = created.Result;
            playlist.BeginUpdate();
            try
            {
                IList<string> urls = listing.Tracks.Select(t => t.PageUrl).ToList();
                // NoCheckFormat: adresy Mixclouda nie sa plikami, ktore AIMP
                // umie rozpoznac po rozszerzeniu.
                playlist.AddList(urls, PlaylistFlags.NoCheckFormat, PlaylistFilePosition.EndPosition);
            }
            finally
            {
                playlist.EndUpdate();
            }

            // AddList sam z siebie nie odpytuje IAimpExtensionFileInfoProvider -
            // bez tego wywolania pozycje pokazuja surowy adres URL jako tytul
            // az do pierwszego odtworzenia. ReloadInfo(false) dziala w tle
            // (dokumentacja SDK) i dotyczy tylko pozycji bez informacji, wiec
            // nie odpala ponownie yt-dlp dla juz znanych tytulow.
            playlist.ReloadInfo(false);

            _ctx.Player.ServicePlaylistManager.SetActivePlaylist(playlist);
        }
    }
}
