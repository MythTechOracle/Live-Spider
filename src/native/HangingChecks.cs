using System;
using System.Drawing;

namespace SpookyDesktopSpider
{
    internal static class HangingChecks
    {
        private static int assertions;
        private static readonly PointF Quiet = new PointF(1000, 1000);
        private static readonly RectangleF Display = new RectangleF(20, 20, 1880, 1040);
        private static void Require(bool condition, string reason)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException("Hanging engine check failed: " + reason);
        }
        private static EcologyMotion Ready()
        {
            EcologyMotion m = new EcologyMotion(new PointF(100, 100), Display, 14);
            m.CurrentWeb = new EcologyWeb { Anchor = m.Position, Progress = 10 };
            m.HangingEnabled = true;
            return m;
        }
        private static EcologyMotion Hung()
        {
            EcologyMotion m = Ready();
            for (int i = 0; i < 100 && m.SuspensionState != "HANGING"; i++) m.Update(Quiet, 0.05, 601, 1);
            Require(m.SuspensionState == "HANGING", "finite safe descent reaches its physical endpoint");
            return m;
        }
        private static bool Has(EcologyMotion m, string outcome)
        {
            foreach (HangingReceipt receipt in m.SuspensionLedger) if (receipt.Outcome == outcome) return true;
            return false;
        }
        public static int Run()
        {
            assertions = 0;
            EcologyMotion off = new EcologyMotion(new PointF(100, 100), Display, 14);
            off.CurrentWeb = new EcologyWeb { Anchor = off.Position, Progress = 10 };
            off.Update(Quiet, 0.1, 601, 1);
            Require(!off.HangingEnabled && !off.SuspensionActive && !off.TetherVisible, "hanging is opt-in and defaults off");
            EcologyMotion m = Ready();
            m.Update(Quiet, 0, 601, 1);
            Require(m.SuspensionState == "PREPARE_DESCENT" && m.DistanceMoved == 0 && !Has(m, "DESCENT_CONFIRMED"), "descent intent alone is not physical descent");
            Require(Has(m, "SILK_ANCHOR_CONFIRMED") && Has(m, "DESCENT_AUTHORIZED") && m.CurrentRouteId != null, "measured existing web receives a routed descent authorization");
            m = Hung();
            Require(Math.Abs(m.Position.Y - 260) <= 0.01 && m.TetherPosition == m.Position && m.TetherVisible, "tether endpoint is the real bounded spider position");
            Require(m.CurrentWeb.Progress == 10 && m.WebBounceOffset == 0, "hanging preserves construction memory without false impact");
            PointF held = m.Position;
            for (int i = 0; i < 10; i++) m.Update(Quiet, 0.05, 601, 1);
            Require(m.Position == held && m.DistanceMoved == 0 && m.HangingAnimation, "idle leg animation never invents physical sway");
            PointF mild = new PointF(m.Position.X, m.Position.Y + 200);
            m.Update(mild, 0.05, 0, 1);
            Require(m.SuspensionState == "CLIMB_HOME" && m.Position.Y < held.Y && m.DistanceMoved > 0, "moderate disturbance chooses actual climbing");
            for (int i = 0; i < 100 && !m.WebImpactPending; i++) m.Update(mild, 0.05, 0, 1);
            Require(m.WebImpactPending && m.Position == m.CurrentWeb.Anchor && Has(m, "RETURN_CONFIRMED"), "arrival is confirmed only at the physical web");
            Require(!Has(m, "WEB_IMPACT_CONFIRMED"), "impact rendering remains unconfirmed before host acknowledgement");
            PointF arrived = m.Position;
            m.Update(Quiet, 0.05, 0, 1);
            Require(m.Position == arrived && Math.Abs(m.WebBounceOffset) > 0.1, "bounce is a web-only visual request after arrival");
            Require(m.ConfirmWebImpactRendered() && Has(m, "WEB_IMPACT_CONFIRMED"), "visible impact may be explicitly acknowledged");
            for (int i = 0; i < 30 && m.SuspensionActive; i++) m.Update(Quiet, 0.05, 0, 1);
            Require(m.Position == arrived && m.SuspensionState == "REST_COOLDOWN" && Has(m, "SETTLED_AT_HOME"), "settling never changes the actual spider coordinate");
            m = Hung();
            PointF strong = new PointF(m.Position.X, m.Position.Y + 90);
            double previousDistance = EcologyMotion.Distance(m.Position, strong);
            for (int i = 0; i < 100 && !m.WebImpactPending; i++)
            {
                m.Update(strong, 0.05, 0, 1);
                Require(m.PostThreatDistance + 0.01 >= previousDistance && m.DistanceMoved <= 620 * 0.05 + 0.01,
                    "safe snap exits initial danger monotonically under its speed bound");
                previousDistance = m.PostThreatDistance;
            }
            Require(m.WebImpactPending && m.Position == m.CurrentWeb.Anchor, "safe strong disturbance returns through real motion");
            m = Hung();
            m.Update(new PointF(m.Position.X, (m.Position.Y + m.CurrentWeb.Anchor.Y) / 2), 0.05, 0, 1);
            Require(!m.TetherVisible && m.SuspensionState == "RELEASE_AND_ESCAPE" && !Has(m, "RETURN_CONFIRMED"), "blocked snap releases instead of crossing danger");
            m = Ready(); m.Update(Quiet, 0.05, 601, 1); m.Update(Quiet, 0.05, 601, 1);
            held = m.Position; double progress = m.CurrentWeb.Progress;
            m.CancelHanging("PAUSED");
            Require(m.Position == held && !m.TetherVisible && !m.SuspensionActive && m.CurrentWeb.Progress == progress,
                "lifecycle cancellation preserves real position and unfinished web");
            m.Update(Quiet, 0.05, 601, 1);
            Require(!m.SuspensionActive && m.SuspensionState == "REST_COOLDOWN", "resume cannot instantly start another descent");
            Require(m.SuspensionLedger.Count <= HangingMotion.LedgerLimit, "suspension receipt memory is bounded");
            m = Hung();
            m.CurrentWeb = new EcologyWeb { Anchor = new PointF(100, 100), Progress = 23 };
            m.Update(Quiet, 0, 601, 1);
            Require(m.SuspensionReceipts.Contains("TETHER_RELEASED") &&
                m.SuspensionLedger[m.SuspensionLedger.Count - 1].Tick == m.Tick,
                "web-identity cancellation remains visible in its actual current-tick receipts");
            return assertions;
        }
    }
}
