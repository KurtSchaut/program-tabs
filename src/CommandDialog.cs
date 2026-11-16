using System;
using System.Drawing;
using System.Windows.Forms;

namespace ProgramTabs
{
    /// <summary>Edits what a closed pinned tab starts: program + arguments (e.g. a web address for a browser).</summary>
    public class CommandDialog : Form
    {
        readonly TextBox exeBox, argsBox;
        public string ResultExe { get { return exeBox.Text.Trim(); } }
        public string ResultArgs { get { return argsBox.Text.Trim(); } }

        public CommandDialog(PinRule rule, Point near)
        {
            Text = "Startopdracht van vaste tab";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9f);
            Padding = new Padding(12);

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoSize = true };
            bool browser = Browser.IsBrowser(rule.ProcessName);
            layout.Controls.Add(new Label
            {
                AutoSize = true, MaximumSize = new Size(460, 0),
                Text = "Wat er gestart wordt als je op de gesloten vaste tab \"" + rule.Display + "\" klikt." +
                       (browser ? "\nVoor een webpagina in een eigen venster: --new-window \"https://…\"" : "")
            });
            layout.Controls.Add(new Label { AutoSize = true, Text = "Programma:", Margin = new Padding(0, 8, 0, 0) });
            exeBox = new TextBox { Width = 460, Text = rule.ExePath };
            layout.Controls.Add(exeBox);
            layout.Controls.Add(new Label { AutoSize = true, Text = "Startargumenten:", Margin = new Padding(0, 8, 0, 0) });
            argsBox = new TextBox { Width = 460, Text = rule.Args };
            layout.Controls.Add(argsBox);

            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 10, 0, 0) };
            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
            var cancel = new Button { Text = "Annuleren", DialogResult = DialogResult.Cancel, AutoSize = true };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            layout.Controls.Add(buttons);
            Controls.Add(layout);
            AcceptButton = ok;
            CancelButton = cancel;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            Load += delegate
            {
                Rectangle wa = Screen.FromPoint(near).WorkingArea;
                Location = new Point(Math.Max(wa.Left, Math.Min(near.X - Width / 2, wa.Right - Width)),
                                     Math.Max(wa.Top, Math.Min(near.Y + 12, wa.Bottom - Height)));
                argsBox.Focus();
                argsBox.SelectAll();
            };
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
        }
    }
}
