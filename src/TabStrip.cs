using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;

namespace ProgramTabs
{
    /// <summary>
    /// The tab bar that floats directly above the active window of a view.
    /// It never takes activation (WS_EX_NOACTIVATE), so clicking it leaves focus with the real window.
    /// Pinned tabs come first: compact, with a pin instead of a close button; closed pinned tabs are
    /// shown dimmed and reopen their program when clicked.
    /// </summary>
    public class TabStrip : Form
    {
        readonly Manager manager;
        readonly View view;
        readonly TabGroup group;
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        readonly ToolTip tip = new ToolTip { ShowAlways = true, InitialDelay = 400, ReshowDelay = 100 };
        int hoverIndex = -1;
        bool hoverClose, hoverSplit;
        int pressIndex = -1;
        Point pressPoint;
        bool pressOnClose, pressOnSplit, dragging, dragOutside;
        bool dropHighlight;

        public TabStrip(Manager manager, View view)
        {
            this.manager = manager;
            this.view = view;
            this.group = view.Group;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Text = "ProgramTabs";
            IntPtr unused = Handle; // force handle creation
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_MOUSEACTIVATE) { m.Result = new IntPtr(Native.MA_NOACTIVATE); return; }
            if (m.Msg == Native.WM_DPICHANGED) { m.Result = IntPtr.Zero; Invalidate(); return; } // we size ourselves
            base.WndProc(ref m);
        }

        public bool DropHighlight
        {
            get { return dropHighlight; }
            set { if (dropHighlight != value) { dropHighlight = value; Invalidate(); } }
        }

        public void PlaceAt(RECT r, bool raise)
        {
            uint flags = Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW | Native.SWP_NOOWNERZORDER;
            if (!raise) flags |= Native.SWP_NOZORDER;
            Native.SetWindowPos(Handle, Native.HWND_TOP, r.Left, r.Top, r.Width, r.Height, flags);
            Invalidate();
        }

        public void HideStrip()
        {
            Native.ShowWindow(Handle, Native.SW_HIDE);
        }

        // ---------- layout ----------

        float S { get { return view.Scale; } }

        List<TabItem> Items() { return manager.GetTabItems(group); }

        Rectangle SplitRect()
        {
            int w = (int)(34 * S), m = (int)(4 * S);
            return new Rectangle(ClientSize.Width - w - m, m, w, ClientSize.Height - 2 * m);
        }

        /// <summary>Tab rectangles: pinned tabs get a fixed compact width, the others share the rest.</summary>
        Rectangle[] LayoutTabs(List<TabItem> items)
        {
            var rects = new Rectangle[items.Count];
            int pad = (int)(6 * S), gap = (int)(2 * S), top = (int)(4 * S);
            int avail = SplitRect().Left - 2 * pad;
            int iconW = (int)(40 * S);
            int pinnedCount = 0, iconCount = 0;
            var iconOnly = new bool[items.Count];
            for (int i = 0; i < items.Count; i++)
            {
                iconOnly[i] = manager.IsIconOnly(items[i]);
                if (iconOnly[i]) iconCount++;
                else if (items[i].IsPinned) pinnedCount++;
            }
            avail -= iconCount * (iconW + gap);
            int others = items.Count - pinnedCount - iconCount;
            int pw = (int)(150 * S);
            if (pinnedCount > 0 && pinnedCount * (pw + gap) > avail / 2) pw = Math.Max((int)(36 * S), avail / 2 / pinnedCount - gap);
            int rest = avail - pinnedCount * (pw + gap);
            int ow = others > 0 ? Math.Min((int)(230 * S), (rest - gap * (others - 1)) / others) : 0;
            ow = Math.Max(ow, (int)(28 * S));
            int x = pad;
            for (int i = 0; i < items.Count; i++)
            {
                int w = iconOnly[i] ? iconW : items[i].IsPinned ? pw : ow;
                rects[i] = new Rectangle(x, top, w, ClientSize.Height - top);
                x += w + gap;
            }
            return rects;
        }

        Rectangle CloseRect(Rectangle t)
        {
            int sz = (int)(18 * S);
            return new Rectangle(t.Right - sz - (int)(6 * S), t.Top + (t.Height - sz) / 2, sz, sz);
        }

