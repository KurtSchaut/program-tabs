using System;
using System.Drawing;

namespace ProgramTabs
{
    /// <summary>Colors following the Windows light/dark app setting and accent color.</summary>
    public class Theme
    {
        public Color Background, Tab, HoverTab, ActiveTab, Text, ActiveText, Accent, CloseHover, DropHighlight, GhostText;
        static Theme cached;
        static DateTime cachedAt;

        public static Theme Current
        {
            get
            {
                if (cached == null || (DateTime.Now - cachedAt).TotalSeconds > 5) { cached = Load(); cachedAt = DateTime.Now; }
                return cached;
            }
        }

        static Theme Load()
        {
            bool light = false;
            Color accent = Color.FromArgb(0x4C, 0xC2, 0xFF);
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    if (k != null) { object v = k.GetValue("AppsUseLightTheme"); if (v is int) light = (int)v != 0; }
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM"))
                    if (k != null)
                    {
                        object v = k.GetValue("AccentColor");
                        if (v is int)
                        {
                            int abgr = (int)v;
                            accent = Color.FromArgb(abgr & 0xFF, (abgr >> 8) & 0xFF, (abgr >> 16) & 0xFF);
                        }
                    }
            }
            catch (Exception) { }

            var t = new Theme { Accent = accent };
            if (light)
            {
                t.Background = Color.FromArgb(0xE3, 0xE5, 0xE8);
                t.Tab = Color.FromArgb(0xD3, 0xD6, 0xDB);
                t.HoverTab = Color.FromArgb(0xEC, 0xEE, 0xF0);
                t.ActiveTab = Color.FromArgb(0xFF, 0xFF, 0xFF);
                t.Text = Color.FromArgb(0x4A, 0x4F, 0x57);
                t.ActiveText = Color.FromArgb(0x14, 0x16, 0x1A);
                t.GhostText = Color.FromArgb(0x8A, 0x8F, 0x97);
                t.CloseHover = Color.FromArgb(0x30, 0, 0, 0);
            }
            else
            {
                t.Background = Color.FromArgb(0x1C, 0x1D, 0x20);
                t.Tab = Color.FromArgb(0x2A, 0x2B, 0x2F);
                t.HoverTab = Color.FromArgb(0x36, 0x37, 0x3C);
                t.ActiveTab = Color.FromArgb(0x45, 0x46, 0x4C);
                t.Text = Color.FromArgb(0xB4, 0xB7, 0xBE);
                t.ActiveText = Color.FromArgb(0xFF, 0xFF, 0xFF);
                t.GhostText = Color.FromArgb(0x78, 0x7B, 0x82);
                t.CloseHover = Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF);
            }
            t.DropHighlight = Color.FromArgb(
                (t.Background.R + accent.R) / 2, (t.Background.G + accent.G) / 2, (t.Background.B + accent.B) / 2);
            return t;
        }
    }
}
