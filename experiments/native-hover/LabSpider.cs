using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace HallowebNativeHoverLab
{
    // Standalone lab artwork and window only. No input or application-state APIs.
    internal sealed class LabSpider : Form
    {
        private const int Surface = 80;
        private readonly Bitmap artwork;
        private Point lastCenter;
        private bool hasPosition;

        public LabSpider()
        {
            Text = "Halloweb Native Hover Lab Spider";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(Surface, Surface);
            artwork = DrawSpider();
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= 0x80000 | 0x20 | 0x80 | 0x8000000;
                return parameters;
            }
        }

        public void MoveTo(Point screenCenter)
        {
            if (IsDisposed || Disposing) return;
            if (hasPosition && lastCenter == screenCenter && Visible) return;
            int x = (int)Math.Max(int.MinValue, Math.Min((long)int.MaxValue - Surface, (long)screenCenter.X - Surface / 2));
            int y = (int)Math.Max(int.MinValue, Math.Min((long)int.MaxValue - Surface, (long)screenCenter.Y - Surface / 2));
            IntPtr nativeHandle = Handle;
            LabSpiderPixels.SetBitmap(nativeHandle, artwork, x, y);
            if (!Visible) Show();
            LabSpiderPixels.ShowWindow(nativeHandle, 4); // SW_SHOWNOACTIVATE
            lastCenter = screenCenter;
            hasPosition = true;
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0084) { message.Result = new IntPtr(-1); return; } // HTTRANSPARENT
            if (message.Msg == 0x0021) { message.Result = new IntPtr(3); return; } // MA_NOACTIVATE
            base.WndProc(ref message);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && artwork != null) artwork.Dispose();
            base.Dispose(disposing);
        }

        private static Bitmap DrawSpider()
        {
            Bitmap bitmap = new Bitmap(Surface, Surface, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (Pen edge = new Pen(Color.FromArgb(190, 110, 82, 65), 4))
            using (Pen legs = new Pen(Color.FromArgb(22, 19, 18), 2.5f))
            using (Pen outline = new Pen(Color.FromArgb(105, 77, 65), 1))
            using (LinearGradientBrush body = new LinearGradientBrush(new Point(30, 32), new Point(48, 61), Color.FromArgb(52, 43, 39), Color.Black))
            using (SolidBrush head = new SolidBrush(Color.FromArgb(28, 23, 22)))
            using (SolidBrush red = new SolidBrush(Color.FromArgb(255, 35, 32)))
            {
                graphics.Clear(Color.Transparent);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                edge.StartCap = edge.EndCap = legs.StartCap = legs.EndCap = LineCap.Round;
                for (int side = -1; side <= 1; side += 2)
                for (int leg = 0; leg < 4; leg++)
                {
                    PointF start = new PointF(40 + side * 5, 31 + leg * 4);
                    PointF bend = new PointF(40 + side * (23 + (leg == 1 ? 5 : 0)), 14 + leg * 13);
                    PointF end = new PointF(40 + side * (28 - leg * 2), 19 + leg * 15);
                    PointF[] points = { start, bend, end };
                    graphics.DrawLines(edge, points);
                    graphics.DrawLines(legs, points);
                }
                graphics.FillEllipse(body, 30, 35, 20, 28);
                graphics.DrawEllipse(outline, 30, 35, 20, 28);
                graphics.FillEllipse(head, 32, 25, 16, 17);
                graphics.DrawEllipse(outline, 32, 25, 16, 17);
                graphics.FillEllipse(red, 34, 28, 3, 3);
                graphics.FillEllipse(red, 43, 28, 3, 3);
                graphics.FillPolygon(red, new PointF[] { new PointF(36, 43), new PointF(44, 43), new PointF(39, 48),
                    new PointF(43, 53), new PointF(37, 53), new PointF(41, 48) });
            }
            return bitmap;
        }
    }

    internal static class LabSpiderPixels
    {
        [StructLayout(LayoutKind.Sequential)] private struct POINT
        { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }
        [StructLayout(LayoutKind.Sequential)] private struct SIZE
        { public int Width, Height; public SIZE(int width, int height) { Width = width; Height = height; } }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] private struct BLEND
        { public byte Operation, Flags, Alpha, Format; }
        [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr handle, int show);
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr handle);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr handle, IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateLayeredWindow(IntPtr handle, IntPtr destinationDC,
            ref POINT destination, ref SIZE size, IntPtr sourceDC, ref POINT source, int color, ref BLEND blend, int flags);

        internal static void SetBitmap(IntPtr handle, Bitmap bitmap, int x, int y)
        {
            IntPtr screen = GetDC(IntPtr.Zero), memory = CreateCompatibleDC(screen);
            IntPtr image = IntPtr.Zero, previous = IntPtr.Zero;
            try
            {
                image = bitmap.GetHbitmap(Color.FromArgb(0));
                previous = SelectObject(memory, image);
                POINT destination = new POINT(x, y), source = new POINT(0, 0);
                SIZE size = new SIZE(bitmap.Width, bitmap.Height);
                BLEND blend = new BLEND { Alpha = 255, Format = 1 };
                if (!UpdateLayeredWindow(handle, screen, ref destination, ref size, memory, ref source, 0, ref blend, 2))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }
            finally
            {
                if (previous != IntPtr.Zero) SelectObject(memory, previous);
                if (image != IntPtr.Zero) DeleteObject(image);
                DeleteDC(memory);
                ReleaseDC(IntPtr.Zero, screen);
            }
        }
    }
}
