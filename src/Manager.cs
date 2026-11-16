using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ProgramTabs
{
    /// <summary>
    /// A set of windows shown as tabs. A group has one view, or two side by side ("split view").
    /// All views of a group show the same tabs; each view shows its own active tab.
    /// </summary>
    public class TabGroup
    {
        public readonly List<IntPtr> Windows = new List<IntPtr>();
        public readonly List<View> Views = new List<View>();
        /// <summary>The view each window physically sits in (stacked behind that view's active window).</summary>
        public readonly Dictionary<IntPtr, View> PlacedIn = new Dictionary<IntPtr, View>();
        public bool IsSplit { get { return Views.Count > 1; } }
    }

    /// <summary>One rectangle with a tab bar above it. In split view a group has two.</summary>
    public class View
    {
        public TabGroup Group;
        public RECT Bounds;              // visible frame shared by the windows placed in this view
        public RECT? RestoreBounds;      // for "maximize" toggle
        public IntPtr Active;            // tab shown in this view (may be a mirror of a window placed elsewhere)
        public RECT StripRect;
        public bool StripVisible;
        public bool Dragging;
        public float Scale = 1f;
        public TabStrip Strip;
        public MirrorForm Mirror;
        public Timer SyncTimer;
    }

    /// <summary>
    /// Core logic: keeps the windows of a view stacked on the same rectangle,
    /// with a TabStrip floating above the active one.
    /// </summary>
    public class Manager : ApplicationContext
    {
        readonly List<TabGroup> groups = new List<TabGroup>();
        readonly Dictionary<IntPtr, Bitmap> icons = new Dictionary<IntPtr, Bitmap>();
        readonly HashSet<IntPtr> userDetached = new HashSet<IntPtr>();
        readonly Dictionary<IntPtr, DateTime> pendingNew = new Dictionary<IntPtr, DateTime>();
        readonly List<IntPtr> hooks = new List<IntPtr>();
        readonly Native.WinEventDelegate winEventProc; // keep a reference so the GC doesn't collect it
        readonly Timer pendingTimer;
        readonly NotifyIcon tray;
        readonly HotkeyWindow hotkeys;
        readonly List<PinRule> pins = PinStore.Load();
        readonly Dictionary<IntPtr, PinRule> pinned = new Dictionary<IntPtr, PinRule>();
        readonly Dictionary<string, Bitmap> exeIcons = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        View reopenTarget;
        ToolStripMenuItem autoAddItem;
        View lastView;
        IntPtr movingWindow;
        View highlighted;
        bool autoAdd;

        public Manager(bool groupAllAtStart, bool splitAtStart)
        {
            winEventProc = OnWinEvent;
            uint flags = Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS;
            uint[,] ranges = {
                { Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND },
                { Native.EVENT_SYSTEM_MOVESIZESTART, Native.EVENT_SYSTEM_MINIMIZEEND },
                { Native.EVENT_OBJECT_DESTROY, Native.EVENT_OBJECT_HIDE },
                { Native.EVENT_OBJECT_LOCATIONCHANGE, Native.EVENT_OBJECT_NAMECHANGE },
                { Native.EVENT_OBJECT_CLOAKED, Native.EVENT_OBJECT_UNCLOAKED },
            };
            for (int i = 0; i < ranges.GetLength(0); i++)
                hooks.Add(Native.SetWinEventHook(ranges[i, 0], ranges[i, 1], IntPtr.Zero, winEventProc, 0, 0, flags));

            pendingTimer = new Timer { Interval = 250 };
            pendingTimer.Tick += delegate { ProcessPending(); };
            pendingTimer.Start();

            tray = CreateTray();
            hotkeys = new HotkeyWindow();
            hotkeys.Register(1, Native.MOD_CONTROL | Native.MOD_ALT, (uint)Keys.T, ToggleForeground);
            hotkeys.Register(2, Native.MOD_CONTROL | Native.MOD_ALT, (uint)Keys.G, GroupAll);
            hotkeys.Register(3, Native.MOD_CONTROL | Native.MOD_ALT, (uint)Keys.U, UngroupAll);
            hotkeys.Register(4, Native.MOD_CONTROL | Native.MOD_ALT, (uint)Keys.PageDown, delegate { CycleForeground(1); });
            hotkeys.Register(5, Native.MOD_CONTROL | Native.MOD_ALT, (uint)Keys.PageUp, delegate { CycleForeground(-1); });
            hotkeys.Register(6, Native.MOD_CONTROL | Native.MOD_ALT, (uint)Keys.S, ToggleSplitForeground);

            StartUpdateChecks();

            if (groupAllAtStart)
            {
                GroupAll();
                if (splitAtStart) foreach (TabGroup g in groups.ToArray()) ToggleSplit(g);
            }
            else tray.ShowBalloonTip(4000, "ProgramTabs", "Ctrl+Alt+G groepeert alle vensters als tabs.", ToolTipIcon.Info);
            Log.Write("started; groupAll=" + groupAllAtStart + " split=" + splitAtStart);
        }

        // ================= tray =================

        NotifyIcon CreateTray()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Alle vensters groeperen\tCtrl+Alt+G", null, delegate { GroupAll(); });
            menu.Items.Add("Gesplitst beeld aan/uit\tCtrl+Alt+S", null, delegate { ToggleSplitForeground(); });
            menu.Items.Add("Alles losmaken\tCtrl+Alt+U", null, delegate { UngroupAll(); });
            autoAddItem = new ToolStripMenuItem("Nieuwe vensters automatisch als tab toevoegen");
            autoAddItem.Click += delegate { autoAdd = !autoAdd; autoAddItem.Checked = autoAdd; };
            menu.Items.Add(autoAddItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Sneltoetsen…", null, delegate
            {
                MessageBox.Show(
                    "Ctrl+Alt+G\tAlle vensters groeperen (één groep per scherm)\n" +
                    "Ctrl+Alt+S\tGesplitst beeld aan/uit (twee helften met dezelfde tabs)\n" +
                    "Ctrl+Alt+T\tActief venster toevoegen aan / losmaken uit groep\n" +
                    "Ctrl+Alt+PgDn/PgUp\tVolgende / vorige tab\n" +
                    "Ctrl+Alt+U\tAlles losmaken\n\n" +
                    "Tabbalk:\n" +
                    "  klik = wisselen, × = sluiten, middelklik = losmaken\n" +
                    "  sleep tab = herschikken, sleep tab naar andere helft = daar tonen\n" +
                    "  sleep tab naar buiten = losmaken\n" +
                    "  sleep een venster op een tabbalk = toevoegen\n" +
                    "  knop rechts = gesplitst beeld aan/uit\n\n" +
                    "Gesplitst beeld: kies je in beide helften hetzelfde venster, dan toont\n" +
                    "de andere helft een live spiegelbeeld. Klik erop om daar te werken.",
                    "ProgramTabs – sneltoetsen");
            });
            menu.Items.Add(new ToolStripSeparator());
            var autostartItem = new ToolStripMenuItem("Starten met Windows");
            menu.Opening += delegate
            {
                try { autostartItem.Checked = Updater.GetAutostart(); } catch (Exception) { }
                installItem.Visible = !Updater.IsInstalled;
            };
            autostartItem.Click += delegate
            {
                try { Updater.SetAutostart(!Updater.GetAutostart(), null); }
                catch (Exception ex) { MessageBox.Show("Kon niet instellen: " + ex.Message, "ProgramTabs"); }
            };
            menu.Items.Add(autostartItem);
            installItem = new ToolStripMenuItem("Installeren voor deze gebruiker…");
            installItem.Click += delegate { Install(); };
            menu.Items.Add(installItem);
            menu.Items.Add("Controleren op updates", null, delegate { CheckForUpdates(true); });
            menu.Items.Add(new ToolStripMenuItem("Versie " + Updater.CurrentText) { Enabled = false });
            menu.Items.Add("Afsluiten", null, delegate { ExitThread(); });

            var ni = new NotifyIcon { Icon = MakeTrayIcon(), Text = "ProgramTabs", ContextMenuStrip = menu, Visible = true };
            ni.DoubleClick += delegate { GroupAll(); };
            return ni;
        }

        // ================= updates & install =================

        ToolStripMenuItem installItem;
        Control invoker;              // to get back on the UI thread after background work
        Timer updateTimer;
        Updater.Release pendingRelease;
        bool updateBusy;

        void StartUpdateChecks()
        {
            invoker = new Control();
            invoker.CreateControl();
            IntPtr unused = invoker.Handle;
            tray.BalloonTipClicked += delegate { if (pendingRelease != null) AskAndUpdate(pendingRelease); };
            // First check shortly after start, then once a day.
            updateTimer = new Timer { Interval = 60 * 1000 };
            updateTimer.Tick += delegate
            {
                updateTimer.Interval = 24 * 60 * 60 * 1000;
                CheckForUpdates(false);
            };
            updateTimer.Start();
        }

        /// <summary>Checks GitHub in the background. manual = also report "up to date" / errors.</summary>
        public void CheckForUpdates(bool manual)
        {
            if (updateBusy) return;
            updateBusy = true;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                Updater.Release r = Updater.GetLatest();
                invoker.BeginInvoke((MethodInvoker)delegate
                {
                    updateBusy = false;
                    if (Updater.IsNewer(r))
                    {
                        pendingRelease = r;
                        if (manual) AskAndUpdate(r);
                        else tray.ShowBalloonTip(8000, "ProgramTabs " + r.Version.ToString(3) + " is beschikbaar",
                            "Klik hier om bij te werken (je hebt " + Updater.CurrentText + ").", ToolTipIcon.Info);
                    }
                    else if (manual)
                    {
                        MessageBox.Show(r == null
                            ? "Kon niet controleren op updates (geen internet of GitHub niet bereikbaar)."
                            : "Je hebt de nieuwste versie (" + Updater.CurrentText + ").", "ProgramTabs");
                    }
                });
            });
        }

        void AskAndUpdate(Updater.Release r)
        {
            string notes = string.IsNullOrEmpty(r.Notes) ? "" : "\n\nWat is er nieuw:\n" + (r.Notes.Length > 600 ? r.Notes.Substring(0, 600) + "…" : r.Notes);
            if (MessageBox.Show("Versie " + r.Version.ToString(3) + " is beschikbaar (je hebt " + Updater.CurrentText + ")." + notes +
                    "\n\nNu bijwerken? ProgramTabs herstart even; je vensters blijven gewoon open.",
                    "ProgramTabs bijwerken", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            updateBusy = true;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                string file = null; Exception error = null;
                try { file = Updater.Download(r); } catch (Exception ex) { error = ex; }
                invoker.BeginInvoke((MethodInvoker)delegate
                {
                    updateBusy = false;
                    if (error != null) { MessageBox.Show("Bijwerken mislukt: " + error.Message, "ProgramTabs"); return; }
                    try
                    {
                        Updater.ApplyAndRestart(file);
                        ExitThread();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Bijwerken mislukt: " + ex.Message +
                            "\n\nStaat ProgramTabs in een map waar je niet mag schrijven? Gebruik dan 'Installeren voor deze gebruiker'.", "ProgramTabs");
                    }
                });
            });
        }

        void Install()
        {
            if (MessageBox.Show("ProgramTabs wordt gekopieerd naar\n" + Updater.InstallDir +
                    "\nen start voortaan automatisch met Windows. Updates werken dan zonder admin-rechten.\n\nDoorgaan?",
                    "ProgramTabs installeren", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            try
            {
                Updater.InstallAndRestart();
                ExitThread();
            }
            catch (Exception ex) { MessageBox.Show("Installeren mislukt: " + ex.Message, "ProgramTabs"); }
        }

        static Icon MakeTrayIcon()
        {
            using (var bmp = new Bitmap(32, 32))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    using (var b1 = new SolidBrush(Color.FromArgb(0x4C, 0xC2, 0xFF)))
                    using (var b2 = new SolidBrush(Color.FromArgb(0x9A, 0xA3, 0xAF)))
                    using (var b3 = new SolidBrush(Color.White))
                    {
                        g.FillRectangle(b1, 2, 4, 12, 7);
                        g.FillRectangle(b2, 16, 6, 12, 5);
                        g.FillRectangle(b1, 2, 11, 28, 18);
                        g.FillRectangle(b3, 5, 14, 22, 12);
                    }
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }

        protected override void ExitThreadCore()
        {
            foreach (IntPtr h in hooks) if (h != IntPtr.Zero) Native.UnhookWinEvent(h);
            hooks.Clear();
            hotkeys.Dispose();
            foreach (TabGroup g in groups.ToArray()) DisposeGroup(g);
            tray.Visible = false;
            tray.Dispose();
            Log.Write("exit");
            base.ExitThreadCore();
        }

        // ================= queries =================

        public Bitmap GetIcon(IntPtr h)
        {
            Bitmap b;
            if (!icons.TryGetValue(h, out b))
            {
                b = WindowUtil.GetIcon(h);
                icons[h] = b;
            }
            return b;
        }

        public TabGroup FindGroup(IntPtr h)
        {
            foreach (TabGroup g in groups) if (g.PlacedIn.ContainsKey(h)) return g;
            return null;
        }

        /// <summary>True when the view shows a live mirror because its tab is physically in the other view.</summary>
        public bool IsMirrored(View v)
        {
            View p;
            return v.Active != IntPtr.Zero && v.Group.PlacedIn.TryGetValue(v.Active, out p) && p != v;
        }

        /// <summary>True when h is the active tab of another view of the same group.</summary>
        public bool IsActiveElsewhere(View v, IntPtr h)
        {
            foreach (View o in v.Group.Views) if (o != v && o.Active == h) return true;
            return false;
        }

        public View ViewAtStrip(POINT p)
        {
            foreach (TabGroup g in groups)
                foreach (View v in g.Views)
                {
                    RECT r = v.StripRect;
                    int extra = (int)(8 * v.Scale);
                    if (v.StripVisible && p.X >= r.Left && p.X < r.Right && p.Y >= r.Top - extra && p.Y < r.Bottom + extra)
                        return v;
                }
            return null;
        }

        bool Alive(View v) { return v != null && groups.Contains(v.Group) && v.Group.Views.Contains(v); }

        // ================= pinned tabs =================

        public bool IsPinned(IntPtr h) { return pinned.ContainsKey(h); }

        // ================= tab names / icon-only =================

        // Three levels, most specific first:
        //   1. pinned tab   -> stored in its PinRule (pinned.txt)
        //   2. this window  -> in memory, until the window closes
        //   3. this program -> labels.txt, applies to every window of that program, also in the future
        readonly Dictionary<IntPtr, TabLabel> labels = new Dictionary<IntPtr, TabLabel>();
        readonly Dictionary<string, TabLabel> programLabels = LabelStore.Load();

        public string ProcessOf(TabItem it)
        {
            return it.Pin != null ? it.Pin.ProcessName : WindowUtil.GetProcessName(WindowUtil.GetPid(it.Hwnd));
        }

        public string FriendlyNameOf(TabItem it)
        {
            string path = it.Pin != null ? it.Pin.ExePath : WindowUtil.GetProcessPath(WindowUtil.GetPid(it.Hwnd));
            return WindowUtil.GetFriendlyName(ProcessOf(it), path);
        }

        /// <summary>The label for this tab only (pin or window level), or null.</summary>
        TabLabel OwnLabel(TabItem it)
        {
            if (it.Pin != null)
                return it.Pin.CustomName != null || it.Pin.IconOnly ? new TabLabel { Name = it.Pin.CustomName, IconOnly = it.Pin.IconOnly } : null;
            TabLabel l;
            return labels.TryGetValue(it.Hwnd, out l) ? l : null;
        }

        public TabLabel GetProgramLabel(TabItem it)
        {
            TabLabel l;
            return programLabels.TryGetValue(ProcessOf(it), out l) ? l : null;
        }

        public bool HasOwnLabel(TabItem it) { return OwnLabel(it) != null; }

        TabLabel EffectiveLabel(TabItem it) { return OwnLabel(it) ?? GetProgramLabel(it); }

        /// <summary>User-chosen tab name, or null for the default (window title).</summary>
        public string GetCustomName(TabItem it)
        {
            TabLabel l = EffectiveLabel(it);
            return l != null ? l.Name : null;
        }

        public bool IsIconOnly(TabItem it)
        {
            TabLabel l = EffectiveLabel(it);
            return l != null && l.IconOnly;
        }

        /// <summary>Name/icon-only for every window of this tab's program, now and in the future.
        /// Clears this tab's own label so the program label becomes visible.</summary>
        public void SetProgramLabel(TabItem it, string name, bool iconOnly)
        {
            if (name != null && name.Trim().Length == 0) name = null;
            string proc = ProcessOf(it);
            if (proc.Length == 0) return;
            if (name == null && !iconOnly) programLabels.Remove(proc);
            else programLabels[proc] = new TabLabel { Name = name, IconOnly = iconOnly };
            LabelStore.Save(programLabels);
            SetLabel(it, null, false);
        }

        /// <summary>Sets name (null = default) and icon-only for this tab only. Persisted for pinned tabs.</summary>
        public void SetLabel(TabItem it, string name, bool iconOnly)
        {
            if (name != null && name.Trim().Length == 0) name = null;
            if (it.Pin != null)
            {
                it.Pin.CustomName = name;
                it.Pin.IconOnly = iconOnly;
                PinStore.Save(pins);
            }
            else if (name == null && !iconOnly) labels.Remove(it.Hwnd);
            else labels[it.Hwnd] = new TabLabel { Name = name, IconOnly = iconOnly };
            foreach (TabGroup g in groups) RefreshViews(g, null);
        }

        /// <summary>The tabs a strip shows: pinned ones first (open or closed "ghosts"), then the rest.
        /// Closed pinned tabs are shown in the first group only.</summary>
        public List<TabItem> GetTabItems(TabGroup g)
        {
            var items = new List<TabItem>();
            bool primary = groups.Count > 0 && groups[0] == g;
            foreach (PinRule r in pins)
            {
                IntPtr open = WindowForRule(r);
                if (open != IntPtr.Zero) { if (g.PlacedIn.ContainsKey(open)) items.Add(new TabItem { Hwnd = open, Pin = r }); }
                else if (primary) items.Add(new TabItem { Pin = r });
            }
            foreach (IntPtr h in g.Windows)
                if (!pinned.ContainsKey(h)) items.Add(new TabItem { Hwnd = h });
            return items;
        }

        IntPtr WindowForRule(PinRule r)
        {
            foreach (KeyValuePair<IntPtr, PinRule> kv in pinned) if (kv.Value == r) return kv.Key;
            return IntPtr.Zero;
        }

        PinRule UnclaimedRuleFor(IntPtr h)
        {
            foreach (PinRule r in pins)
                if (WindowForRule(r) == IntPtr.Zero && r.Matches(h)) return r;
            return null;
        }

        /// <summary>Marks h as pinned if it matches a remembered pin that has no open window yet.</summary>
        void ClaimPin(IntPtr h)
        {
            if (pinned.ContainsKey(h)) return;
            PinRule r = UnclaimedRuleFor(h);
            if (r != null) { pinned[h] = r; Log.Write("pin claimed: " + r.Display); }
        }

        public void Pin(IntPtr h)
        {
            TabGroup g = FindGroup(h);
            if (g == null || pinned.ContainsKey(h)) return;
            PinRule r = PinRule.FromWindow(h);
            pins.RemoveAll(delegate(PinRule o)
            {
                return WindowForRule(o) == IntPtr.Zero &&
                       string.Equals(o.ProcessName, r.ProcessName, StringComparison.OrdinalIgnoreCase) &&
                       string.Equals(o.TitleKey, r.TitleKey, StringComparison.OrdinalIgnoreCase);
            });
            TabLabel l;
            if (labels.TryGetValue(h, out l)) { r.CustomName = l.Name; r.IconOnly = l.IconOnly; labels.Remove(h); }
            pins.Add(r);
            pinned[h] = r;
            PinStore.Save(pins);
            SortPinnedFirst(g);
            RefreshViews(g, null);
        }

        /// <summary>The open, non-pinned windows of a program in this group (pinned tabs are protected).</summary>
        public List<IntPtr> WindowsOfProgram(TabGroup g, string process)
        {
            var list = new List<IntPtr>();
            if (string.IsNullOrEmpty(process)) return list;
            foreach (IntPtr h in g.Windows)
                if (!pinned.ContainsKey(h) &&
                    string.Equals(WindowUtil.GetProcessName(WindowUtil.GetPid(h)), process, StringComparison.OrdinalIgnoreCase))
                    list.Add(h);
            return list;
        }

        /// <summary>Asks every non-pinned window of the program to close, like its own close button
        /// (so the program still asks about unsaved work).</summary>
        public void CloseAllOfProgram(TabGroup g, string process)
        {
            List<IntPtr> wins = WindowsOfProgram(g, process);
            Log.Write("close all of " + process + ": " + wins.Count);
            foreach (IntPtr h in wins) WindowUtil.Close(h);
        }

        public void SetPinCommand(PinRule r, string exe, string args)
        {
            r.ExePath = exe;
            r.Args = args;
            PinStore.Save(pins);
        }

        public void Unpin(PinRule r)
        {
            pins.Remove(r);
            IntPtr h = WindowForRule(r);
            if (h != IntPtr.Zero)
            {
                pinned.Remove(h);
                if (r.CustomName != null || r.IconOnly) labels[h] = new TabLabel { Name = r.CustomName, IconOnly = r.IconOnly };
            }
            PinStore.Save(pins);
            foreach (TabGroup g in groups) RefreshViews(g, null);
        }

        /// <summary>Starts the program of a closed pinned tab; its new window lands in view v.</summary>
        public void Reopen(View v, PinRule r)
        {
            // Store apps (WhatsApp, Claude, …) can't be started from their exe: use their app ID.
            if (PackagedApps.IsPackagedPath(r.ExePath))
            {
                reopenTarget = v;
                try
                {
                    if (PackagedApps.TryStart(r.ExePath)) return;
                }
                catch (Exception ex) { Log.Write("packaged start: " + ex.Message); }
                MessageBox.Show("Deze Store-app kon niet gestart worden. Is ze nog geïnstalleerd?\n" + r.ExePath, "ProgramTabs");
                return;
            }
            if (r.ExePath.Length == 0 || !System.IO.File.Exists(r.ExePath))
            {
                MessageBox.Show("Het programma voor deze vastgezette tab is niet gevonden:\n" + r.ExePath,
                    "ProgramTabs");
                return;
            }
            reopenTarget = v;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(r.ExePath, r.Args) { UseShellExecute = true });
                Log.Write("reopen " + r.Display);
            }
            catch (Exception ex) { MessageBox.Show("Openen mislukt: " + ex.Message, "ProgramTabs"); }
        }

        public Bitmap GetExeIcon(string path)
        {
            Bitmap b;
            if (exeIcons.TryGetValue(path, out b)) return b;
            b = null;
            try
            {
                if (System.IO.File.Exists(path))
                    using (Icon ic = Icon.ExtractAssociatedIcon(path)) if (ic != null) b = ic.ToBitmap();
            }
            catch (Exception) { }
            exeIcons[path] = b;
            return b;
        }

        /// <summary>Keeps g.Windows ordered: pinned windows (in pin order) first.</summary>
        void SortPinnedFirst(TabGroup g)
        {
            var first = new List<IntPtr>();
            foreach (PinRule r in pins)
            {
                IntPtr h = WindowForRule(r);
                if (h != IntPtr.Zero && g.PlacedIn.ContainsKey(h)) first.Add(h);
            }
            var rest = new List<IntPtr>();
            foreach (IntPtr h in g.Windows) if (!first.Contains(h)) rest.Add(h);
            g.Windows.Clear();
            g.Windows.AddRange(first);
            g.Windows.AddRange(rest);
        }

        void PinnedRefusal()
        {
            tray.ShowBalloonTip(3000, "ProgramTabs",
                "Deze tab is vastgezet. Rechtsklik op de tab → 'Niet meer vastzetten' om hem los te maken.", ToolTipIcon.Info);
        }

        // ================= grouping =================

        /// <summary>Groups every tabbable window: one group per monitor, filling the work area.</summary>
        public void GroupAll()
        {
            List<IntPtr> wins = WindowUtil.EnumTabbable(); // z-order: top first
            var perMonitor = new Dictionary<IntPtr, View>();
            foreach (TabGroup g in groups)
            {
                IntPtr m = WindowUtil.GetMonitorHandle(g.Views[0].Bounds);
                if (!perMonitor.ContainsKey(m)) perMonitor[m] = g.Views[0];
            }
            var touched = new List<View>();
            foreach (IntPtr h in wins)
            {
                if (FindGroup(h) != null) continue;
                userDetached.Remove(h);
                RECT f = WindowUtil.GetRestoredRect(h);
                IntPtr mon = WindowUtil.GetMonitorHandle(f);
                View v;
                if (!perMonitor.TryGetValue(mon, out v))
                {
                    v = NewGroup(WorkAreaBounds(f));
                    perMonitor[mon] = v;
                }
                v.Group.Windows.Add(h);
                v.Group.PlacedIn[h] = v;
                ClaimPin(h);
                if (v.Active == IntPtr.Zero) v.Active = h;
                if (!touched.Contains(v)) touched.Add(v);
            }
            foreach (View v in touched)
            {
                SortPinnedFirst(v.Group);
                foreach (IntPtr h in v.Group.Windows) if (v.Group.PlacedIn[h] == v) Place(v, h, h == v.Active);
                RefreshViews(v.Group, v);
            }
            autoAdd = true;
            if (autoAddItem != null) autoAddItem.Checked = true;
            Log.Write("GroupAll: " + wins.Count + " windows, " + groups.Count + " groups");
        }

        public void UngroupAll()
        {
            foreach (TabGroup g in groups.ToArray()) Ungroup(g);
            autoAdd = false;
            autoAddItem.Checked = false;
        }

        public void Ungroup(TabGroup g)
        {
            // Cascade the windows slightly so they don't stay hidden behind each other.
            int i = 0;
            foreach (IntPtr h in g.Windows.ToArray())
            {
                userDetached.Add(h);
                View v = g.PlacedIn[h];
                if (h == v.Active) continue;
                i++;
                int step = (int)(32 * v.Scale);
                RECT b = v.Bounds;
                int dx = step * (i % 8);
                var r = new RECT(b.Left + dx, b.Top + dx, b.Right + dx - step * 4, b.Bottom + dx - step * 4);
                if (r.Width < 300) r.Right = r.Left + 300;
                if (r.Height < 200) r.Bottom = r.Top + 200;
                if (!Native.IsIconic(h)) WindowUtil.SetFrame(h, r);
            }
            DisposeGroup(g);
        }

        // ================= split view =================

        public void ToggleSplit(TabGroup g)
        {
            if (!groups.Contains(g)) return;
            if (!g.IsSplit)
            {
                View v0 = g.Views[0];
                RECT b = v0.Bounds;
                int mid = b.Left + b.Width / 2;
                v0.RestoreBounds = null;
                v0.Bounds = new RECT(b.Left, b.Top, mid, b.Bottom);
                View v1 = NewView(g, new RECT(mid, b.Top, b.Right, b.Bottom));
                // Right half starts with the next tab, or mirrors the same one if there is only one.
                IntPtr second = v0.Active;
                if (g.Windows.Count > 1)
                {
                    int i = g.Windows.IndexOf(v0.Active);
                    second = g.Windows[(i + 1) % g.Windows.Count];
                    g.PlacedIn[second] = v1;
                }
                v1.Active = second;
                SyncView(v0, true);
                SyncView(v1, true);
                if (second != v0.Active) WindowUtil.Activate(second);
                RefreshViews(g, v1);
            }
            else
            {
                View v0 = g.Views[0], v1 = g.Views[1];
                // Keep the view that has the focus as the remaining one's active tab.
                IntPtr fg = Native.GetForegroundWindow();
                IntPtr keep = g.PlacedIn.ContainsKey(fg) ? fg : v0.Active;
                RECT a = v0.Bounds, c = v1.Bounds;
                foreach (IntPtr h in g.Windows) g.PlacedIn[h] = v0;
                DisposeView(v1);
                g.Views.Remove(v1);
                v0.Bounds = new RECT(Math.Min(a.Left, c.Left), Math.Min(a.Top, c.Top), Math.Max(a.Right, c.Right), Math.Max(a.Bottom, c.Bottom));
                v0.Active = keep;
                if (lastView == v1) lastView = v0;
                SyncView(v0, true);
                RefreshViews(g, v0);
            }
            Log.Write("split=" + g.IsSplit);
        }

        void ToggleSplitForeground()
        {
            TabGroup g = FindGroup(Native.GetForegroundWindow());
            if (g == null && Alive(lastView)) g = lastView.Group;
            if (g != null) ToggleSplit(g);
        }

        // ================= tab operations (used by strips / hotkeys) =================

        /// <summary>Shows window h in view v. If h was physically in the other view it moves here,
        /// and the other view keeps showing it as a live mirror.</summary>
        public void ActivateInView(View v, IntPtr h, bool foreground)
        {
            TabGroup g = v.Group;
            if (!Alive(v) || !g.PlacedIn.ContainsKey(h)) return;
            g.PlacedIn[h] = v;
            v.Active = h;
            lastView = v;
            Place(v, h, true);
            if (foreground) WindowUtil.Activate(h);
            RefreshViews(g, v);
        }

        public void Cycle(View v, int dir)
        {
            List<IntPtr> w = v.Group.Windows;
            if (w.Count < 2) return;
            int i = w.IndexOf(v.Active);
            i = (i + dir + w.Count) % w.Count;
            ActivateInView(v, w[i], true);
        }

        /// <summary>Moves tab h to the position of tab target. Pinned tabs keep their place.</summary>
        public void ReorderTo(TabGroup g, IntPtr h, IntPtr target)
        {
            if (h == target || pinned.ContainsKey(h) || pinned.ContainsKey(target)) return;
            int from = g.Windows.IndexOf(h), to = g.Windows.IndexOf(target);
            if (from < 0 || to < 0) return;
            g.Windows.RemoveAt(from);
            g.Windows.Insert(to, h);
            foreach (View v in g.Views) v.Strip.Invalidate();
        }

        public void Detach(View v, IntPtr h, POINT? drop)
        {
            if (pinned.ContainsKey(h)) { PinnedRefusal(); return; }
            float s = v.Scale;
            RemoveFromGroup(v.Group, h);
            userDetached.Add(h);
            RECT f = WindowUtil.GetRestoredRect(h);
            int w = f.Width, ht = f.Height;
            RECT r;
            if (drop.HasValue)
            {
                int x = drop.Value.X - Math.Min(w / 2, (int)(120 * s)), y = drop.Value.Y - (int)(12 * s);
                r = new RECT(x, y, x + w, y + ht);
            }
            else
            {
                int d = (int)(40 * s);
                r = new RECT(f.Left + d, f.Top + d, f.Left + d + Math.Max(300, w - 2 * d), f.Top + d + Math.Max(200, ht - 2 * d));
            }
            if (Native.IsIconic(h)) Native.ShowWindowAsync(h, Native.SW_RESTORE);
            WindowUtil.SetFrame(h, r);
            WindowUtil.Activate(h);
        }

        /// <summary>Drop of a tab or window onto a view's strip.</summary>
        public void MoveToView(IntPtr h, View target)
        {
            TabGroup from = FindGroup(h);
            if (from == target.Group) { ActivateInView(target, h, true); return; }
            if (from != null) RemoveFromGroup(from, h);
            AddToGroup(target, h, true);
        }

        public void ToggleMaximize(View v)
        {
            if (v.Group.IsSplit) return; // the halves are defined by the split
            RECT work = WorkAreaBounds(v.Bounds);
            if (v.Bounds.NearlyEquals(work, 2) && v.RestoreBounds.HasValue)
                v.Bounds = v.RestoreBounds.Value;
            else
            {
                v.RestoreBounds = v.Bounds;
                v.Bounds = work;
            }
            SyncView(v, true);
        }

        // ================= hotkeys =================

        void ToggleForeground()
        {
            IntPtr fg = Native.GetForegroundWindow();
            TabGroup g = FindGroup(fg);
            if (g != null) { Detach(g.PlacedIn[fg], fg, null); return; }
            if (!WindowUtil.IsTabbable(fg)) return;
            userDetached.Remove(fg);
            if (Alive(lastView)) AddToGroup(lastView, fg, true);
            else
            {
                RECT f = WindowUtil.GetFrame(fg);
                View nv = NewGroup(Native.IsZoomed(fg) ? WorkAreaBounds(f) : Normalize(f));
                AddToGroup(nv, fg, true);
            }
        }

        void CycleForeground(int dir)
        {
            IntPtr fg = Native.GetForegroundWindow();
            TabGroup g = FindGroup(fg);
            View v = g != null ? g.PlacedIn[fg] : lastView;
            if (Alive(v)) Cycle(v, dir);
        }

        // ================= group / view internals =================

        View NewGroup(RECT bounds)
        {
            var g = new TabGroup();
            groups.Add(g);
            View v = NewView(g, bounds);
            lastView = v;
            return v;
        }

        View NewView(TabGroup g, RECT bounds)
        {
            var v = new View { Group = g, Bounds = bounds, Scale = WindowUtil.GetScale(bounds) };
            v.Strip = new TabStrip(this, v);
            v.Mirror = new MirrorForm(delegate { if (Alive(v) && v.Active != IntPtr.Zero) ActivateInView(v, v.Active, true); });
            v.SyncTimer = new Timer { Interval = 250 };
            v.SyncTimer.Tick += delegate
            {
                if (v.Dragging) return;
                v.SyncTimer.Stop();
                SyncView(v, false);
            };
            g.Views.Add(v);
            return v;
        }

        void DisposeView(View v)
        {
            if (lastView == v) lastView = null;
            if (highlighted == v) highlighted = null;
            v.SyncTimer.Stop();
            v.SyncTimer.Dispose();
            v.Strip.HideStrip();
            v.Strip.Dispose();
            v.Mirror.HideMirror();
            v.Mirror.Dispose();
        }

        void DisposeGroup(TabGroup g)
        {
            groups.Remove(g);
            foreach (View v in g.Views) DisposeView(v);
            g.Views.Clear();
            g.Windows.Clear();
            g.PlacedIn.Clear();
        }

        void AddToGroup(View v, IntPtr h, bool activate)
        {
            TabGroup g = v.Group;
            if (g.PlacedIn.ContainsKey(h)) { if (activate) ActivateInView(v, h, true); return; }
            TabGroup other = FindGroup(h);
            if (other != null) RemoveFromGroup(other, h);
            pendingNew.Remove(h);
            g.Windows.Add(h);
            g.PlacedIn[h] = v;
            ClaimPin(h);
            SortPinnedFirst(g);
            Log.Write("add window -> group of " + g.Windows.Count + (pinned.ContainsKey(h) ? " (pinned)" : ""));
            if (activate || v.Active == IntPtr.Zero) ActivateInView(v, h, activate);
            else { Place(v, h, false); RefreshViews(g, null); }
        }

        void RemoveFromGroup(TabGroup g, IntPtr h)
        {
            int idx = g.Windows.IndexOf(h);
            if (idx < 0) return;
            View physical = g.PlacedIn[h];
            g.Windows.RemoveAt(idx);
            g.PlacedIn.Remove(h);
            Bitmap b;
            if (icons.TryGetValue(h, out b)) { if (b != null) b.Dispose(); icons.Remove(h); }
            pinned.Remove(h); // the pin rule stays: the tab remains as a closed "ghost" tab
            if (g.Windows.Count == 0)
            {
                // Keep the group alive while it still has closed pinned tabs to show.
                if (pins.Count > 0 && groups[0] == g)
                {
                    foreach (View v in g.Views) v.Active = IntPtr.Zero;
                    RefreshViews(g, null);
                }
                else DisposeGroup(g);
                return;
            }
            // First the view where h physically was, then the ones that mirrored it.
            var order = new List<View>();
            if (physical.Active == h) order.Add(physical);
            foreach (View v in g.Views) if (v.Active == h && !order.Contains(v)) order.Add(v);
            foreach (View v in order)
            {
                v.Active = PickReplacement(v, idx);
                if (v.Active != IntPtr.Zero && g.PlacedIn[v.Active] == v && !Native.IsIconic(v.Active)) Place(v, v.Active, true);
            }
            RefreshViews(g, null);
        }

        /// <summary>Chooses a new active tab for view v after its tab disappeared.</summary>
        IntPtr PickReplacement(View v, int removedIndex)
        {
            TabGroup g = v.Group;
            IntPtr fg = Native.GetForegroundWindow();
            if (g.PlacedIn.ContainsKey(fg) && g.PlacedIn[fg] == v && !IsActiveElsewhere(v, fg)) return fg;
            int n = g.Windows.Count;
            // 1) a window parked in this view, nearest to the removed tab
            for (int k = 0; k < n; k++)
            {
                IntPtr h = g.Windows[(Math.Min(removedIndex, n - 1) + k) % n];
                if (g.PlacedIn[h] == v && !IsActiveElsewhere(v, h)) return h;
            }
            // 2) a window parked elsewhere that isn't shown there: move it here
            foreach (IntPtr h in g.Windows)
                if (!IsActiveElsewhere(v, h)) { g.PlacedIn[h] = v; return h; }
            // 3) nothing free: mirror whatever the other view shows
            return g.Windows[0];
        }

        /// <summary>Puts a window on its view's rectangle (restoring it from max/min where needed).</summary>
        void Place(View v, IntPtr h, bool isActive)
        {
            bool needsFixup = false;
            if (Native.IsIconic(h))
            {
                if (!isActive) return;
                Native.ShowWindowAsync(h, Native.SW_RESTORE);
                needsFixup = true;
            }
            else if (Native.IsZoomed(h))
            {
                Native.ShowWindowAsync(h, isActive ? Native.SW_RESTORE : Native.SW_SHOWNOACTIVATE);
                needsFixup = true;
            }
            WindowUtil.SetFrame(h, v.Bounds);
            if (needsFixup)
            {
                // The restore is asynchronous and changes the invisible-border margins; correct once it settled.
                Later(250, delegate
                {
                    View p;
                    if (v.Group.PlacedIn.TryGetValue(h, out p) && p == v && !Native.IsZoomed(h) && !Native.IsIconic(h))
                    {
                        WindowUtil.SetFrame(h, v.Bounds);
                        RefreshViews(v.Group, null);
                    }
                });
            }
        }

        void SyncView(View v, bool includeActive)
        {
            if (!Alive(v)) return;
            RECT nb = Normalize(v.Bounds);
            if (!nb.NearlyEquals(v.Bounds, 0)) { v.Bounds = nb; includeActive = true; }
            v.Scale = WindowUtil.GetScale(v.Bounds);
            foreach (IntPtr h in v.Group.Windows)
            {
                if (v.Group.PlacedIn[h] != v) continue;
                if (h == v.Active && !includeActive) continue;
                Place(v, h, h == v.Active);
            }
            RefreshViews(v.Group, null);
        }

        /// <summary>Repositions strips and mirrors of all views of a group; raises the given view.</summary>
        void RefreshViews(TabGroup g, View raise)
        {
            if (!groups.Contains(g)) return;
            foreach (View v in g.Views)
            {
                UpdateStrip(v, v == raise);
                UpdateMirror(v, v == raise);
                v.Strip.Invalidate();
            }
        }

        int StripHeight(float scale) { return (int)Math.Round(34 * scale); }

        void UpdateStrip(View v, bool raise)
        {
            IntPtr a = v.Active;
            bool onlyGhosts = a == IntPtr.Zero && v.Group.Windows.Count == 0 && GetTabItems(v.Group).Count > 0;
            bool hide = !onlyGhosts && (a == IntPtr.Zero || !Native.IsWindow(a) || WindowUtil.IsCloaked(a));
            if (!hide && !onlyGhosts && !IsMirrored(v)) hide = Native.IsIconic(a) || !Native.IsWindowVisible(a);
            if (hide)
            {
                if (v.StripVisible) { v.Strip.HideStrip(); v.StripVisible = false; }
                return;
            }
            int h = StripHeight(v.Scale);
            RECT b = v.Bounds;
            v.StripRect = new RECT(b.Left, b.Top - h, b.Right, b.Top);
            v.Strip.PlaceAt(v.StripRect, raise || !v.StripVisible);
            v.StripVisible = true;
        }

        void UpdateMirror(View v, bool raise)
        {
            if (IsMirrored(v) && v.StripVisible && !Native.IsIconic(v.Active))
            {
                v.Mirror.ShowMirror(v.Active, v.Bounds, raise);
                // Windows doesn't let a background process lift its own window above another app's
                // window, but it may push the other app's windows down: put this half's real windows
                // directly below the mirror.
                foreach (IntPtr h in v.Group.Windows)
                    if (v.Group.PlacedIn[h] == v)
                        Native.SetWindowPos(h, v.Mirror.Handle, 0, 0, 0, 0,
                            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_ASYNCWINDOWPOS);
            }
            else
                v.Mirror.HideMirror();
        }

        /// <summary>Makes sure there is room for the strip above the rectangle, within the work area.</summary>
        RECT Normalize(RECT r)
        {
            RECT work = WindowUtil.GetMonitor(r).rcWork;
            int h = StripHeight(WindowUtil.GetScale(r));
            if (r.Top < work.Top + h) { int dy = work.Top + h - r.Top; r.Top += dy; r.Bottom += dy; }
            if (r.Bottom > work.Bottom) r.Bottom = work.Bottom;
            if (r.Height < 120) r.Top = Math.Max(work.Top + h, r.Bottom - 120);
            return r;
        }

        RECT WorkAreaBounds(RECT anyRectOnMonitor)
        {
            RECT work = WindowUtil.GetMonitor(anyRectOnMonitor).rcWork;
            int h = StripHeight(WindowUtil.GetScale(anyRectOnMonitor));
            return new RECT(work.Left, work.Top + h, work.Right, work.Bottom);
        }

        // ================= new windows =================

        void ProcessPending()
        {
            if (pendingNew.Count == 0) return;
            DateTime now = DateTime.Now;
            foreach (IntPtr h in new List<IntPtr>(pendingNew.Keys))
            {
                // Pinned apps (e.g. Outlook) can take a while before their title is final: wait longer.
                double maxAge = pins.Count > 0 ? 15 : 5;
                if ((now - pendingNew[h]).TotalSeconds > maxAge || !Native.IsWindow(h)) { pendingNew.Remove(h); continue; }
                if (FindGroup(h) != null || userDetached.Contains(h)) { pendingNew.Remove(h); continue; }
                if (!WindowUtil.IsTabbable(h)) continue;
                PinRule rule = UnclaimedRuleFor(h);
                if (rule == null && !autoAdd) continue; // its title may still change into a pinned one
                pendingNew.Remove(h);
                RECT nr = WindowUtil.GetRestoredRect(h);
                View target = rule != null && Alive(reopenTarget) ? reopenTarget : null;
                if (target == null && rule != null && groups.Count > 0) target = groups[0].Views[0];
                if (target == null) target = ViewForMonitor(nr);
                if (target == null && Alive(lastView)) target = lastView;
                if (target == null) target = NewGroup(WorkAreaBounds(nr));
                if (rule != null) reopenTarget = null;
                AddToGroup(target, h, rule != null || Native.GetForegroundWindow() == h);
            }
        }

        View ViewForMonitor(RECT r)
        {
            IntPtr m = WindowUtil.GetMonitorHandle(r);
            if (Alive(lastView) && WindowUtil.GetMonitorHandle(lastView.Bounds) == m) return lastView;
            foreach (TabGroup g in groups)
                foreach (View v in g.Views)
                    if (WindowUtil.GetMonitorHandle(v.Bounds) == m) return v;
            return null;
        }

        // ================= WinEvent handling =================

        void OnWinEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            if (hwnd == IntPtr.Zero || idObject != 0 || idChild != 0) return;
            try { HandleEvent(evt, hwnd); }
            catch (Exception ex) { Log.Write("event error: " + ex); }
        }

        void HandleEvent(uint evt, IntPtr hwnd)
        {
            TabGroup g = FindGroup(hwnd);
            View v = g != null ? g.PlacedIn[hwnd] : null;
            switch (evt)
            {
                case Native.EVENT_SYSTEM_FOREGROUND:
                    if (v == null) break;
                    lastView = v;
                    if (v.Active != hwnd)
                    {
                        // Activated from the taskbar / Alt+Tab: it becomes the active tab of its view.
                        v.Active = hwnd;
                        if (!Native.IsIconic(hwnd)) Place(v, hwnd, true);
                    }
                    RefreshViews(g, v);
                    break;

                case Native.EVENT_SYSTEM_MOVESIZESTART:
                    movingWindow = hwnd;
                    if (v != null) v.Dragging = true;
                    break;

                case Native.EVENT_SYSTEM_MOVESIZEEND:
                {
                    movingWindow = IntPtr.Zero;
                    SetHighlight(null);
                    POINT c;
                    Native.GetCursorPos(out c);
                    View target = ViewAtStrip(c);
                    if (v != null)
                    {
                        v.Dragging = false;
                        if (target != null && target != v) { MoveToView(hwnd, target); break; }
                        if (hwnd == v.Active) { v.Bounds = WindowUtil.GetFrame(hwnd); SyncView(v, false); }
                    }
                    else if (target != null && WindowUtil.IsTabbable(hwnd))
                    {
                        userDetached.Remove(hwnd);
                        AddToGroup(target, hwnd, true);
                    }
                    break;
                }

                case Native.EVENT_SYSTEM_MINIMIZESTART:
                    if (v != null && hwnd == v.Active) { v.Strip.HideStrip(); v.StripVisible = false; }
                    if (g != null) { TabGroup gg = g; Later(400, delegate { RefreshViews(gg, null); }); }
                    break;

                case Native.EVENT_SYSTEM_MINIMIZEEND:
                    if (v != null) ActivateInView(v, hwnd, false);
                    break;

                case Native.EVENT_OBJECT_DESTROY:
                    if (g != null) RemoveFromGroup(g, hwnd);
                    if (pinned.Remove(hwnd)) foreach (TabGroup og in groups) RefreshViews(og, null);
                    labels.Remove(hwnd);
                    userDetached.Remove(hwnd);
                    pendingNew.Remove(hwnd);
                    break;

                case Native.EVENT_OBJECT_HIDE:
                    if (g != null && !Native.IsWindowVisible(hwnd)) RemoveFromGroup(g, hwnd);
                    break;

                case Native.EVENT_OBJECT_SHOW:
                    if (g == null && (autoAdd || pins.Count > 0) && !userDetached.Contains(hwnd)) pendingNew[hwnd] = DateTime.Now;
                    break;

                case Native.EVENT_OBJECT_NAMECHANGE:
                    if (g == null) break;
                    // New windows start with a temporary title ("Nieuw tabblad", "Verkenner") and may have
                    // been added as a normal tab already; once the real title matches a closed pinned tab,
                    // it becomes that pinned tab.
                    if (!pinned.ContainsKey(hwnd) && UnclaimedRuleFor(hwnd) != null)
                    {
                        ClaimPin(hwnd);
                        SortPinnedFirst(g);
                        RefreshViews(g, null);
                    }
                    foreach (View o in g.Views) o.Strip.Invalidate();
                    break;

                case Native.EVENT_OBJECT_CLOAKED:
                case Native.EVENT_OBJECT_UNCLOAKED:
                    if (g != null) RefreshViews(g, null);
                    break;

                case Native.EVENT_OBJECT_LOCATIONCHANGE:
                    if (movingWindow == hwnd)
                    {
                        POINT c;
                        Native.GetCursorPos(out c);
                        View t = ViewAtStrip(c);
                        SetHighlight(t != v ? t : null);
                    }
                    if (v == null || hwnd != v.Active || Native.IsIconic(hwnd)) break;
                    if (Native.IsZoomed(hwnd))
                    {
                        if (g.IsSplit) { Place(v, hwnd, true); break; } // stay in its half
                        // The user maximized a tab: maximize the group instead (fill work area below the strip).
                        RECT work = WorkAreaBounds(WindowUtil.GetFrame(hwnd));
                        if (!v.Bounds.NearlyEquals(work, 2)) v.RestoreBounds = v.Bounds;
                        v.Bounds = work;
                        SyncView(v, true);
                        break;
                    }
                    RECT f = WindowUtil.GetFrame(hwnd);
                    if (!f.NearlyEquals(v.Bounds, 0))
                    {
                        v.Bounds = f;
                        UpdateStrip(v, false);
                        UpdateMirror(v, false);
                        v.SyncTimer.Stop();
                        v.SyncTimer.Start();
                    }
                    break;
            }
        }

        void SetHighlight(View t)
        {
            if (highlighted == t) return;
            if (Alive(highlighted)) highlighted.Strip.DropHighlight = false;
            highlighted = t;
            if (t != null)
            {
                t.Strip.DropHighlight = true;
                t.Strip.PlaceAt(t.StripRect, true); // lift above the window being dragged
            }
        }

        static void Later(int ms, Action a)
        {
            var t = new Timer { Interval = Math.Max(1, ms) };
            t.Tick += delegate { t.Stop(); t.Dispose(); try { a(); } catch (Exception ex) { Log.Write("later: " + ex); } };
            t.Start();
        }
    }

    /// <summary>Hidden message-only window that receives global hotkeys.</summary>
    public class HotkeyWindow : NativeWindow, IDisposable
    {
        readonly Dictionary<int, Action> actions = new Dictionary<int, Action>();

        public HotkeyWindow() { CreateHandle(new CreateParams()); }

        public void Register(int id, uint mods, uint vk, Action a)
        {
            if (Native.RegisterHotKey(Handle, id, mods | Native.MOD_NOREPEAT, vk)) actions[id] = a;
            else Log.Write("hotkey " + id + " could not be registered (in use?)");
        }

        protected override void WndProc(ref Message m)
        {
            Action a;
            if (m.Msg == Native.WM_HOTKEY && actions.TryGetValue(m.WParam.ToInt32(), out a))
            {
                try { a(); } catch (Exception ex) { Log.Write("hotkey: " + ex); }
                return;
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            foreach (int id in actions.Keys) Native.UnregisterHotKey(Handle, id);
            DestroyHandle();
        }
    }
}
