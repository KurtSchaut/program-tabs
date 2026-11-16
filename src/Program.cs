using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("ProgramTabs")]
[assembly: System.Reflection.AssemblyVersion("0.2.1.0")]
[assembly: System.Reflection.AssemblyFileVersion("0.2.1.0")]
[assembly: System.Reflection.AssemblyProduct("ProgramTabs")]
[assembly: System.Reflection.AssemblyDescription("Tabs voor alle vensters")]

namespace ProgramTabs
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // Physical pixel coordinates everywhere (must happen before any window is created).
            try { Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch (Exception) { }

            bool groupAll = true, split = false, list = false, updated = false, updateNow = false;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i].ToLowerInvariant();
                if (a == "--no-group") groupAll = false;
                else if (a == "--split") split = true;
                else if (a == "--list") list = true;
                else if (a == "--updated") updated = true;
                else if (a == "--update-now") updateNow = true;
                else if (a == "--version") { MessageBox.Show("ProgramTabs " + Updater.CurrentText, "ProgramTabs"); return; }
                else if (a == "--pins" && i + 1 < args.Length) PinStore.OverridePath = args[++i];
                else if (a == "--only" && i + 1 < args.Length)
                    foreach (string p in args[++i].Split(',')) { if (p.Trim().Length > 0) WindowUtil.OnlyProcesses.Add(p.Trim()); }
                else if (a == "--log" && i + 1 < args.Length) Log.Path = args[++i];
                else if (a == "--help" || a == "/?")
                {
                    MessageBox.Show("ProgramTabs [--no-group] [--split] [--only proc1,proc2] [--log bestand] [--list] [--update-now] [--version]", "ProgramTabs");
                    return;
                }
            }

            Updater.CleanupOld();

            if (updateNow)
            {
                // Non-interactive: check, download, verify, swap, restart. Used for testing and scripts.
                Updater.Release r = Updater.GetLatest();
                if (!Updater.IsNewer(r)) { Log.Write("update-now: already up to date (" + Updater.CurrentText + ")"); return; }
                Updater.ApplyAndRestart(Updater.Download(r));
                return;
            }

            if (list)
            {
                // Diagnostics: which windows would become tabs? Changes nothing.
                var sb = new System.Text.StringBuilder();
                foreach (IntPtr h in WindowUtil.EnumTabbable())
                    sb.AppendLine(WindowUtil.GetProcessName(WindowUtil.GetPid(h)) + "\t" +
                        (Native.IsIconic(h) ? "[geminimaliseerd] " : "") + WindowUtil.GetTitle(h));
                if (Log.Path != null) File.WriteAllText(Log.Path, sb.ToString());
                else MessageBox.Show(sb.ToString(), "ProgramTabs – vensters die tabs worden");
                return;
            }

            bool created;
            using (var mutex = new Mutex(true, "ProgramTabs.SingleInstance", out created))
            {
                if (!created)
                {
                    // Right after an update/install the previous version is still closing: wait for it.
                    if (!updated) return;
                    try { if (!mutex.WaitOne(15000)) return; }
                    catch (AbandonedMutexException) { }
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += (s, e) => Log.Write("unhandled: " + e.Exception);
                Application.Run(new Manager(groupAll, split));
            }
        }
    }

    static class Log
    {
        public static string Path;
        public static void Write(string msg)
        {
            if (Path == null) return;
            try { File.AppendAllText(Path, DateTime.Now.ToString("HH:mm:ss.fff ") + msg + Environment.NewLine); }
            catch (Exception) { }
        }
    }
}
