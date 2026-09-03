using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using AIMP.SDK;
using AIMP.SDK.FileManager.Objects;
using AIMP.SDK.Playlist.Objects;
using Mixcloud.Core.Catalog;
using Mixcloud.Core.Localization;

namespace Mixcloud.Plugin.Playlists
{
    public sealed class PlaylistBuilder
    {
        // AddList(IList<IAimpFileInfo>,...) z flaga FileInfo wola wewnetrznie
        // AimpFileInfo.set_AlbumArt dla kazdej pozycji i nie sprawdza null -
        // AimpConverter.ToAimpImage rzuca NullReferenceException, gdy AlbumArt
        // jest nieustawiony (potwierdzone w dzienniku wtyczki). Jedna wspoldzielona
        // 1x1 bitmapa omija ten blad biblioteki bez alokowania osobnej na kazdy utwor.
        private static readonly Bitmap PlaceholderAlbumArt = new Bitmap(1, 1);

        // To samo wewnetrzne CreateFileInfo, ktore wymaga niepustego AlbumArt,
        // wymaga tez niepustych stringow dla WSZYSTKICH pol tekstowych - kazde
        // nietkniete zostaje null i przy pierwszym uzyciu rzuca
        // ArgumentNullException w SetString (potwierdzone w dzienniku dla
        // AlbumArtist). Zamiast lapac te wlasciwosci jedna po drugiej przy
        // kolejnych wdrozeniach, inicjalizujemy od razu wszystkie 26 pol
        // tekstowych z IAimpFileInfo pustym stringiem.
        private static void ClearAllStringFields(IAimpFileInfo info)
        {
            info.CustomData = string.Empty;
            info.Album = string.Empty;
            info.AlbumArtist = string.Empty;
            info.Artist = string.Empty;
            info.Codec = string.Empty;
            info.Comment = string.Empty;
            info.Composer = string.Empty;
            info.CopyRight = string.Empty;
            info.CUESheet = string.Empty;
            info.Date = string.Empty;
            info.DiskNumber = string.Empty;
            info.DiskTotal = string.Empty;
            info.FileName = string.Empty;
            info.Genre = string.Empty;
            info.Lyrics = string.Empty;
            info.Publisher = string.Empty;
            info.Title = string.Empty;
            info.TrackNumber = string.Empty;
            info.TrackTotal = string.Empty;
            info.URL = string.Empty;
            info.Conductor = string.Empty;
            info.Mood = string.Empty;
            info.Catalog = string.Empty;
            info.Isrc = string.Empty;
            info.Lyricist = string.Empty;
            info.EncodedBy = string.Empty;
        }

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
                // Budujemy IAimpFileInfo z gory zamiast dodawac gole adresy URL.
                // AddList(IList<string>,...) pokazuje w widoku playlisty sam adres
                // az do pierwszego odtworzenia - ReloadInfo() odpytuje
                // IAimpExtensionFileInfoProvider w tle i faktycznie rozwiazuje
                // tytuly (potwierdzone w dzienniku), ale AIMP nie przerysowuje
                // juz wyswietlonych wierszy, wiec uzytkownik nadal widzi URL-e.
                // Przekazanie gotowego tytulu/wykonawcy juz w AddList omija ten
                // problem calkowicie - flaga FileInfo mowi AIMP, ze lista
                // zawiera IAimpFileInfo, a nie string.
                var items = new List<IAimpFileInfo>();
                foreach (var t in listing.Tracks)
                {
                    var createdInfo = _ctx.Player.Core.CreateAimpObject<IAimpFileInfo>();
                    if (createdInfo.ResultType != ActionResultType.OK) continue;

                    var info = createdInfo.Result;
                    ClearAllStringFields(info);
                    info.FileName = t.PageUrl;
                    info.Title = t.Title;
                    info.Artist = t.Artist;
                    info.Album = "Mixcloud";
                    info.Duration = t.DurationSeconds;
                    info.AlbumArt = PlaceholderAlbumArt;
                    items.Add(info);
                }

                // NoCheckFormat: adresy Mixclouda nie sa plikami, ktore AIMP
                // umie rozpoznac po rozszerzeniu.
                playlist.AddList((IList<IAimpFileInfo>)items,
                    PlaylistFlags.NoCheckFormat | PlaylistFlags.FileInfo,
                    PlaylistFilePosition.EndPosition);
            }
            finally
            {
                playlist.EndUpdate();
            }

            _ctx.Player.ServicePlaylistManager.SetActivePlaylist(playlist);
        }
    }
}
