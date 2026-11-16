using System;
using System.IO;
using System.Reflection;

namespace ProgramTabs
{
    /// <summary>
    /// Microsoft Store (MSIX) apps such as WhatsApp or Claude live in C:\Program Files\WindowsApps, where
    /// starting the .exe directly gives "Access denied" and the folder name changes with every update.
    /// They are started like the Start menu does: explorer.exe shell:AppsFolder\&lt;AppUserModelID&gt;.
    /// </summary>
    public static class PackagedApps
    {
        public static bool IsPackagedPath(string exePath)
        {
            return !string.IsNullOrEmpty(exePath) &&
                   exePath.IndexOf(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>"…\WindowsApps\Name_1.2.3.0_x64__publisherid\app.exe" → "Name_publisherid".</summary>
        public static string FamilyFromPath(string exePath)
        {
            int i = exePath.IndexOf(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase);
            if (i < 0) return null;
            string rest = exePath.Substring(i + @"\WindowsApps\".Length);
            int slash = rest.IndexOf('\\');
            string full = slash > 0 ? rest.Substring(0, slash) : rest;   // Name_Version_Arch_ResourceId_PublisherId
            string[] parts = full.Split('_');
            if (parts.Length < 3) return null;
            return parts[0] + "_" + parts[parts.Length - 1];
        }

        /// <summary>Finds the app ID (AUMID) of a package family in the Start menu's app list.</summary>
        public static string AumidForFamily(string family, string exeName)
        {
            if (string.IsNullOrEmpty(family)) return null;
            try
            {
                Type shellType = Type.GetTypeFromProgID("Shell.Application");
                object shell = Activator.CreateInstance(shellType);
                object folder = Call(shell, "NameSpace", "shell:AppsFolder");
                if (folder == null) return null;
                object items = Call(folder, "Items");
                int count = Convert.ToInt32(Get(items, "Count"));
                string first = null;
                for (int i = 0; i < count; i++)
                {
                    object it = Call(items, "Item", i);
                    string path = it != null ? Get(it, "Path") as string : null;
                    if (path == null || !path.StartsWith(family + "!", StringComparison.OrdinalIgnoreCase)) continue;
                    if (first == null) first = path;
                    // Prefer the entry whose app id resembles the exe name when a package has several apps.
                    string appId = path.Substring(family.Length + 1);
                    if (!string.IsNullOrEmpty(exeName) && exeName.StartsWith(appId, StringComparison.OrdinalIgnoreCase)) return path;
                }
                return first;
            }
            catch (Exception ex)
            {
                Log.Write("aumid lookup: " + ex.Message);
                return null;
            }
        }

        /// <summary>Starts a Store app by its exe path (version independent). False if no app ID was found.</summary>
        public static bool TryStart(string exePath)
        {
            string aumid = AumidForFamily(FamilyFromPath(exePath), Path.GetFileNameWithoutExtension(exePath));
            if (aumid == null) return false;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
                "shell:AppsFolder\\" + aumid) { UseShellExecute = true });
            Log.Write("started packaged app " + aumid);
            return true;
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
