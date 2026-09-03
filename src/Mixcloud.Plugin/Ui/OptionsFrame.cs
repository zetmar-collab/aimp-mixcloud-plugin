using System;
using System.Drawing;
using System.Windows.Forms;
using AIMP.SDK.Options;
using Mixcloud.Core.Localization;
using Mixcloud.Core.Settings;
using Mixcloud.Core.YtDlp;

namespace Mixcloud.Plugin.Ui
{
    // Wywolywana bezposrednio przez AIMP przez cztery metody interfejsu -
    // zaden wyjatek stad nie moze uciec w natywny kod hosta (patrz notatki
    // w MixcloudPlugin o tym samym wzorcu dla akcji menu).
    public sealed class OptionsFrame : IAimpOptionsDialogFrame
    {
        private readonly PluginContext _ctx;
        private readonly YtDlpInstaller _installer;
        private readonly Action _loadFavorites;

        private Panel _panel;
        private TextBox _handle;
        private NumericUpDown _limit;
        private NumericUpDown _cacheGb;
        private CheckBox _autoUpdate;
        private Label _version;

        public OptionsFrame(PluginContext ctx, YtDlpInstaller installer, Action loadFavorites)
        {
            _ctx = ctx;
            _installer = installer;
            _loadFavorites = loadFavorites;
        }

        public string GetName()
        {
            try
            {
                return "Mixcloud";
            }
            catch (Exception ex)
            {
                MixcloudPlugin.LogStartup("OptionsFrame.GetName WYJATEK: " + ex);
                return "Mixcloud";
            }
        }

        public IntPtr CreateFrame(IntPtr parentHandle)
        {
            try
            {
                // Dispose any existing panel before creating a new one to avoid handle leaks.
                if (_panel != null)
                {
                    _panel.Dispose();
                    _panel = null;
                }

                var s = _ctx.Strings;
                _panel = new Panel { Location = new Point(0, 0), Size = new Size(560, 340) };

                _panel.Controls.Add(Label(s.Get(StringKeys.OptHandle), 12, 15));
                _handle = new TextBox { Text = _ctx.Settings.Handle };
                _handle.SetBounds(240, 12, 300, 24);
                _panel.Controls.Add(_handle);

                var loadFavoritesNow = new Button { Text = s.Get(StringKeys.OptLoadFavoritesNow) };
                loadFavoritesNow.SetBounds(12, 45, 220, 26);
                loadFavoritesNow.Click += (o, e) => SafeLoadFavorites();
                _panel.Controls.Add(loadFavoritesNow);

                _panel.Controls.Add(Label(s.Get(StringKeys.OptListingLimit), 12, 87));
                _limit = new NumericUpDown { Minimum = 1, Maximum = 10000, Value = _ctx.Settings.ListingLimit };
                _limit.SetBounds(240, 84, 100, 24);
                _panel.Controls.Add(_limit);

                _panel.Controls.Add(Label(s.Get(StringKeys.OptCacheLimit), 12, 123));
                _cacheGb = new NumericUpDown
                {
                    Minimum = 1,
                    Maximum = 200,
                    Value = Math.Max(1, _ctx.Settings.CacheLimitBytes / (1024L * 1024 * 1024))
                };
                _cacheGb.SetBounds(240, 120, 100, 24);
                _panel.Controls.Add(_cacheGb);

                _autoUpdate = new CheckBox
                {
                    Text = s.Get(StringKeys.OptAutoUpdate),
                    Checked = _ctx.Settings.AutoUpdateYtDlp,
                    AutoSize = true
                };
                _autoUpdate.Location = new Point(12, 158);
                _panel.Controls.Add(_autoUpdate);

                _panel.Controls.Add(Label(s.Get(StringKeys.OptYtDlpVersion), 12, 195));
                _version = Label("...", 240, 195);
                _panel.Controls.Add(_version);

                var check = new Button { Text = s.Get(StringKeys.OptCheckNow) };
                check.SetBounds(12, 226, 220, 28);
                check.Click += (o, e) => SafeCheckNow();
                _panel.Controls.Add(check);

                var languageNote = new Label
                {
                    Text = s.Get(StringKeys.OptLanguageNote),
                    AutoSize = false,
                    Size = new Size(520, 40)
                };
                languageNote.Location = new Point(12, 266);
                _panel.Controls.Add(languageNote);

                RefreshVersion();

                // Osadzenie panelu w oknie ustawien AIMP.
                SetParent(_panel.Handle, parentHandle);
                return _panel.Handle;
            }
            catch (Exception ex)
            {
                MixcloudPlugin.LogStartup("OptionsFrame.CreateFrame WYJATEK: " + ex);
                return IntPtr.Zero;
            }
        }

