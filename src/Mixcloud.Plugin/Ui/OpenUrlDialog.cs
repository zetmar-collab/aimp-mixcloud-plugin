using System.Drawing;
using System.Windows.Forms;
using Mixcloud.Core.Localization;

namespace Mixcloud.Plugin.Ui
{
    public static class OpenUrlDialog
    {
        public static string Show(IStringProvider s)
        {
            using (var form = new Form())
            using (var prompt = new Label())
            using (var input = new TextBox())
            using (var ok = new Button())
            using (var cancel = new Button())
            {
                form.Text = s.Get(StringKeys.DialogOpenUrlTitle);
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.StartPosition = FormStartPosition.CenterScreen;
                form.MinimizeBox = false;
                form.MaximizeBox = false;
                form.ClientSize = new Size(520, 120);

                prompt.Text = s.Get(StringKeys.DialogOpenUrlPrompt);
                prompt.SetBounds(12, 12, 496, 20);

                input.SetBounds(12, 38, 496, 24);

                ok.Text = s.Get(StringKeys.DialogOk);
                ok.DialogResult = DialogResult.OK;
                ok.SetBounds(332, 78, 84, 28);

                cancel.Text = s.Get(StringKeys.DialogCancel);
                cancel.DialogResult = DialogResult.Cancel;
                cancel.SetBounds(424, 78, 84, 28);

                form.Controls.AddRange(new Control[] { prompt, input, ok, cancel });
                form.AcceptButton = ok;
                form.CancelButton = cancel;

                return form.ShowDialog() == DialogResult.OK && input.Text.Trim().Length > 0
                    ? input.Text.Trim()
                    : null;
            }
        }
    }
}
