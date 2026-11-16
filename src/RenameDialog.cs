using System;
using System.Drawing;
using System.Windows.Forms;

namespace ProgramTabs
{
    /// <summary>
    /// Small dialog to rename a tab. Empty name = show only the icon; "Standaard" = back to the window title.
    /// </summary>
    public class RenameDialog : Form
    {
        readonly TextBox box;
        public string ResultName { get; private set; }   // null = default title
        public bool ResultIconOnly { get; private set; }

        public RenameDialog(string currentName, string windowTitle, Point near, string scopeHint)
        {
            Text = "Tab hernoemen";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9f);
            Padding = new Padding(12);

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoSize = true };
            var label = new Label
            {
                AutoSize = true, MaximumSize = new Size(380, 0),
                Text = "Naam voor deze tab. Laat leeg om alleen het icoontje te tonen.\n" +
                       (scopeHint != null ? scopeHint + "\n" : "") + "Venstertitel: " + windowTitle
            };
            box = new TextBox { Width = 380, Text = currentName ?? "" };
            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 10, 0, 0) };
            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
            var cancel = new Button { Text = "Annuleren", DialogResult = DialogResult.Cancel, AutoSize = true };
            var reset = new Button { Text = "Standaardnaam", AutoSize = true };
            reset.Click += delegate { ResultName = null; ResultIconOnly = false; DialogResult = DialogResult.Yes; Close(); };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            buttons.Controls.Add(reset);
            layout.Controls.Add(label);
            layout.Controls.Add(box);
            layout.Controls.Add(buttons);
            Controls.Add(layout);
            AcceptButton = ok;
            CancelButton = cancel;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            FormClosing += delegate
            {
                if (DialogResult == DialogResult.OK)
                {
                    string t = box.Text.Trim();
                    ResultName = t.Length > 0 ? t : null;
                    ResultIconOnly = t.Length == 0;
                }
            };
            Load += delegate
            {
                // Place under the cursor, kept on screen.
                Rectangle wa = Screen.FromPoint(near).WorkingArea;
                int x = Math.Max(wa.Left, Math.Min(near.X - Width / 2, wa.Right - Width));
                int y = Math.Max(wa.Top, Math.Min(near.Y + 12, wa.Bottom - Height));
                Location = new Point(x, y);
                box.SelectAll();
                box.Focus();
            };
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
        }
    }
}
