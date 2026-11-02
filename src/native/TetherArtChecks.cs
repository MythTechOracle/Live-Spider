using System;
using System.Drawing;
using System.Drawing.Imaging;

namespace SpookyDesktopSpider
{
    internal static class TetherArtChecks
    {
        private static int assertions;

        private static void Require(bool condition, string description)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException("Tether art check failed: " + description);
        }

        public static int Run()
        {
            assertions = 0;
            Rectangle surface;
            PointF negativeAnchor = new PointF(-200.25f, -100.5f), negativeSpider = new PointF(-196.5f, 60.25f);
            Require(PureTetherArt.TrySurface(negativeAnchor, negativeSpider, 1, out surface), "negative monitor coordinates are supported");
            Require(surface.Left < negativeAnchor.X && surface.Top < negativeAnchor.Y && surface.Right > negativeSpider.X && surface.Bottom > negativeSpider.Y,
                "local bounds preserve both actual physical endpoints and padding");
            Require(PureTetherArt.TrySurface(new PointF(0, 0), new PointF(191, 591), 1, out surface) && surface.Size == new Size(200, 600),
                "maximum bounded surface is explicit and finite");
            Require(!PureTetherArt.TrySurface(new PointF(0, 0), new PointF(192, 10), 1, out surface), "oversized width is hidden instead of making a desktop-sized layer");
            Require(!PureTetherArt.TrySurface(new PointF(0, 0), new PointF(0, 592), 1, out surface), "oversized height is rejected");
            Require(!PureTetherArt.TrySurface(new PointF(100, 100), new PointF(100, 100), 1, out surface), "zero-length tether is not rendered");
            Require(!PureTetherArt.TrySurface(new PointF(100, 100), new PointF(100, 100.25f), 1, out surface), "subpixel collapsed tether is hidden");
            Require(!PureTetherArt.TrySurface(new PointF(float.NaN, 0), new PointF(0, 100), 1, out surface), "NaN anchor fails closed");
            Require(!PureTetherArt.TrySurface(new PointF(0, 0), new PointF(float.PositiveInfinity, 100), 1, out surface), "infinite endpoint fails closed");
            Require(!PureTetherArt.TrySurface(new PointF(float.MaxValue, 0), new PointF(float.MaxValue, 100), 1, out surface), "extreme coordinates cannot overflow native placement");
            foreach (double scale in new double[] { double.NaN, double.PositiveInfinity, 0, -1 })
                Require(!PureTetherArt.TrySurface(new PointF(0, 0), new PointF(0, 100), scale, out surface), "invalid scale cannot allocate a layer");

            foreach (double scale in new double[] { 0.75, 1, 1.35, 2 })
            foreach (PointF end in new PointF[] { new PointF(100, 100 + (float)(160 * scale)), new PointF(105, 100 + (float)(240 * scale)),
                new PointF(260, 100), new PointF(190, 210) })
            {
                PointF anchor = new PointF(100, 100);
                Require(PureTetherArt.TrySurface(anchor, end, scale, out surface), "ordinary vertical, swaying, horizontal and diagonal endpoints fit");
                using (Bitmap image = PureTetherArt.Draw(anchor, end, scale))
                {
                    Require(image.Size == surface.Size && image.PixelFormat == PixelFormat.Format32bppArgb,
                        "bitmap uses the exact small local bounds with per-pixel alpha");
                    bool clearEdges = true;
                    int colored = 0;
                    for (int y = 0; y < image.Height; y++)
                    for (int x = 0; x < image.Width; x++)
                    {
                        int alpha = image.GetPixel(x, y).A;
                        if (alpha > 0) colored++;
                        if (x == 0 || y == 0 || x == image.Width - 1 || y == image.Height - 1) clearEdges &= alpha == 0;
                    }
                    Require(clearEdges, "antialiased silk fits inside a fully transparent outer edge");
                    Require(colored > 0 && colored < image.Width * image.Height * 0.65,
                        "the layer contains a skinny line and no filled panel");
                    Point startPixel = Point.Round(new PointF(anchor.X - surface.X, anchor.Y - surface.Y));
                    Point endPixel = Point.Round(new PointF(end.X - surface.X, end.Y - surface.Y));
                    Require(image.GetPixel(startPixel.X, startPixel.Y).A > 0 && image.GetPixel(endPixel.X, endPixel.Y).A > 0,
                        "visible silk actually connects the provided anchor and spider endpoints");
                }
            }
            bool invalidDrawRejected = false;
            try { using (Bitmap invalid = PureTetherArt.Draw(new PointF(0, 0), new PointF(0, 0), 1)) { } }
            catch (ArgumentOutOfRangeException) { invalidDrawRejected = true; }
            Require(invalidDrawRejected, "pure rendering rejects invalid geometry before allocation");
            using (Bitmap first = PureTetherArt.Draw(negativeAnchor, negativeSpider, 1))
            using (Bitmap second = PureTetherArt.Draw(negativeAnchor, negativeSpider, 1))
            {
                bool same = true;
                for (int y = 0; y < first.Height; y++)
                for (int x = 0; x < first.Width; x++) same &= first.GetPixel(x, y) == second.GetPixel(x, y);
                Require(same, "rendering is deterministic for the same actual geometry");
            }
            return assertions;
        }
    }
}
