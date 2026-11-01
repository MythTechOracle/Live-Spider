using System;
using System.Drawing;

namespace SpookyDesktopSpider
{
    internal static class EcologyChecks
    {
        private static int count;
        private static void Require(bool condition, string label)
        {
            count++;
            if (!condition) throw new InvalidOperationException("Ecology check failed: " + label);
        }

        private static EcologyMotion Recovery()
        {
            EcologyMotion m = new EcologyMotion(new PointF(800, 800), new RectangleF(20, 20, 1880, 1040), 7);
            m.CurrentWeb = new EcologyWeb { Anchor = new PointF(100, 100), Progress = 42, Interrupted = true };
            return m;
        }

        public static string Run()
        {
            count = 0;
            PointF guard = new PointF(450, 450), far = new PointF(-1000, -1000);
            EcologyMotion m = Recovery();
            for (int i = 0; i < 120; i++)
            {
                PointF previous = m.Position;
                m.Update(guard, 0.1, 300, 1);
                Require(m.State == "WAIT_FOR_ROUTE", "stationary obstruction waits rather than bouncing");
                Require(m.Position == previous && m.DistanceMoved == 0, "blocked intent produces no claimed movement");
                Require(m.PostThreatDistance >= 150, "blocked recovery remains safe");
                Require(m.CurrentWeb.Progress == 42, "blocked recovery preserves web progress");
            }
            Require(m.RouteChecks <= 5, "remembered route avoids a fresh planning attempt every tick");
            Require(m.RejectedRouteCount <= 16, "route memory is bounded");
            bool returned = false;
            for (int i = 0; i < 100; i++)
            {
                PointF previous = m.Position;
                m.Update(far, 0.1, 300, 1);
                Require(EcologyMotion.SegmentDistance(previous, m.Position, far) >= 150, "cleared corridor stays safe");
                if (m.Receipts.Contains("RETURN_CONFIRMED"))
                {
                    returned = true;
                    Require(EcologyMotion.Distance(m.Position, m.CurrentWeb.Anchor) <= 0.01, "return receipt proves anchor arrival");
                    Require(m.DistanceMoved > 0, "return receipt follows displacement");
                    break;
                }
            }
            Require(returned, "pointer withdrawal permits eventual homecoming");
            Require(m.CurrentWeb.Progress >= 42, "homecoming retains unfinished web");
            double progress = m.CurrentWeb.Progress;
            m.Update(far, 0.1, 0, 1);
            Require(m.CurrentWeb.Progress == progress, "active desktop cannot weave");
            m.Position = new PointF(800, 800);
            m.CurrentWeb.Interrupted = false;
            m.Update(far, 0.1, 300, 1);
            Require(m.State == "RECOVER" && m.CurrentWeb.Progress == progress, "no weaving away from anchor");

            m = new EcologyMotion(new PointF(500, 500), new RectangleF(20, 20, 1880, 1040), 9);
            m.Update(new PointF(500, 500), 0, 0, 1);
            Require(m.State == "ESCAPE" && m.DistanceMoved == 0, "zero-time threat has intent without effect");
            m.Update(far, 0, 0, 1);
            Require(m.Receipts.Contains("POINTER_WITHDREW"), "withdrawal has an honest cause receipt");
            Require(!m.Receipts.Contains("MOVE_CONFIRMED") && m.DistanceMoved == 0, "withdrawal is not spider motion");
            Require(m.Receipts.Contains("THREAT_CLEARED"), "withdrawal verifies clearance");

            m = new EcologyMotion(new PointF(1900, 1060), new RectangleF(20, 20, 1880, 1040), 11);
            PointF cornerStart = m.Position;
            for (int i = 0; i < 60; i++) m.Update(new PointF(1820, 980), 0.1, 0, 1);
            Require(EcologyMotion.Distance(cornerStart, m.Position) > 20, "corner cancellation does not paralyze spider");
            Require(m.PostThreatDistance >= 150, "corner maneuver eventually clears threat");

            m = new EcologyMotion(new PointF(-1500, -500), new RectangleF(-1900, -1000, 1800, 900), 12);
            PointF start = m.Position;
            m.Update(start, 50, 0, 1);
            Require(m.DistanceMoved <= 820 * 0.5 + 0.01, "large time gap is bounded");
            Require(m.Position.X >= -1900 && m.Position.X <= -100 && m.Position.Y >= -1000 && m.Position.Y <= -100,
                "negative display coordinates remain in bounds");
            start = m.Position;
            m.Update(start, -5, 0, 1);
            Require(m.Position == start && m.DistanceMoved == 0, "negative time cannot move backward");
            bool rejected = false;
            try { m.Update(new PointF(float.NaN, 0), 0.1, 0, 1); } catch (ArgumentException) { rejected = true; }
            Require(rejected, "nonfinite cursor rejected");
            rejected = false;
            try { m.Update(far, double.NaN, 0, 1); } catch (ArgumentException) { rejected = true; }
            Require(rejected, "nonfinite time rejected");
            rejected = false;
            try { m.SetBounds(new RectangleF(0, 0, 0, 10)); } catch (ArgumentException) { rejected = true; }
            Require(rejected, "empty display rectangle rejected");
            m = new EcologyMotion(new PointF(0, 0), new RectangleF(0, 0, 1000, 800), 1);
            m.WanderThreshold = m.WeaveThreshold = 0;
            m.Update(far, 0.1, 0, 1);
            Require(m.State == "SELECT_ANCHOR", "zero idle threshold honored");

            return "PASS: native ecology " + count + " assertions; blocked-route memory, 120-tick safety, verified homecoming, " +
                "honest withdrawal/movement receipts, retained silk, idle-only weaving, corners, time gaps, finite inputs, and negative displays.";
        }
    }
}
