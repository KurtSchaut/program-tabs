using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows.Automation;

namespace ProgramTabs
{
    /// <summary>
    /// Browser-specific handling for pinned tabs: browsers put the page title in the window title and
    /// reopen to an empty tab when started without arguments, so for them we remember the web address
    /// and recognise the window by its site name (e.g. "Gmail") instead of the changing page title.
    /// </summary>
    public static class Browser
    {
        static readonly HashSet<string> Chromium = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "chrome", "msedge", "brave", "vivaldi", "opera", "chromium" };
        static readonly string[] BrandWords = { "Google Chrome", "Microsoft Edge", "Microsoft​ Edge", "Mozilla Firefox", "Firefox", "Brave", "Vivaldi", "Opera", "Chromium" };
        static readonly string[] Separators = { " - ", " – ", " — ", " | " };
        static readonly Regex Counter = new Regex(@"[\(\[]\s*\d+\+?\s*[\)\]]", RegexOptions.Compiled);

        public static bool IsBrowser(string process)
        {
            return Chromium.Contains(process) || string.Equals(process, "firefox", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Removes unread counters like "(28)" or "[3]" and trims.</summary>
        public static string Normalize(string s)
        {
            return s == null ? "" : Counter.Replace(s, "").Trim();
        }

        /// <summary>Title split on " - " etc., without the browser's own name at the end.</summary>
        public static List<string> Segments(string title, bool stripBrand)
        {
            var parts = new List<string>(title.Split(Separators, StringSplitOptions.RemoveEmptyEntries));
            for (int i = 0; i < parts.Count; i++) parts[i] = parts[i].Trim();
            parts.RemoveAll(delegate(string p) { return p.Length == 0; });
            if (stripBrand)
            {
                while (parts.Count > 1 && IsBrand(parts[parts.Count - 1])) parts.RemoveAt(parts.Count - 1);
            }
            return parts;
        }

        static bool IsBrand(string segment)
        {
            foreach (string b in BrandWords)
                if (segment.IndexOf(b, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        /// <summary>
        /// The most stable part of a page title: the last segment without digits (usually the site name,
        /// e.g. "Inbox (28) - x@y - Gmail" → "Gmail", "Google Agenda - Week van 16 nov" → "Google Agenda").
        /// </summary>
        public static string SiteKey(string title)
        {
            List<string> seg = Segments(title, true);
            for (int i = seg.Count - 1; i >= 0; i--)
            {
                string n = Normalize(seg[i]);
                if (n.Length > 0 && !Regex.IsMatch(n, @"\d") && n.IndexOf('@') < 0) return n;
            }
            return seg.Count > 0 ? Normalize(seg[0]) : "";
        }

        /// <summary>True when one of the title's segments equals the key (counters ignored).</summary>
        public static bool TitleHasKey(string title, string key)
        {
            string k = Normalize(key);
            if (k.Length == 0) return false;
            foreach (string s in Segments(title, true))
                if (string.Equals(Normalize(s), k, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Reads the address bar via UI Automation. Returns a full URL or null.</summary>
        public static string GetUrl(IntPtr hwnd)
        {
            try
            {
                AutomationElement root = AutomationElement.FromHandle(hwnd);
                if (root == null) return null;
                AutomationElement edit = root.FindFirst(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
                if (edit == null) return null;
                object vp;
                if (!edit.TryGetCurrentPattern(ValuePattern.Pattern, out vp)) return null;
                string v = ((ValuePattern)vp).Current.Value;
                if (string.IsNullOrEmpty(v) || v.IndexOf(' ') >= 0) return null; // empty or a search text
                if (v.IndexOf("://", StringComparison.Ordinal) < 0) v = "https://" + v;
                return v;
            }
            catch (Exception ex)
            {
                Log.Write("browser url: " + ex.Message);
                return null;
            }
        }

        /// <summary>Start arguments that open the URL in a new browser window.</summary>
        public static string NewWindowArgs(string process, string url)
        {
            if (string.IsNullOrEmpty(url)) return "";
            string q = "\"" + url.Replace("\"", "%22") + "\"";
            return string.Equals(process, "firefox", StringComparison.OrdinalIgnoreCase) ? "-new-window " + q : "--new-window " + q;
        }
    }
}
