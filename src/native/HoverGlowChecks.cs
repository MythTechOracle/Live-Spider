using System;
using System.Drawing;
using System.Drawing.Imaging;

namespace SpookyDesktopSpider
{
    internal static class HoverGlowChecks
    {
        private static int assertions;

        private static void Require(bool condition, string description)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException("Hover glow check failed: " + description);
        }

        public static int Run()
        {
            assertions = 0;
            Rectangle surface;
            Require(PureHoverGlowArt.TrySurface(new Rectangle(-300, -150, 96, 36), out surface), "negative monitor origin remains usable");
            Require(surface == new Rectangle(-306, -156, 108, 48), "padding preserves physical target alignment");
            Require(!PureHoverGlowArt.TrySurface(Rectangle.Empty, out surface), "empty targets are rejected");
            Require(!PureHoverGlowArt.TrySurface(new Rectangle(0, 0, -1, 30), out surface), "negative width is rejected");
            Require(!PureHoverGlowArt.TrySurface(new Rectangle(0, 0, 30, -1), out surface), "negative height is rejected");
            Require(!PureHoverGlowArt.TrySurface(new Rectangle(0, 0, 7, 30), out surface), "tiny width is rejected");
            Require(!PureHoverGlowArt.TrySurface(new Rectangle(0, 0, 30, 7), out surface), "tiny height is rejected");
            Require(!PureHoverGlowArt.TrySurface(new Rectangle(0, 0, 601, 30), out surface), "large width cannot allocate a desktop-sized layer");
            Require(!PureHoverGlowArt.TrySurface(new Rectangle(0, 0, 30, 201), out surface), "large height is rejected");
            Require(!PureHoverGlowArt.TrySurface(new Rectangle(int.MinValue, 0, 30, 30), out surface), "left-padding overflow is rejected");
            Require(!PureHoverGlowArt.TrySurface(new Rectangle(0, int.MinValue, 30, 30), out surface), "top-padding overflow is rejected");
            Require(!PureHoverGlowArt.TrySurface(new Rectangle(int.MaxValue - 10, 0, 30, 30), out surface), "right-padding overflow is rejected");
            Require(!PureHoverGlowArt.TrySurface(new Rectangle(0, int.MaxValue - 10, 30, 30), out surface), "bottom-padding overflow is rejected");

            foreach (Size target in new Size[] { new Size(8, 8), new Size(96, 36), new Size(600, 200) })
            using (Bitmap bitmap = PureHoverGlowArt.Draw(target.Width, target.Height))
            {
                Require(bitmap.Width == target.Width + 12 && bitmap.Height == target.Height + 12, "bitmap adds only the configured small padding");
                Require(bitmap.Width <= 612 && bitmap.Height <= 212, "maximum overlay allocation remains bounded");
                Require(bitmap.PixelFormat == PixelFormat.Format32bppArgb, "per-pixel transparency is preserved");
                bool transparentEdge = true, amber = true;
                int colored = 0, maximumAlpha = 0;
                for (int y = 0; y < bitmap.Height; y++)
                for (int x = 0; x < bitmap.Width; x++)
                {
                    Color pixel = bitmap.GetPixel(x, y);
                    if (x == 0 || y == 0 || x == bitmap.Width - 1 || y == bitmap.Height - 1)
                        transparentEdge &= pixel.A == 0;
                    if (pixel.A > 0)
                    {
                        colored++;
                        maximumAlpha = Math.Max(maximumAlpha, pixel.A);
                        // At near-zero alpha, GDI+ channel quantization can
                        // dominate the unpremultiplied RGB value. Judge the
                        // visible outline rather than effectively clear pixels.
                        if (pixel.A >= 20) amber &= pixel.R >= pixel.G && pixel.G > pixel.B;
                    }
                }
                Require(transparentEdge, "glow has a fully transparent outer edge instead of a rectangular background");
                Require(colored > 0 && colored < bitmap.Width * bitmap.Height, "border is visible without painting the full layer");
                Require(amber, "visible glow stays in the intended warm amber palette");
                Require(maximumAlpha >= 60 && maximumAlpha <= 180, "outline remains visible and translucent");
                if (target.Width >= 96)
                {
                    Color center = bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);
                    Require(center.A > 0 && center.A <= 20, "soft center fill keeps underlying labels legible");
                    Require(bitmap.GetPixel(PureHoverGlowArt.Padding - 2, bitmap.Height / 2).A > 0,
                        "soft glow extends slightly beyond the target border");
                }
            }

            bool invalidDrawRejected = false;
            try { using (Bitmap invalid = PureHoverGlowArt.Draw(int.MaxValue, 20)) { } }
            catch (ArgumentOutOfRangeException) { invalidDrawRejected = true; }
            Require(invalidDrawRejected, "invalid dimensions fail before image allocation");

            // Test the renderer only: no Form or native handle is created here.
            using (Bitmap first = PureHoverGlowArt.Draw(96, 36))
            using (Bitmap second = PureHoverGlowArt.Draw(96, 36))
            {
                bool identical = true;
                for (int y = 0; y < first.Height; y++)
                for (int x = 0; x < first.Width; x++) identical &= first.GetPixel(x, y) == second.GetPixel(x, y);
                Require(identical, "same target geometry produces the same pure visual effect");
            }
            return assertions;
        }
    }
}
