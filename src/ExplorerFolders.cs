using System;
using System.Collections.Generic;
using System.Reflection;

namespace ProgramTabs
{
    /// <summary>
    /// File Explorer support for pinned tabs: started without arguments, explorer.exe opens Home instead
    /// of the folder that was pinned. We read the open folder through the Shell's window list
    /// (Shell.Application.Windows, late-bound COM) and reopen exactly that folder.
    /// </summary>
    public static class ExplorerFolders
    {
        // Localised folder titles → shell: names (Dutch and English).
        static readonly Dictionary<string, string> Known = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Downloads", "shell:Downloads" },
            { "Documenten", "shell:Personal" }, { "Documents", "shell:Personal" },
            { "Afbeeldingen", "shell:My Pictures" }, { "Pictures", "shell:My Pictures" },
            { "Bureaublad", "shell:Desktop" }, { "Desktop", "shell:Desktop" },
            { "Muziek", "shell:My Music" }, { "Music", "shell:My Music" },
            { "Video's", "shell:My Video" }, { "Videos", "shell:My Video" },
            { "Deze pc", "shell:MyComputerFolder" }, { "This PC", "shell:MyComputerFolder" },
            { "Prullenbak", "shell:RecycleBinFolder" }, { "Recycle Bin", "shell:RecycleBinFolder" },
            { "OneDrive", "shell:OneDrive" }
        };

        public static bool IsExplorer(string process)
        {
            return string.Equals(process, "explorer", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Start argument (quoted path or shell: name) that reopens the folder shown in this window.</summary>
        public static string ArgsFor(IntPtr hwnd, string titleKey)
        {
            string path = GetFolder(hwnd, titleKey);
            if (!string.IsNullOrEmpty(path))
            {
                if (path.StartsWith("::", StringComparison.Ordinal)) return "shell:" + path;
                return "\"" + path + "\"";
            }
            string shell;
            return Known.TryGetValue(titleKey ?? "", out shell) ? shell : "";
        }

        /// <summary>Folder path of the Explorer window (the tab whose name matches titleKey, if several).</summary>
        public static string GetFolder(IntPtr hwnd, string titleKey)
        {
            try
            {
                Type shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null) return null;
                object shell = Activator.CreateInstance(shellType);
                object windows = Call(shell, "Windows");
                int count = Convert.ToInt32(Get(windows, "Count"));
                string first = null;
                for (int i = 0; i < count; i++)
                {
                    object w = Call(windows, "Item", i);
                    if (w == null) continue;
                    long h;
                    try { h = Convert.ToInt64(Get(w, "HWND")); } catch (Exception) { continue; }
                    if (h != hwnd.ToInt64()) continue;
                    object doc = Get(w, "Document");
                    object folder = doc != null ? Get(doc, "Folder") : null;
                    object self = folder != null ? Get(folder, "Self") : null;
                    string path = self != null ? Get(self, "Path") as string : null;
                    string name = folder != null ? Get(folder, "Title") as string : null;
                    if (string.IsNullOrEmpty(path)) continue;
                    if (first == null) first = path;
                    if (name != null && string.Equals(name, titleKey, StringComparison.OrdinalIgnoreCase)) return path;
                }
                return first;
            }
            catch (Exception ex)
            {
                Log.Write("explorer folder: " + ex.Message);
                return null;
            }
        }

        static object Get(object o, string name)
        {
            return o.GetType().InvokeMember(name, BindingFlags.GetProperty, null, o, null);
        }

        static object Call(object o, string name, params object[] args)
        {
            return o.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, o, args);
        }
    }
}
