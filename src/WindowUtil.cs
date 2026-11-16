using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace ProgramTabs
{
    /// <summary>Helpers around foreign top-level windows.</summary>
    public static class WindowUtil
    {
        static readonly uint OwnPid = (uint)Process.GetCurrentProcess().Id;
        public static uint OwnProcessId { get { return OwnPid; } }
        static readonly HashSet<string> ExcludedClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow",
            "XamlExplorerHostIslandWindow", "ForegroundStaging", "MultitaskingViewFrame", "TaskListThumbnailWnd",
            "NotifyIconOverflowWindow", "Shell_InputSwitchTopLevelWindow", "TopLevelWindowForOverflowXamlIsland",
            "#32768", "#32770_tooltip"
        };
        static readonly Dictionary<uint, string> ProcNameCache = new Dictionary<uint, string>();
        static readonly Dictionary<uint, string> ProcPathCache = new Dictionary<uint, string>();

        /// <summary>Optional process-name filter (lower case, no .exe). Empty = all processes.</summary>
        public static HashSet<string> OnlyProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Process names that are never tabbed.</summary>
        public static HashSet<string> ExcludedProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost", "TextInputHost", "LockApp"
        };

        /// <summary>Alt+Tab-like rules: is this a "real" application window that can become a tab?</summary>
        public static bool IsTabbable(IntPtr h)
        {
            if (h == IntPtr.Zero || !Native.IsWindow(h) || !Native.IsWindowVisible(h)) return false;
            if (Native.GetWindow(h, Native.GW_OWNER) != IntPtr.Zero) return false;
            int style = Native.GetWindowLong(h, Native.GWL_STYLE);
            int ex = Native.GetWindowLong(h, Native.GWL_EXSTYLE);
            if ((style & Native.WS_CHILD) != 0) return false;
            if ((ex & Native.WS_EX_TOOLWINDOW) != 0) return false;
            if ((ex & Native.WS_EX_NOACTIVATE) != 0) return false;
            if (IsCloaked(h)) return false;
            if (Native.GetWindowTextLength(h) == 0) return false;
            if (ExcludedClasses.Contains(GetClass(h))) return false;
            uint pid;
            Native.GetWindowThreadProcessId(h, out pid);
            if (pid == OwnPid) return false;
            string proc = GetProcessName(pid);
            if (ExcludedProcesses.Contains(proc)) return false;
            if (OnlyProcesses.Count > 0 && !OnlyProcesses.Contains(proc)) return false;
            // Minimized windows report a tiny frame; judge them by their normal (restored) size.
            RECT r = GetRestoredRect(h);
            if (r.Width < 80 || r.Height < 50) return false;
            return true;
        }

        /// <summary>The visible frame, or for a minimized window the rectangle it will restore to.
        /// Use this to decide size or monitor of a window that may be minimized.</summary>
        public static RECT GetRestoredRect(IntPtr h)
        {
            if (!Native.IsIconic(h)) return GetFrame(h);
            var wp = new WINDOWPLACEMENT();
            wp.length = Marshal.SizeOf(typeof(WINDOWPLACEMENT));
            if (!Native.GetWindowPlacement(h, ref wp)) return GetFrame(h);
            // rcNormalPosition is in work-area coordinates; shift it to screen coordinates.
            RECT n = wp.rcNormalPosition;
            MONITORINFO mi = GetMonitor(n);
            int dx = mi.rcWork.Left - mi.rcMonitor.Left, dy = mi.rcWork.Top - mi.rcMonitor.Top;
            return new RECT(n.Left + dx, n.Top + dy, n.Right + dx, n.Bottom + dy);
        }

        public static bool IsCloaked(IntPtr h)
        {
            int cloaked;
            if (Native.DwmGetWindowAttribute(h, Native.DWMWA_CLOAKED, out cloaked, 4) == 0) return cloaked != 0;
            return false;
        }

        /// <summary>All tabbable windows, in z-order (top first).</summary>
        public static List<IntPtr> EnumTabbable()
        {
            var list = new List<IntPtr>();
            Native.EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                if (IsTabbable(h)) list.Add(h);
                return true;
            }, IntPtr.Zero);
            return list;
        }

        public static string GetTitle(IntPtr h)
        {
            int len = Native.GetWindowTextLength(h);
            if (len <= 0) return "";
            var sb = new StringBuilder(len + 1);
            Native.GetWindowText(h, sb, sb.Capacity);
            return sb.ToString();
        }

        public static string GetClass(IntPtr h)
        {
            var sb = new StringBuilder(256);
            Native.GetClassName(h, sb, sb.Capacity);
            return sb.ToString();
        }

        public static uint GetPid(IntPtr h)
        {
            uint pid;
            Native.GetWindowThreadProcessId(h, out pid);
            return pid;
        }

        public static string GetProcessPath(uint pid)
        {
            string path;
            if (ProcPathCache.TryGetValue(pid, out path)) return path;
            path = "";
            IntPtr hp = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (hp != IntPtr.Zero)
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                if (Native.QueryFullProcessImageName(hp, 0, sb, ref size)) path = sb.ToString();
                Native.CloseHandle(hp);
            }
            ProcPathCache[pid] = path;
            return path;
        }

        static readonly Dictionary<string, string> FriendlyCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Readable program name ("Microsoft Word" instead of "WINWORD"), from the exe's
        /// file description; falls back to the process name.</summary>
        public static string GetFriendlyName(string processName, string exePath)
        {
            string key = processName ?? "";
            string name;
            if (FriendlyCache.TryGetValue(key, out name)) return name;
            name = key;
            try
            {
                if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                {
                    string d = FileVersionInfo.GetVersionInfo(exePath).FileDescription;
                    if (!string.IsNullOrEmpty(d) && d.Trim().Length > 1 && d.Length <= 40) name = d.Trim();
                }
            }
            catch (Exception) { }
            FriendlyCache[key] = name;
            return name;
        }

        public static string GetProcessName(uint pid)
        {
            string name;
            if (ProcNameCache.TryGetValue(pid, out name)) return name;
            string path = GetProcessPath(pid);
            name = path.Length > 0 ? Path.GetFileNameWithoutExtension(path) : "";
            ProcNameCache[pid] = name;
            return name;
        }

        /// <summary>Visible frame (without the invisible resize borders of Win10/11).</summary>
        public static RECT GetFrame(IntPtr h)
        {
            RECT r;
            if (Native.DwmGetWindowAttribute(h, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out r, Marshal.SizeOf(typeof(RECT))) == 0
                && r.Width > 0)
                return r;
            Native.GetWindowRect(h, out r);
            return r;
        }

        /// <summary>Moves a window so that its *visible* frame matches target. Never blocks on hung apps.</summary>
        public static void SetFrame(IntPtr h, RECT target)
        {
            RECT wr;
            if (!Native.GetWindowRect(h, out wr)) return;
            RECT fr = GetFrame(h);
            int l = fr.Left - wr.Left, t = fr.Top - wr.Top, r = wr.Right - fr.Right, b = wr.Bottom - fr.Bottom;
            // Sanity: margins of a normal window are small and non-negative.
            if (l < 0 || l > 40) l = 0; if (t < 0 || t > 40) t = 0;
            if (r < 0 || r > 40) r = 0; if (b < 0 || b > 40) b = 0;
            Native.SetWindowPos(h, IntPtr.Zero, target.Left - l, target.Top - t,
                target.Width + l + r, target.Height + t + b,
                Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER | Native.SWP_ASYNCWINDOWPOS);
        }

        public static MONITORINFO GetMonitor(RECT r)
        {
            IntPtr hm = Native.MonitorFromRect(ref r, Native.MONITOR_DEFAULTTONEAREST);
            var mi = new MONITORINFO();
            mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            Native.GetMonitorInfo(hm, ref mi);
            return mi;
        }

        public static IntPtr GetMonitorHandle(RECT r)
        {
            return Native.MonitorFromRect(ref r, Native.MONITOR_DEFAULTTONEAREST);
        }

        public static float GetScale(RECT r)
        {
            try
            {
                uint dx, dy;
                IntPtr hm = Native.MonitorFromRect(ref r, Native.MONITOR_DEFAULTTONEAREST);
                if (Native.GetDpiForMonitor(hm, 0, out dx, out dy) == 0 && dx > 0) return dx / 96f;
            }
            catch (Exception) { }
            return 1f;
        }

        /// <summary>Brings a window to the foreground, working around the foreground lock where possible.</summary>
        public static void Activate(IntPtr h)
        {
            if (Native.IsIconic(h)) Native.ShowWindowAsync(h, Native.SW_RESTORE);
            if (Native.SetForegroundWindow(h) && Native.GetForegroundWindow() == h) return;
            // Fallback: briefly share the input state of the current foreground thread so Windows
            // accepts the switch (no synthetic keys, so no menus pop open in the foreground app).
            uint unused;
            uint fgThread = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out unused);
            uint me = Native.GetCurrentThreadId();
            bool attached = fgThread != 0 && fgThread != me && Native.AttachThreadInput(me, fgThread, true);
            Native.BringWindowToTop(h);
            Native.SetForegroundWindow(h);
            if (attached) Native.AttachThreadInput(me, fgThread, false);
        }

        public static void Close(IntPtr h)
        {
            Native.PostMessage(h, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }

        /// <summary>Gets a small icon for the window as an owned bitmap, or null.</summary>
        public static Bitmap GetIcon(IntPtr h)
        {
            IntPtr hicon = IntPtr.Zero, res;
            int[] kinds = { Native.ICON_SMALL2, Native.ICON_SMALL, Native.ICON_BIG };
            foreach (int k in kinds)
            {
                if (Native.SendMessageTimeout(h, Native.WM_GETICON, new IntPtr(k), IntPtr.Zero,
                        Native.SMTO_ABORTIFHUNG, 100, out res) != IntPtr.Zero && res != IntPtr.Zero)
                { hicon = res; break; }
            }
            if (hicon == IntPtr.Zero) hicon = Native.GetClassLongPtr(h, Native.GCLP_HICONSM);
            if (hicon == IntPtr.Zero) hicon = Native.GetClassLongPtr(h, Native.GCLP_HICON);
            try
            {
                if (hicon != IntPtr.Zero)
                    using (Icon ic = Icon.FromHandle(hicon)) return ic.ToBitmap();
                string path = GetProcessPath(GetPid(h));
                if (path.Length > 0 && File.Exists(path))
                    using (Icon ic = Icon.ExtractAssociatedIcon(path)) if (ic != null) return ic.ToBitmap();
            }
            catch (Exception) { }
            return null;
        }
    }
}
