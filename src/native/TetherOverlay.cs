using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace SpookyDesktopSpider
{
    // A small visual-only layer between the two actual physical endpoints.
    // Physics, visibility policy, and lifecycle belong to the host.
    internal sealed class TetherOverlay : Form
    {
        private PointF lastAnchor, lastSpider;
        private double lastScale;
        private bool hasEndpoints;

        public TetherOverlay()
        {
            Text = "Halloweb Hanging Silk";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(9, 9);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT |
                    Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
                return parameters;
            }
        }

        public void UpdateTether(PointF anchor, PointF spider, double scale, bool visible)
        {
            if (IsDisposed || Disposing) return;
            Rectangle surface;
            if (!visible || !PureTetherArt.TrySurface(anchor, spider, scale, out surface)) { Clear(); return; }
            if (hasEndpoints && lastAnchor == anchor && lastSpider == spider && lastScale == scale && Visible) return;
            using (Bitmap bitmap = PureTetherArt.Draw(anchor, spider, scale))
            {
                ClientSize = bitmap.Size;
                IntPtr nativeHandle = Handle;
                Native.SetBitmap(nativeHandle, bitmap, surface.X, surface.Y);
                if (!Visible) Show();
                Native.ShowWindow(nativeHandle, 4); // SW_SHOWNOACTIVATE
            }
            lastAnchor = anchor;
            lastSpider = spider;
            lastScale = scale;
            hasEndpoints = true;
        }

        public void Clear()
        {
            hasEndpoints = false;
            if (!IsDisposed && !Disposing && Visible) Hide();
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0084) { message.Result = new IntPtr(-1); return; } // HTTRANSPARENT
            if (message.Msg == 0x0021) { message.Result = new IntPtr(3); return; } // MA_NOACTIVATE
            base.WndProc(ref message);
        }
    }

    internal static class PureTetherArt
    {
        public const int Padding = 4;
        public const int MaximumSurfaceWidth = 200;
        public const int MaximumSurfaceHeight = 600;
        private const double MaximumCoordinate = 1000000;

        private static bool Finite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        public static bool TrySurface(PointF anchor, PointF spider, double scale, out Rectangle surface)
        {
            surface = Rectangle.Empty;
            if (!Finite(anchor.X) || !Finite(anchor.Y) || !Finite(spider.X) || !Finite(spider.Y) ||
                !Finite(scale) || scale <= 0 || Math.Abs(anchor.X) > MaximumCoordinate || Math.Abs(anchor.Y) > MaximumCoordinate ||
                Math.Abs(spider.X) > MaximumCoordinate || Math.Abs(spider.Y) > MaximumCoordinate) return false;
            double dx = (double)spider.X - anchor.X, dy = (double)spider.Y - anchor.Y;
            if (dx * dx + dy * dy < 1) return false;
            double left = Math.Floor(Math.Min(anchor.X, spider.X)) - Padding;
            double top = Math.Floor(Math.Min(anchor.Y, spider.Y)) - Padding;
            double right = Math.Ceiling(Math.Max(anchor.X, spider.X)) + Padding + 1;
            double bottom = Math.Ceiling(Math.Max(anchor.Y, spider.Y)) + Padding + 1;
            double width = right - left, height = bottom - top;
            if (left < int.MinValue || top < int.MinValue || right > int.MaxValue || bottom > int.MaxValue ||
                width <= 0 || height <= 0 || width > MaximumSurfaceWidth || height > MaximumSurfaceHeight) return false;
            surface = new Rectangle((int)left, (int)top, (int)width, (int)height);
            return true;
        }

        public static Bitmap Draw(PointF anchor, PointF spider, double scale)
        {
            Rectangle surface;
            if (!TrySurface(anchor, spider, scale, out surface))
                throw new ArgumentOutOfRangeException("spider", "Tether endpoints must define a finite, nonempty small surface.");
            Bitmap bitmap = new Bitmap(surface.Width, surface.Height, PixelFormat.Format32bppArgb);
            PointF start = new PointF(anchor.X - surface.X, anchor.Y - surface.Y);
            PointF end = new PointF(spider.X - surface.X, spider.Y - surface.Y);
            double dx = (double)end.X - start.X, dy = (double)end.Y - start.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            float sag = (float)Math.Min(1.5, 0.7 * scale);
            float normalX = (float)(-dy / length) * sag, normalY = (float)(dx / length) * sag;
            PointF first = new PointF(start.X + (float)dx / 3 + normalX, start.Y + (float)dy / 3 + normalY);
            PointF second = new PointF(start.X + (float)dx * 2 / 3 + normalX, start.Y + (float)dy * 2 / 3 + normalY);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (Pen shadow = new Pen(Color.FromArgb(42, 36, 31, 32), 2.3f))
            using (Pen silk = new Pen(Color.FromArgb(172, 218, 222, 229), (float)Math.Max(0.65, Math.Min(1.2, 0.8 * scale))))
            {
                graphics.Clear(Color.Transparent);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                shadow.StartCap = shadow.EndCap = silk.StartCap = silk.EndCap = LineCap.Round;
                graphics.DrawBezier(shadow, start, first, second, end);
                graphics.DrawBezier(silk, start, first, second, end);
            }
            return bitmap;
        }
    }
}
