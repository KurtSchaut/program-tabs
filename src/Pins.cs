using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ProgramTabs
{
    /// <summary>
    /// A pinned tab: remembered across sessions, always shown first, protected against accidental
    /// closing/detaching from the tab bar, and re-openable with one click when its window is gone.
    /// A window matches a rule by process name + "title key" (the part of the title before " - ",
    /// e.g. "Postvak IN" or "Agenda" for Outlook).
    /// </summary>
    public class PinRule
    {
        public string ProcessName = "";
        public string TitleKey = "";
        public string ExePath = "";
        public string Args = "";
        public string CustomName;   // null = use the window title
        public bool IconOnly;        // show only the icon

        public static string KeyOf(string title)
        {
            if (title == null) return "";
            string[] seps = { " - ", " – ", " — " };
            int cut = -1;
            foreach (string s in seps)
            {
                int i = title.IndexOf(s, StringComparison.Ordinal);
                if (i > 0 && (cut < 0 || i < cut)) cut = i;
            }
            return (cut > 0 ? title.Substring(0, cut) : title).Trim();
        }

        /// <summary>Recognition key for a window title: the site name for browsers ("Gmail"),
        /// otherwise the first part of the title. Unread counters such as "(28)" are dropped.</summary>
        public static string KeyFor(string process, string title)
        {
            if (Browser.IsBrowser(process)) return Browser.SiteKey(title ?? "");
            return Browser.Normalize(KeyOf(title));
        }

        public bool Matches(IntPtr h)
        {
            if (!string.Equals(WindowUtil.GetProcessName(WindowUtil.GetPid(h)), ProcessName, StringComparison.OrdinalIgnoreCase))
                return false;
            string title = WindowUtil.GetTitle(h);
            if (Browser.IsBrowser(ProcessName)) return Browser.TitleHasKey(title, TitleKey);
            return string.Equals(Browser.Normalize(KeyOf(title)), Browser.Normalize(TitleKey), StringComparison.OrdinalIgnoreCase);
        }

        public static PinRule FromWindow(IntPtr h)
        {
            uint pid = WindowUtil.GetPid(h);
            string proc = WindowUtil.GetProcessName(pid);
            var r = new PinRule
            {
                ProcessName = proc,
                TitleKey = KeyFor(proc, WindowUtil.GetTitle(h)),
                ExePath = WindowUtil.GetProcessPath(pid)
            };
            if (Browser.IsBrowser(proc))
            {
                // Reopen the same page in its own window instead of an empty tab.
                string url = Browser.GetUrl(h);
                r.Args = Browser.NewWindowArgs(proc, url);
                Log.Write("pin browser: url " + (url != null ? "found" : "not found"));
            }
            else if (ExplorerFolders.IsExplorer(proc))
            {
                // Reopen the same folder instead of Explorer's Home page.
                r.Args = ExplorerFolders.ArgsFor(h, r.TitleKey);
                Log.Write("pin explorer: folder " + (r.Args.Length > 0 ? "found" : "not found"));
            }
            else r.Args = GuessArgs(r);
            return r;
        }

        /// <summary>Start arguments that reopen the same kind of window. Known for classic Outlook.</summary>
        static string GuessArgs(PinRule r)
        {
            if (string.Equals(r.ProcessName, "outlook", StringComparison.OrdinalIgnoreCase))
            {
                string k = r.TitleKey.ToLowerInvariant();
                if (k == "agenda" || k == "calendar" || k == "kalender") return "/select outlook:calendar";
                if (k == "postvak in" || k == "inbox" || k == "postvak-in") return "/select outlook:inbox";
                if (k == "contactpersonen" || k == "contacts") return "/select outlook:contacts";
                if (k == "taken" || k == "tasks") return "/select outlook:tasks";
            }
            return "";
        }

        public string Display { get { return TitleKey.Length > 0 ? TitleKey : ProcessName; } }
    }

    /// <summary>Stores pin rules in %APPDATA%\ProgramTabs\pinned.txt (tab separated, editable by hand).</summary>
    public static class PinStore
    {
        /// <summary>Override with --pins (used for testing).</summary>
        public static string OverridePath;

        public static string FilePath
        {
            get
            {
                if (OverridePath != null) return OverridePath;
                return Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ProgramTabs"), "pinned.txt");
            }
        }

        public static List<PinRule> Load()
        {
            var list = new List<PinRule>();
            try
            {
                if (!File.Exists(FilePath)) return list;
                foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    if (line.Trim().Length == 0 || line.StartsWith("#")) continue;
                    string[] p = line.Split('\t');
                    if (p.Length < 2) continue;
                    list.Add(new PinRule
                    {
                        ProcessName = p[0], TitleKey = p[1],
                        ExePath = p.Length > 2 ? p[2] : "", Args = p.Length > 3 ? p[3] : "",
                        CustomName = p.Length > 4 && p[4].Length > 0 ? p[4] : null,
                        IconOnly = p.Length > 5 && p[5].Trim() == "icoon"
                    });
                }
            }
            catch (Exception ex) { Log.Write("pins load: " + ex.Message); }
            return list;
        }

        public static void Save(List<PinRule> rules)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                var sb = new StringBuilder();
                sb.AppendLine("# ProgramTabs vastgezette tabs: proces<TAB>titel-begin<TAB>programma<TAB>startargumenten<TAB>eigen naam<TAB>icoon");
                foreach (PinRule r in rules)
                    sb.AppendLine(r.ProcessName + "\t" + r.TitleKey + "\t" + r.ExePath + "\t" + r.Args + "\t" +
                        (r.CustomName ?? "").Replace("\t", " ") + "\t" + (r.IconOnly ? "icoon" : ""));
                File.WriteAllText(FilePath, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex) { Log.Write("pins save: " + ex.Message); }
        }
    }

    /// <summary>A user-chosen tab name (null = window title) and/or icon-only display.</summary>
    public class TabLabel
    {
        public string Name;
        public bool IconOnly;
    }

    /// <summary>Per-program tab labels in labels.txt next to pinned.txt: proces&lt;TAB&gt;naam&lt;TAB&gt;icoon.</summary>
    public static class LabelStore
    {
        public static string FilePath
        {
            get { return Path.Combine(Path.GetDirectoryName(PinStore.FilePath), "labels.txt"); }
        }

        public static Dictionary<string, TabLabel> Load()
        {
            var d = new Dictionary<string, TabLabel>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(FilePath)) return d;
                foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    if (line.Trim().Length == 0 || line.StartsWith("#")) continue;
                    string[] p = line.Split('\t');
                    if (p[0].Trim().Length == 0) continue;
                    d[p[0].Trim()] = new TabLabel
                    {
                        Name = p.Length > 1 && p[1].Length > 0 ? p[1] : null,
                        IconOnly = p.Length > 2 && p[2].Trim() == "icoon"
                    };
                }
            }
            catch (Exception ex) { Log.Write("labels load: " + ex.Message); }
            return d;
        }

        public static void Save(Dictionary<string, TabLabel> labels)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                var sb = new StringBuilder();
                sb.AppendLine("# ProgramTabs tabnamen per programma: proces<TAB>eigen naam<TAB>icoon");
                foreach (KeyValuePair<string, TabLabel> kv in labels)
                    sb.AppendLine(kv.Key + "\t" + (kv.Value.Name ?? "").Replace("\t", " ") + "\t" + (kv.Value.IconOnly ? "icoon" : ""));
                File.WriteAllText(FilePath, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex) { Log.Write("labels save: " + ex.Message); }
        }
    }

    /// <summary>One tab in a strip: an open window, or a closed pinned tab ("ghost").</summary>
    public class TabItem
    {
        public IntPtr Hwnd;      // zero for a ghost
        public PinRule Pin;      // non-null when pinned (open or ghost)
        public bool IsGhost { get { return Hwnd == IntPtr.Zero; } }
        public bool IsPinned { get { return Pin != null; } }
    }
}
