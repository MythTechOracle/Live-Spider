using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace SpookyDesktopSpider
{
    // A second small click-through window keeps silk at its anchor while the spider flees.
    internal sealed class WebOverlay : Form
    {
        private const int Surface = 192;
        private PointF lastAnchor;
        private double lastProgress = -1, lastScale, lastBounce;

        public WebOverlay()
        {
            Text = "Spooky Spider Silk";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(Surface, Surface);
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        public void UpdateWeb(EcologyWeb web, double scale, double bounceOffset = 0)
        {
            if (web == null || web.Progress <= 0) { if (Visible) Hide(); return; }
            if (double.IsNaN(bounceOffset) || double.IsInfinity(bounceOffset)) bounceOffset = 0;
            bounceOffset = Math.Max(-8 * scale, Math.Min(8 * scale, bounceOffset));
            if (Math.Abs(lastProgress - web.Progress) < 0.035 && lastAnchor == web.Anchor && lastScale == scale &&
                Math.Abs(lastBounce - bounceOffset) < 0.05 && Visible) return;
            lastAnchor = web.Anchor;
            lastProgress = web.Progress;
            lastScale = scale;
            lastBounce = bounceOffset;
            // Construct the HWND without activation; UpdateLayeredWindow supplies per-pixel alpha.
            IntPtr nativeHandle = Handle;
            using (Bitmap bitmap = WebArt.Draw(Surface, web.Progress, scale))
                Native.SetBitmap(nativeHandle, bitmap, (int)Math.Round(web.Anchor.X) - Surface / 2,
                    (int)Math.Round(web.Anchor.Y + bounceOffset) - Surface / 2);
            if (!Visible) Show();
            Native.ShowWindow(nativeHandle, 4);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0084) { m.Result = new IntPtr(-1); return; }
            if (m.Msg == 0x0021) { m.Result = new IntPtr(3); return; }
            base.WndProc(ref m);
        }
    }

    internal static class WebArt
    {
        public static Bitmap Draw(int size, double progress, double scale)
        {
            Bitmap image = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            List<PointF[]> segments = new List<PointF[]>();
            PointF center = new PointF(size / 2f, size / 2f);
            float radius = (float)(58 * scale);
            for (int spoke = 0; spoke < 8; spoke++)
            {
                double angle = spoke * Math.PI / 4;
                segments.Add(new PointF[] { center, new PointF(center.X + (float)Math.Cos(angle) * radius,
                    center.Y + (float)Math.Sin(angle) * radius) });
            }
            for (int ring = 1; ring <= 4; ring++)
            for (int section = 0; section < 8; section++)
            {
                double a = section * Math.PI / 4, b = (section + 1) * Math.PI / 4;
                float r = radius * ring / 4;
                segments.Add(new PointF[] {
                    new PointF(center.X + (float)Math.Cos(a) * r, center.Y + (float)Math.Sin(a) * r),
                    new PointF(center.X + (float)Math.Cos(b) * r, center.Y + (float)Math.Sin(b) * r) });
            }
            double length = 0;
            foreach (PointF[] segment in segments) length += EscapeMotion.Distance(segment[0], segment[1]);
            double remaining = length * Math.Max(0, Math.Min(1, progress / 24));
            using (Graphics g = Graphics.FromImage(image))
            using (Pen outline = new Pen(Color.FromArgb(80, 34, 30, 30), 2.4f))
            using (Pen silk = new Pen(Color.FromArgb(165, 226, 229, 238), 0.9f))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                foreach (PointF[] segment in segments)
                {
                    if (remaining <= 0) break;
                    double segmentLength = EscapeMotion.Distance(segment[0], segment[1]);
                    float fraction = (float)Math.Min(1, remaining / segmentLength);
                    PointF end = new PointF(segment[0].X + (segment[1].X - segment[0].X) * fraction,
                        segment[0].Y + (segment[1].Y - segment[0].Y) * fraction);
                    g.DrawLine(outline, segment[0], end);
                    g.DrawLine(silk, segment[0], end);
                    remaining -= segmentLength;
                }
            }
            return image;
        }
    }
}