        public void DestroyFrame()
        {
            try
            {
                if (_panel == null) return;
                _panel.Dispose();
                _panel = null;
            }
            catch (Exception ex)
            {
                MixcloudPlugin.LogStartup("OptionsFrame.DestroyFrame WYJATEK: " + ex);
            }
        }

        public void Notification(OptionsDialogFrameNotificationType type)
        {
            try
            {
                if (_panel == null) return;

                if (type == OptionsDialogFrameNotificationType.Save)
                {
                    _ctx.Settings.Handle = _handle.Text.Trim();
                    _ctx.Settings.ListingLimit = (int)_limit.Value;
                    _ctx.Settings.CacheLimitBytes = (long)_cacheGb.Value * 1024 * 1024 * 1024;
                    _ctx.Settings.AutoUpdateYtDlp = _autoUpdate.Checked;
                    _ctx.SaveSettings();
                }
                else if (type == OptionsDialogFrameNotificationType.Load)
                {
                    _handle.Text = _ctx.Settings.Handle;
                    _limit.Value = _ctx.Settings.ListingLimit;
                    var cacheLimitGb = Math.Max(1, _ctx.Settings.CacheLimitBytes / (1024L * 1024 * 1024));
                    cacheLimitGb = Math.Min(200, cacheLimitGb);
                    _cacheGb.Value = cacheLimitGb;
                    _autoUpdate.Checked = _ctx.Settings.AutoUpdateYtDlp;
                    RefreshVersion();
                }
                // Localization i CanSave: strona nie ma wlasnego przelacznika jezyka
                // (idzie za jezykiem AIMP przez IStringProvider) i zawsze mozna zapisac,
                // wiec te powiadomienia sa celowo ignorowane.
            }
            catch (Exception ex)
            {
                MixcloudPlugin.LogStartup("OptionsFrame.Notification WYJATEK: " + ex);
            }
        }

        private void SafeLoadFavorites()
        {
            try
            {
                if (_loadFavorites != null) _loadFavorites();
            }
            catch (Exception ex)
            {
                MixcloudPlugin.LogStartup("OptionsFrame.SafeLoadFavorites WYJATEK: " + ex);
            }
        }

        private void SafeCheckNow()
        {
            try
            {
                CheckNow();
            }
            catch (Exception ex)
            {
                MixcloudPlugin.LogStartup("OptionsFrame.SafeCheckNow WYJATEK: " + ex);
            }
        }

        private void CheckNow()
        {
            // Sprawdzenie aktualizacji uderza w siec - nie moze isc na watku UI.
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    _installer.CheckForUpdate(_ctx.Settings, System.Threading.CancellationToken.None);
                    _ctx.SaveSettings();
                }
                catch (Exception ex)
                {
                    MixcloudPlugin.LogStartup("OptionsFrame.CheckNow WYJATEK: " + ex);
                }
            });
        }

        private void RefreshVersion()
        {
            // GetVersion odpala proces yt-dlp (~2s) - nie moze isc na watku UI.
            System.Threading.Tasks.Task.Run(() =>
            {
                string v;
                try { v = _ctx.YtDlp.GetVersion(System.Threading.CancellationToken.None); }
                catch (Exception) { v = _ctx.Strings.Get(StringKeys.MsgYtDlpMissing); }

                try
                {
                    // Panel mogl juz zostac zniszczony (DestroyFrame) zanim watek
                    // w tle skonczyl - kazde odwolanie do kontrolki trzeba
                    // zabezpieczyc, zeby BeginInvoke nie trafilo w martwy uchwyt.
                    var label = _version;
                    var panel = _panel;
                    if (label == null || panel == null) return;
                    if (label.IsDisposed || panel.IsDisposed) return;
                    if (!label.IsHandleCreated) return;

                    label.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            if (!label.IsDisposed) label.Text = v;
                        }
                        catch (Exception ex)
                        {
                            MixcloudPlugin.LogStartup("OptionsFrame.RefreshVersion BeginInvoke WYJATEK: " + ex);
                        }
                    }));
                }
                catch (Exception ex)
                {
                    MixcloudPlugin.LogStartup("OptionsFrame.RefreshVersion WYJATEK: " + ex);
                }
            });
        }

        private static Label Label(string text, int x, int y)
        {
            var l = new Label { Text = text, AutoSize = true };
            l.Location = new Point(x, y);
            return l;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetParent(IntPtr child, IntPtr newParent);
    }
}
