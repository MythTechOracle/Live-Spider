using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace SpookyDesktopSpider
{
    // Visual-only feedback. This window never invokes the underlying control,
    // moves the pointer, or changes focus; every mouse message passes through.
    internal sealed class HoverGlow : Form
    {
        internal const int RequiredExtendedStyles = Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT |
            Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
        private Rectangle? lastTarget;

        public HoverGlow()
        {
            Text = "Halloweb Button Glow";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(PureHoverGlowArt.Padding * 2 + PureHoverGlowArt.MinimumTargetSize,
                PureHoverGlowArt.Padding * 2 + PureHoverGlowArt.MinimumTargetSize);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= RequiredExtendedStyles;
                return parameters;
            }
        }

        public void UpdateTarget(Rectangle? physicalBounds)
        {
            if (IsDisposed || Disposing) return;
            Rectangle surface;
            if (!physicalBounds.HasValue || !PureHoverGlowArt.TrySurface(physicalBounds.Value, out surface))
            {
                Clear();
                return;
            }
            Rectangle target = physicalBounds.Value;
            // The provider already filters controls. Also reject an entirely
            // off-screen rectangle instead of constructing an invisible window.
            if (!Screen.FromRectangle(target).Bounds.IntersectsWith(target))
            {
                Clear();
                return;
            }
            if (lastTarget.HasValue && lastTarget.Value == target && Visible) return;
            using (Bitmap bitmap = PureHoverGlowArt.Draw(target.Width, target.Height))
            {
                ClientSize = bitmap.Size;
                IntPtr nativeHandle = Handle;
                Native.SetBitmap(nativeHandle, bitmap, surface.X, surface.Y);
                if (!Visible) Show();
                Native.ShowWindow(nativeHandle, 4); // SW_SHOWNOACTIVATE
            }
            lastTarget = target;
        }

        public void Clear()
        {
            lastTarget = null;
            if (!IsDisposed && !Disposing && Visible) Hide();
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0084) { message.Result = new IntPtr(-1); return; } // HTTRANSPARENT
            if (message.Msg == 0x0021) { message.Result = new IntPtr(3); return; } // MA_NOACTIVATE
            base.WndProc(ref message);
        }
    }

    // No HWND, UI Automation, screen capture, or input APIs are used by the art
    // and geometry functions, so self-tests can exercise them without a flash.
    internal static class PureHoverGlowArt
    {
        public const int Padding = 6;
        public const int MinimumTargetSize = 8;
        public const int MaximumTargetWidth = 600;
        public const int MaximumTargetHeight = 200;

        public static bool TrySurface(Rectangle target, out Rectangle surface)
        {
            surface = Rectangle.Empty;
            if (target.Width < MinimumTargetSize || target.Height < MinimumTargetSize ||
                target.Width > MaximumTargetWidth || target.Height > MaximumTargetHeight) return false;
            long left = (long)target.X - Padding, top = (long)target.Y - Padding;
            long right = (long)target.X + target.Width + Padding, bottom = (long)target.Y + target.Height + Padding;
            if (left < int.MinValue || top < int.MinValue || right > int.MaxValue || bottom > int.MaxValue) return false;
            surface = new Rectangle((int)left, (int)top, target.Width + Padding * 2, target.Height + Padding * 2);
            return true;
        }

        private static GraphicsPath RoundedRectangle(RectangleF rectangle, float radius)
        {
            float diameter = radius * 2;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static Bitmap Draw(int targetWidth, int targetHeight)
        {
            Rectangle surface;
            if (!TrySurface(new Rectangle(0, 0, targetWidth, targetHeight), out surface))
                throw new ArgumentOutOfRangeException("targetWidth", "Glow target must be between 8x8 and 600x200 pixels.");
            Bitmap bitmap = new Bitmap(surface.Width, surface.Height, PixelFormat.Format32bppArgb);
            RectangleF target = new RectangleF(Padding + 0.5f, Padding + 0.5f, targetWidth - 1, targetHeight - 1);
            float radius = Math.Min(6, Math.Min(target.Width, target.Height) / 3);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (GraphicsPath outline = RoundedRectangle(target, radius))
            using (Pen outerGlow = new Pen(Color.FromArgb(7, 246, 172, 65), 9))
            using (Pen middleGlow = new Pen(Color.FromArgb(9, 246, 172, 65), 6))
            using (Pen innerGlow = new Pen(Color.FromArgb(18, 246, 172, 65), 3.5f))
            using (Pen border = new Pen(Color.FromArgb(104, 246, 172, 65), 1.35f))
            using (SolidBrush fill = new SolidBrush(Color.FromArgb(10, 255, 184, 70)))
            {
                graphics.Clear(Color.Transparent);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                outerGlow.LineJoin = middleGlow.LineJoin = innerGlow.LineJoin = border.LineJoin = LineJoin.Round;
                graphics.DrawPath(outerGlow, outline);
                graphics.DrawPath(middleGlow, outline);
                graphics.DrawPath(innerGlow, outline);
                graphics.FillPath(fill, outline);
                graphics.DrawPath(border, outline);
            }
            return bitmap;
        }
    }
}