        static int Hit(Rectangle[] rects, Point p)
        {
            for (int i = 0; i < rects.Length; i++) if (rects[i].Contains(p)) return i;
            return -1;
        }

        bool HasClose(TabItem it, Rectangle r, bool active, bool hover)
        {
            return !it.IsPinned && !manager.IsIconOnly(it) && (active || hover) && r.Width > 90 * S;
        }

        /// <summary>Text for a tab: the user's own name, else (part of) the window title.
        /// full = always the complete window title when there is no own name (for tooltips).</summary>
        string TabTitle(TabItem it, bool full)
        {
            string custom = manager.GetCustomName(it);
            if (custom != null) return custom;
            if (it.IsGhost) return it.Pin.Display;
            string t = WindowUtil.GetTitle(it.Hwnd);
            return it.IsPinned && !full ? PinRule.KeyFor(it.Pin.ProcessName, t) : t;
        }

        string TooltipText(TabItem it)
        {
            if (it.IsGhost) return TabTitle(it, true) + " (gesloten, klik om te openen)";
            string title = WindowUtil.GetTitle(it.Hwnd);
            string custom = manager.GetCustomName(it);
            return custom != null && custom != title ? custom + "\n" + title : title;
        }

        // ---------- painting ----------

        static GraphicsPath TopRounded(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            int d = radius * 2;
            p.AddArc(r.Left, r.Top, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            p.AddLine(r.Right, r.Bottom, r.Left, r.Bottom);
            p.CloseFigure();
            return p;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme th = Theme.Current;
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.Clear(dropHighlight ? th.DropHighlight : th.Background);
            bool mirrored = manager.IsMirrored(view);
            List<TabItem> items = Items();
            Rectangle[] rects = LayoutTabs(items);

            using (var font = new Font("Segoe UI", 12f * S, GraphicsUnit.Pixel))
            using (var ghostFont = new Font("Segoe UI", 12f * S, FontStyle.Italic, GraphicsUnit.Pixel))
            {
                for (int i = 0; i < items.Count; i++)
                {
                    TabItem it = items[i];
                    Rectangle r = rects[i];
                    bool active = !it.IsGhost && it.Hwnd == view.Active;
                    bool hover = i == hoverIndex;
                    bool elsewhere = !it.IsGhost && !active && manager.IsActiveElsewhere(view, it.Hwnd);
                    Color fill = active ? th.ActiveTab : (hover ? th.HoverTab : th.Tab);
                    using (GraphicsPath path = TopRounded(r, (int)(6 * S)))
                    using (var b = new SolidBrush(fill))
                        g.FillPath(b, path);
                    if (it.IsGhost)
                    {
                        // Closed pinned tab: dashed outline.
                        using (GraphicsPath path = TopRounded(Rectangle.Inflate(r, -1, 0), (int)(6 * S)))
                        using (var pen = new Pen(th.GhostText, Math.Max(1f, S)) { DashPattern = new float[] { 3f, 2f } })
                            g.DrawPath(pen, path);
                    }
                    if (active)
                    {
                        // Solid accent bar = the real window is here; dashed = live mirror of the other half.
                        float bar = Math.Max(2f, 2f * S);
                        using (var pen = new Pen(th.Accent, bar))
                        {
                            if (mirrored) pen.DashPattern = new float[] { 2f, 1.5f };
                            float y = r.Top + bar / 2;
                            g.DrawLine(pen, r.Left + 8 * S, y, r.Right - 8 * S, y);
                        }
                    }
                    else if (elsewhere)
                    {
                        int d = (int)(5 * S);
                        using (var db = new SolidBrush(th.Accent))
                            g.FillEllipse(db, r.Left + (r.Width - d) / 2, r.Bottom - d - (int)(2 * S), d, d);
                    }

                    bool iconOnly = manager.IsIconOnly(it);
                    int isz = (int)(16 * S);
                    int x = iconOnly ? r.Left + (r.Width - isz) / 2 : r.Left + (int)(8 * S);
                    Bitmap icon = it.IsGhost ? manager.GetExeIcon(it.Pin.ExePath) : manager.GetIcon(it.Hwnd);
                    if (icon == null && iconOnly)
                    {
                        // No icon available: show the first letter instead so the tab isn't empty.
                        string t = TabTitle(it, true);
                        TextRenderer.DrawText(g, t.Length > 0 ? t.Substring(0, 1).ToUpper() : "?", font, r,
                            active ? th.ActiveText : th.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    }
                    if (icon != null && (iconOnly || r.Width > isz + 12 * S))
                    {
                        var dest = new Rectangle(x, r.Top + (r.Height - isz) / 2, isz, isz);
                        if (it.IsGhost) DrawFaded(g, icon, dest, 0.45f);
                        else g.DrawImage(icon, dest);
                        x += isz + (int)(6 * S);
                    }

                    bool showClose = HasClose(it, r, active, hover);
                    int right;
                    if (showClose) right = CloseRect(r).Left - (int)(2 * S);
                    else if (it.IsPinned) right = r.Right - (int)(20 * S);
                    else right = r.Right - (int)(6 * S);

                    if (iconOnly)
                    {
                        // Icon-only tab: just a small pin marker in the corner for pinned tabs.
                        if (it.IsPinned)
                            DrawPin(g, new Point(r.Right - (int)(7 * S), r.Top + (int)(11 * S)), it.IsGhost ? th.GhostText : th.Accent, 0.6f);
                        continue;
                    }

                    string title = TabTitle(it, false);
                    if (active && mirrored) title = "Spiegel · " + title;
                    var textRect = new Rectangle(x, r.Top, Math.Max(0, right - x), r.Height);
                    TextRenderer.DrawText(g, title, it.IsGhost ? ghostFont : font, textRect,
                        it.IsGhost ? th.GhostText : (active ? th.ActiveText : th.Text),
                        TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis |
                        TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);

                    if (it.IsPinned && r.Width > 60 * S)
                        DrawPin(g, new Point(r.Right - (int)(12 * S), r.Top + r.Height / 2), it.IsGhost ? th.GhostText : th.Accent, 1f);

                    if (showClose)
                    {
                        Rectangle c = CloseRect(r);
                        if (hover && hoverClose)
                            using (var cb = new SolidBrush(th.CloseHover)) g.FillEllipse(cb, c);
                        int m = (int)(5.5f * S);
                        using (var pen = new Pen(active ? th.ActiveText : th.Text, Math.Max(1f, 1.3f * S)))
                        {
                            g.DrawLine(pen, c.Left + m, c.Top + m, c.Right - m, c.Bottom - m);
                            g.DrawLine(pen, c.Right - m, c.Top + m, c.Left + m, c.Bottom - m);
                        }
                    }
                }
            }
            DrawSplitButton(g, th);
        }

        /// <summary>A small push-pin: round head, short body, needle.</summary>
        void DrawPin(Graphics g, Point c, Color color, float k)
        {
            float s = S * k;
            using (var b = new SolidBrush(color))
            using (var pen = new Pen(color, Math.Max(1f, 1.4f * s)))
            {
                g.FillEllipse(b, c.X - 3.5f * s, c.Y - 7f * s, 7f * s, 7f * s);
                g.FillRectangle(b, c.X - 2f * s, c.Y - 2f * s, 4f * s, 4f * s);
                g.DrawLine(pen, c.X, c.Y + 2f * s, c.X, c.Y + 7f * s);
            }
        }

        static void DrawFaded(Graphics g, Bitmap bmp, Rectangle dest, float alpha)
        {
            var cm = new ColorMatrix { Matrix33 = alpha };
            using (var ia = new ImageAttributes())
            {
                ia.SetColorMatrix(cm);
                g.DrawImage(bmp, dest, 0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel, ia);
            }
        }

        void DrawSplitButton(Graphics g, Theme th)
        {
            Rectangle b = SplitRect();
            if (hoverSplit)
                using (var hb = new SolidBrush(th.HoverTab)) g.FillRectangle(hb, b);
            int iw = (int)(16 * S), ih = (int)(12 * S);
            var icon = new Rectangle(b.Left + (b.Width - iw) / 2, b.Top + (b.Height - ih) / 2, iw, ih);
            Color c = group.IsSplit ? th.Accent : th.Text;
            if (group.IsSplit)
            {
                bool left = group.Views.IndexOf(view) == 0;
                using (var fb = new SolidBrush(Color.FromArgb(110, c)))
                    g.FillRectangle(fb, left ? icon.Left : icon.Left + iw / 2, icon.Top, iw / 2, ih);
            }
            using (var pen = new Pen(c, Math.Max(1f, 1.3f * S)))
            {
                g.DrawRectangle(pen, icon);
                g.DrawLine(pen, icon.Left + iw / 2, icon.Top, icon.Left + iw / 2, icon.Bottom);
            }
        }

        // ---------- mouse ----------

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            List<TabItem> items = Items();
            Rectangle[] rects = LayoutTabs(items);
            if (pressIndex >= 0 && pressIndex < items.Count && (e.Button & MouseButtons.Left) != 0 && !items[pressIndex].IsGhost)
            {
                if (!dragging && (Math.Abs(e.X - pressPoint.X) > 6 * S || Math.Abs(e.Y - pressPoint.Y) > 6 * S))
                    dragging = true;
                if (dragging)
                {
                    int band = (int)(28 * S);
                    dragOutside = e.Y < -band || e.Y > ClientSize.Height + band || e.X < 0 || e.X > ClientSize.Width;
                    Cursor = dragOutside ? Cursors.SizeAll : Cursors.Default;
                    if (!dragOutside && !items[pressIndex].IsPinned)
                    {
                        int ni = Hit(rects, new Point(e.X, rects.Length > 0 ? rects[0].Top + 2 : 0));
                        if (ni >= 0 && ni != pressIndex && !items[ni].IsPinned && !items[ni].IsGhost)
                        {
                            manager.ReorderTo(group, items[pressIndex].Hwnd, items[ni].Hwnd);
                            pressIndex = ni;
                            hoverIndex = ni;
                        }
                    }
                    Invalidate();
                    return;
                }
            }
            int hi = Hit(rects, e.Location);
            bool hc = hi >= 0 && HasClose(items[hi], rects[hi], items[hi].Hwnd == view.Active, true) && CloseRect(rects[hi]).Contains(e.Location);
            bool hs = SplitRect().Contains(e.Location);
            if (hi != hoverIndex || hc != hoverClose || hs != hoverSplit)
            {
                if (hi != hoverIndex)
                    tip.SetToolTip(this, hi >= 0 ? TooltipText(items[hi]) : (hs ? "Gesplitst beeld aan/uit (Ctrl+Alt+S)" : null));
                hoverIndex = hi; hoverClose = hc; hoverSplit = hs;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (pressIndex < 0) { hoverIndex = -1; hoverClose = hoverSplit = false; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            List<TabItem> items = Items();
            Rectangle[] rects = LayoutTabs(items);
            pressIndex = Hit(rects, e.Location);
            pressPoint = e.Location;
            dragging = dragOutside = false;
            pressOnClose = pressIndex >= 0 && HasClose(items[pressIndex], rects[pressIndex], items[pressIndex].Hwnd == view.Active, true)
                && CloseRect(rects[pressIndex]).Contains(e.Location);
            pressOnSplit = SplitRect().Contains(e.Location);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            List<TabItem> items = Items();
            Rectangle[] rects = LayoutTabs(items);
            int idx = Hit(rects, e.Location);
            try
            {
                if (e.Button == MouseButtons.Left && pressOnSplit && SplitRect().Contains(e.Location))
                {
                    manager.ToggleSplit(group);
                    return;
                }
                if (e.Button == MouseButtons.Left && pressIndex >= 0 && pressIndex < items.Count)
                {
                    TabItem it = items[pressIndex];
                    if (dragging && !it.IsGhost)
                    {
                        Cursor = Cursors.Default;
                        if (dragOutside)
                        {
                            POINT p;
                            Native.GetCursorPos(out p);
                            View target = manager.ViewAtStrip(p);
                            if (target != null && target != view) manager.MoveToView(it.Hwnd, target);
                            else if (target == null) manager.Detach(view, it.Hwnd, p); // refused for pinned tabs
                        }
                    }
                    else if (idx == pressIndex)
                    {
                        if (it.IsGhost) manager.Reopen(view, it.Pin);
                        else if (pressOnClose && CloseRect(rects[idx]).Contains(e.Location)) WindowUtil.Close(it.Hwnd);
                        else manager.ActivateInView(view, it.Hwnd, true);
                    }
                }
                else if (e.Button == MouseButtons.Middle && idx >= 0 && !items[idx].IsGhost)
                {
                    manager.Detach(view, items[idx].Hwnd, null);
                }
                else if (e.Button == MouseButtons.Right && idx >= 0)
                {
                    ShowMenu(items[idx], e.Location);
                }
            }
            finally
            {
                pressIndex = -1;
                pressOnSplit = false;
                dragging = dragOutside = false;
            }
        }

        void AskName(TabItem it, string current, bool forProgram)
        {
            POINT p;
            Native.GetCursorPos(out p);
            string hint = forProgram ? "Geldt voor elk venster van " + manager.FriendlyNameOf(it) + ", ook in de toekomst." : null;
            using (var dlg = new RenameDialog(current, TabTitle(it, true), new Point(p.X, p.Y), hint))
            {
                DialogResult res = dlg.ShowDialog();
                if (res != DialogResult.OK && res != DialogResult.Yes) return;
                if (forProgram) manager.SetProgramLabel(it, dlg.ResultName, dlg.ResultIconOnly);
                else manager.SetLabel(it, dlg.ResultName, dlg.ResultIconOnly);
            }
        }

        void ShowMenu(TabItem it, Point at)
        {
            menu.Items.Clear();
            if (it.IsGhost)
            {
                menu.Items.Add("Openen", null, delegate { manager.Reopen(view, it.Pin); });
                menu.Items.Add("Niet meer vastzetten", null, delegate { manager.Unpin(it.Pin); });
            }
            else if (it.IsPinned)
            {
                menu.Items.Add("Niet meer vastzetten", null, delegate { manager.Unpin(it.Pin); });
                var close = new ToolStripMenuItem("Venster sluiten (vastgezet)") { Enabled = false };
                menu.Items.Add(close);
            }
            else
            {
                IntPtr h = it.Hwnd;
                menu.Items.Add("Tab vastzetten", null, delegate { manager.Pin(h); });
                menu.Items.Add("Losmaken uit groep", null, delegate { manager.Detach(view, h, null); });
                menu.Items.Add("Venster sluiten", null, delegate { WindowUtil.Close(h); });
            }

            // Close every (non-pinned) window of this program at once, e.g. all Word documents.
            string procName = manager.ProcessOf(it);
            string friendly = manager.FriendlyNameOf(it);
            int sameCount = manager.WindowsOfProgram(group, procName).Count;
            if (sameCount >= 2)
                menu.Items.Add("Alle vensters van " + friendly + " sluiten (" + sameCount + ")", null,
                    delegate { manager.CloseAllOfProgram(group, procName); });

            if (it.IsPinned)
            {
                PinRule rule = it.Pin;
                menu.Items.Add("Startopdracht wijzigen…", null, delegate
                {
                    POINT p;
                    Native.GetCursorPos(out p);
                    using (var dlg = new CommandDialog(rule, new Point(p.X, p.Y)))
                        if (dlg.ShowDialog() == DialogResult.OK) manager.SetPinCommand(rule, dlg.ResultExe, dlg.ResultArgs);
                });
            }

            // Name / icon-only: for this tab only, or always for this program.
            menu.Items.Add(new ToolStripSeparator());
            string custom = manager.GetCustomName(it);
            bool iconOnly = manager.IsIconOnly(it);
            string proc = procName.Length > 0 ? friendly : "";
            TabLabel progLabel = manager.GetProgramLabel(it);
            bool ownLabel = manager.HasOwnLabel(it);
            string thisTab = it.IsPinned ? "deze vaste tab" : "alleen dit venster";

            var nameMenu = new ToolStripMenuItem("Naam en weergave");
            nameMenu.DropDownItems.Add("Naam wijzigen (" + thisTab + ")…", null, delegate { AskName(it, custom, false); });
            if (proc.Length > 0)
                nameMenu.DropDownItems.Add("Naam wijzigen (altijd voor " + proc + ")…", null,
                    delegate { AskName(it, progLabel != null ? progLabel.Name : custom, true); });
            nameMenu.DropDownItems.Add(new ToolStripSeparator());
            var iconItem = new ToolStripMenuItem("Alleen icoon (" + thisTab + ")") { Checked = ownLabel && iconOnly };
            iconItem.Click += delegate { manager.SetLabel(it, custom, !iconOnly); };
            nameMenu.DropDownItems.Add(iconItem);
            if (proc.Length > 0)
            {
                bool progIcon = progLabel != null && progLabel.IconOnly;
                var progIconItem = new ToolStripMenuItem("Alleen icoon (altijd voor " + proc + ")") { Checked = progIcon };
                progIconItem.Click += delegate { manager.SetProgramLabel(it, progLabel != null ? progLabel.Name : null, !progIcon); };
                nameMenu.DropDownItems.Add(progIconItem);
            }
            if (ownLabel || progLabel != null) nameMenu.DropDownItems.Add(new ToolStripSeparator());
            if (ownLabel)
                nameMenu.DropDownItems.Add("Standaardnaam herstellen (" + thisTab + ")", null, delegate { manager.SetLabel(it, null, false); });
            if (progLabel != null)
                nameMenu.DropDownItems.Add("Naam voor " + proc + " vergeten", null, delegate { manager.SetProgramLabel(it, null, false); });
            menu.Items.Add(nameMenu);

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Gesplitst beeld aan/uit", null, delegate { manager.ToggleSplit(group); });
            menu.Items.Add("Groep opheffen", null, delegate { manager.Ungroup(group); });
            ShowMenuActivated(at);
        }

        Timer menuWatch;
        IntPtr menuRestoreTo;

        /// <summary>
        /// The strip never takes focus, so Windows wouldn't tell a context menu that the user clicked
        /// elsewhere and the menu would stay open. Give the menu focus while it is open, close it on any
        /// click outside our own windows, and hand the focus back to the active window afterwards.
        /// </summary>
        void ShowMenuActivated(Point at)
        {
            menuRestoreTo = view.Active;
            menu.Show(this, at);
            Native.SetForegroundWindow(menu.Handle);
            if (menuWatch == null)
            {
                menuWatch = new Timer { Interval = 80 };
                menuWatch.Tick += delegate
                {
                    if (!menu.Visible) { menuWatch.Stop(); return; }
                    bool down = (Native.GetAsyncKeyState(0x01) & 0x8000) != 0 || (Native.GetAsyncKeyState(0x02) & 0x8000) != 0 ||
                                (Native.GetAsyncKeyState(0x04) & 0x8000) != 0;
                    if (!down) return;
                    POINT p;
                    Native.GetCursorPos(out p);
                    IntPtr under = Native.WindowFromPoint(p);
                    if (WindowUtil.GetPid(under) != WindowUtil.OwnProcessId) menu.Close(ToolStripDropDownCloseReason.AppClicked);
                };
                menu.Closed += delegate(object s, ToolStripDropDownClosedEventArgs e)
                {
                    menuWatch.Stop();
                    // Give focus back to the real window, unless a menu action moved it somewhere else.
                    IntPtr back = menuRestoreTo;
                    var t = new Timer { Interval = 150 };
                    t.Tick += delegate
                    {
                        t.Stop(); t.Dispose();
                        IntPtr fg = Native.GetForegroundWindow();
                        bool ours = fg == IntPtr.Zero || fg == menu.Handle || fg == Handle;
                        if (ours && back != IntPtr.Zero && Native.IsWindow(back)) WindowUtil.Activate(back);
                    };
                    t.Start();
                };
            }
            menuWatch.Start();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            manager.Cycle(view, e.Delta < 0 ? 1 : -1);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button == MouseButtons.Left && Hit(LayoutTabs(Items()), e.Location) < 0 && !SplitRect().Contains(e.Location))
                manager.ToggleMaximize(view);
        }
    }
}
