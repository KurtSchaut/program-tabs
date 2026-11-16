using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ProgramTabs
{
    /// <summary>
    /// Shows a live, read-only copy of a window that physically sits in the other half of a split view,
    /// using a DWM thumbnail (the same mechanism as taskbar previews). Clicking it moves the real
    /// window here, much like switching between two "New Window" views in Excel.
    /// </summary>
    public class MirrorForm : Form
    {
        [StructLayout(LayoutKind.Sequential)]
        struct DWM_THUMBNAIL_PROPERTIES
        {
            public int dwFlags;
            public RECT rcDestination;
            public RECT rcSource;
            public byte opacity;
            public int fVisible;
            public int fSourceClientAreaOnly;
        }

        const int DWM_TNP_RECTDESTINATION = 0x1, DWM_TNP_RECTSOURCE = 0x2, DWM_TNP_OPACITY = 0x4,
            DWM_TNP_VISIBLE = 0x8, DWM_TNP_SOURCECLIENTAREAONLY = 0x10;

        [DllImport("dwmapi.dll")] static extern int DwmRegisterThumbnail(IntPtr dest, IntPtr src, out IntPtr thumb);
        [DllImport("dwmapi.dll")] static extern int DwmUnregisterThumbnail(IntPtr thumb);
        [DllImport("dwmapi.dll")] static extern int DwmUpdateThumbnailProperties(IntPtr thumb, ref DWM_THUMBNAIL_PROPERTIES p);

        readonly Action onClick;
        IntPtr thumb, source;
        bool shown;

        public MirrorForm(Action onClick)
        {
            this.onClick = onClick;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(0x20, 0x21, 0x24);
            Text = "ProgramTabs mirror";
            Cursor = Cursors.Hand;
            IntPtr unused = Handle;
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
            if (m.Msg == Native.WM_DPICHANGED) { m.Result = IntPtr.Zero; return; }
            base.WndProc(ref m);
        }

        public void ShowMirror(IntPtr src, RECT bounds, bool raise)
        {
            if (src != source)
            {
                Unregister();
                if (DwmRegisterThumbnail(Handle, src, out thumb) == 0) source = src;
                else thumb = IntPtr.Zero;
            }
            uint flags = Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW | Native.SWP_NOOWNERZORDER;
            if (!raise && shown) flags |= Native.SWP_NOZORDER;
            Native.SetWindowPos(Handle, Native.HWND_TOP, bounds.Left, bounds.Top, bounds.Width, bounds.Height, flags);
            shown = true;
            UpdateThumbnail(bounds);
        }

        void UpdateThumbnail(RECT bounds)
        {
            if (thumb == IntPtr.Zero) return;
            RECT wr;
            Native.GetWindowRect(source, out wr);
            RECT fr = WindowUtil.GetFrame(source);
            // Source rect relative to the window rect: crop away the invisible resize borders.
            var src = new RECT(fr.Left - wr.Left, fr.Top - wr.Top, fr.Right - wr.Left, fr.Bottom - wr.Top);
            int sw = Math.Max(1, src.Width), sh = Math.Max(1, src.Height);
            double scale = Math.Min((double)bounds.Width / sw, (double)bounds.Height / sh);
            if (scale > 1) scale = 1; // never blow up, show at most 1:1
            int dw = (int)(sw * scale), dh = (int)(sh * scale);
            int dx = (bounds.Width - dw) / 2, dy = 0;
            var p = new DWM_THUMBNAIL_PROPERTIES
            {
                dwFlags = DWM_TNP_RECTDESTINATION | DWM_TNP_RECTSOURCE | DWM_TNP_OPACITY | DWM_TNP_VISIBLE | DWM_TNP_SOURCECLIENTAREAONLY,
                rcDestination = new RECT(dx, dy, dx + dw, dy + dh),
                rcSource = src,
                opacity = 255,
                fVisible = 1,
                fSourceClientAreaOnly = 0
            };
            DwmUpdateThumbnailProperties(thumb, ref p);
        }

        public void HideMirror()
        {
            Unregister();
            if (shown) { Native.ShowWindow(Handle, Native.SW_HIDE); shown = false; }
        }

        void Unregister()
        {
            if (thumb != IntPtr.Zero) DwmUnregisterThumbnail(thumb);
            thumb = IntPtr.Zero;
            source = IntPtr.Zero;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && onClick != null) onClick();
        }

        protected override void Dispose(bool disposing)
        {
            Unregister();
            base.Dispose(disposing);
        }
    }
}
