using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace ProgramTabs
{
    /// <summary>
    /// Self-update from GitHub Releases (public repo, no token needed):
    ///   1. GET api.github.com/repos/{Repo}/releases/latest
    ///   2. if its tag (vX.Y.Z) is newer: download ProgramTabs.exe + ProgramTabs.exe.sha256
    ///   3. verify the SHA-256, rename the running exe to .old (Windows allows that), put the new one
    ///      in its place, start it with --updated and exit. The .old file is removed on the next start.
    /// </summary>
    public static class Updater
    {
        public const string Repo = "KurtSchaut/program-tabs";
        const string ExeAsset = "ProgramTabs.exe";
        const string ShaAsset = "ProgramTabs.exe.sha256";

        public class Release
        {
            public Version Version;
            public string Tag;
            public string ExeUrl;
            public string ShaUrl;
            public string Notes;
        }

        public static Version Current
        {
            get { return Assembly.GetExecutingAssembly().GetName().Version; }
        }

        public static string CurrentText
        {
            get { Version v = Current; return v.Major + "." + v.Minor + "." + v.Build; }
        }

        static string UpdateDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"ProgramTabs\update"); }
        }

        static WebClient Client()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var wc = new WebClient();
            wc.Headers[HttpRequestHeader.UserAgent] = "ProgramTabs-updater";
            return wc;
        }

        /// <summary>The latest release, or null when there is none / no network. Throws nothing.</summary>
        public static Release GetLatest()
        {
            try
            {
                string json;
                using (WebClient wc = Client())
                {
                    wc.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
                    json = wc.DownloadString("https://api.github.com/repos/" + Repo + "/releases/latest");
                }
                var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
                var r = new Release { Tag = d["tag_name"] as string, Notes = d.ContainsKey("body") ? d["body"] as string : "" };
                Version v;
                if (r.Tag == null || !Version.TryParse(r.Tag.TrimStart('v', 'V'), out v)) return null;
                r.Version = v;
                foreach (object o in (IEnumerable)d["assets"])
                {
                    var a = (Dictionary<string, object>)o;
                    string name = a["name"] as string, url = a["browser_download_url"] as string;
                    if (name == ExeAsset) r.ExeUrl = url;
                    else if (name == ShaAsset) r.ShaUrl = url;
                }
                return r.ExeUrl != null ? r : null;
            }
            catch (Exception ex)
            {
                Log.Write("update check: " + ex.Message);
                return null;
            }
        }

        public static bool IsNewer(Release r)
        {
            Version c = Current;
            return r != null && r.Version > new Version(c.Major, c.Minor, Math.Max(0, c.Build));
        }

        /// <summary>Downloads and verifies the new exe. Returns its local path; throws on failure.</summary>
        public static string Download(Release r)
        {
            Directory.CreateDirectory(UpdateDir);
            string target = Path.Combine(UpdateDir, ExeAsset);
            using (WebClient wc = Client()) wc.DownloadFile(r.ExeUrl, target);
            if (r.ShaUrl != null)
            {
                string expected;
                using (WebClient wc = Client()) expected = wc.DownloadString(r.ShaUrl).Trim().Split(' ', '\t', '\r', '\n')[0];
                string actual = Sha256(target);
                if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(target);
                    throw new Exception("De gedownloade update is beschadigd (checksum klopt niet).");
                }
            }
            return target;
        }

        public static string Sha256(string path)
        {
            using (var sha = SHA256.Create())
            using (FileStream fs = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>Swaps the running exe for the new one and starts it. The caller must exit right after.</summary>
        public static void ApplyAndRestart(string newExe)
        {
            string cur = Application.ExecutablePath;
            string old = cur + ".old";
            try { if (File.Exists(old)) File.Delete(old); } catch (Exception) { }
            File.Move(cur, old);
            try { File.Copy(newExe, cur, true); }
            catch
            {
                File.Move(old, cur); // put the working version back
                throw;
            }
            Process.Start(new ProcessStartInfo(cur, "--updated") { UseShellExecute = false });
            Log.Write("updated, restarting");
        }

        /// <summary>Removes the previous version left behind by an update.</summary>
        public static void CleanupOld()
        {
            try
            {
                string old = Application.ExecutablePath + ".old";
                if (File.Exists(old)) File.Delete(old);
            }
            catch (Exception) { }
        }

        // ---------- installing for the current user (no admin rights) ----------

        public static string InstallDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\ProgramTabs"); }
        }

        public static bool IsInstalled
        {
            get { return string.Equals(Path.GetDirectoryName(Application.ExecutablePath).TrimEnd('\\'), InstallDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase); }
        }

        /// <summary>Copies this exe to %LOCALAPPDATA%\Programs\ProgramTabs, enables start with Windows,
        /// and starts the installed copy. The caller must exit right after.</summary>
        public static void InstallAndRestart()
        {
            Directory.CreateDirectory(InstallDir);
            string target = Path.Combine(InstallDir, "ProgramTabs.exe");
            File.Copy(Application.ExecutablePath, target, true);
            SetAutostart(true, target);
            Process.Start(new ProcessStartInfo(target, "--updated") { UseShellExecute = false });
        }

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static bool GetAutostart()
        {
            using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey))
                return k != null && k.GetValue("ProgramTabs") != null;
        }

        public static void SetAutostart(bool on, string exePath)
        {
            using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) k.SetValue("ProgramTabs", "\"" + (exePath ?? Application.ExecutablePath) + "\"");
                else if (k.GetValue("ProgramTabs") != null) k.DeleteValue("ProgramTabs");
            }
        }
    }
}
