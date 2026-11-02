using System;
using System.Drawing;
using System.Reflection;

namespace SpookyDesktopSpider
{
    // Acceptance scenarios use only simulation values and owned web objects.
    // No desktop APIs, UI Automation, files, network, or input are exercised.
    internal static class HangingAcceptanceChecks
    {
        private static int assertions;
        private static readonly PointF QuietPointer = new PointF(1000, 1000);
        private static readonly PointF Home = new PointF(100, 100);
        private static readonly RectangleF Display = new RectangleF(20, 20, 1880, 1040);

        private static void Require(bool condition, string reason)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException("Hanging acceptance check failed: " + reason);
        }

        private static bool At(PointF a, PointF b)
        {
            return EcologyMotion.Distance(a, b) <= 0.01;
        }

        private static bool Inside(PointF point, RectangleF bounds)
        {
            return !float.IsNaN(point.X) && !float.IsInfinity(point.X) &&
                !float.IsNaN(point.Y) && !float.IsInfinity(point.Y) &&
                point.X >= bounds.Left && point.X <= bounds.Right &&
                point.Y >= bounds.Top && point.Y <= bounds.Bottom;
        }

        private static int Count(EcologyMotion motion, string outcome)
        {
            int count = 0;
            foreach (HangingReceipt receipt in motion.SuspensionLedger)
                if (receipt.Outcome == outcome) count++;
            return count;
        }

        private static bool LastMovementWasEscape(EcologyMotion motion)
        {
            for (int i = motion.Ledger.Count - 1; i >= 0; i--)
            {
                EcologyReceipt receipt = motion.Ledger[i];
                if (receipt.Event == "MOVE_CONFIRMED")
                    return receipt.Evidence != null && receipt.Evidence.State == "ESCAPE" && receipt.Evidence.DistanceMoved > 0;
            }
            return false;
        }

        private static EcologyMotion WebAt(PointF home, RectangleF bounds)
        {
            EcologyMotion motion = new EcologyMotion(home, bounds, 14);
            motion.CurrentWeb = new EcologyWeb { Anchor = home, Progress = 10, Interrupted = false };
            return motion;
        }

        private static EcologyMotion Prepared(double depth)
        {
            EcologyMotion motion = WebAt(Home, Display);
            motion.HangingEnabled = true;
            motion.HangingDepth = depth;
            motion.Update(QuietPointer, 0, 601, 1);
            Require(motion.SuspensionActive && motion.SuspensionState == "PREPARE_DESCENT",
                "eligible existing web selects a separate preparation phase");
            Require(At(motion.Position, Home) && !motion.Receipts.Contains("MOVE_CONFIRMED") &&
                Count(motion, "RETURN_CONFIRMED") == 0 && Count(motion, "WEB_IMPACT_CONFIRMED") == 0,
                "a zero-time plan is not displacement, homecoming, or rendered impact");
            return motion;
        }

        private static EcologyMotion AtBottom(double depth)
        {
            EcologyMotion motion = Prepared(depth);
            EcologyWeb original = motion.CurrentWeb;
            int frames = 0;
            while (motion.SuspensionState != "HANGING" && frames++ < 100)
            {
                PointF before = motion.Position;
                motion.Update(QuietPointer, 0.1, 601, 1);
                Require(Inside(motion.Position, Display) && Math.Abs(motion.Position.X - Home.X) <= 0.01 &&
                    motion.Position.Y + 0.01 >= before.Y && motion.Position.Y <= Home.Y + depth + 0.01,
                    "each descent effect is vertical, bounded, and directed toward the selected endpoint");
                Require(object.ReferenceEquals(motion.CurrentWeb, original) && motion.CurrentWeb.Progress == 10 &&
                    !motion.WeaveAuthorized && motion.WebBounceOffset == 0,
                    "suspension preserves woven progress and cannot construct silk or bounce remotely");
            }
            Require(motion.SuspensionState == "HANGING" && motion.HangingAnimation && motion.TetherVisible &&
                At(motion.Position, new PointF(Home.X, Home.Y + (float)depth)),
                "hanging begins only at the physically measured descent endpoint");
            Require(motion.TetherPosition == motion.Position && motion.TetherAnchor == Home &&
                Count(motion, "RETURN_CONFIRMED") == 0 && !motion.WebImpactPending,
                "tether endpoints describe real position and descent does not imply a return");
            return motion;
        }

        public static int Run()
        {
            assertions = 0;

            // 1. Explicit opt-in, idle, woven-web, and upper-corner eligibility.
            EcologyMotion disabled = WebAt(Home, Display);
            Require(!disabled.HangingEnabled && disabled.HangingIdleThreshold == 600,
                "hanging defaults off with the ten-minute idle threshold");
            disabled.Update(QuietPointer, 0, 1000, 1);
            Require(!disabled.SuspensionActive && !disabled.TetherVisible,
                "idle alone cannot enable the optional feature");
            disabled.HangingEnabled = true;
            disabled.Update(QuietPointer, 0, 1000, 1);
            Require(disabled.SuspensionState == "PREPARE_DESCENT" && disabled.SuspensionActive,
                "explicit enabling does not inherit a cooldown from ordinary disabled updates");
            EcologyMotion early = WebAt(Home, Display);
            early.HangingEnabled = true;
            early.Update(QuietPointer, 0, 599, 1);
            Require(!early.SuspensionActive && At(early.Position, Home), "idle below the threshold does not descend");
            EcologyMotion noWeb = new EcologyMotion(Home, Display, 14);
            noWeb.HangingEnabled = true;
            noWeb.Update(QuietPointer, 0, 601, 1);
            Require(!noWeb.SuspensionActive && !noWeb.TetherVisible, "an intended anchor without a web cannot support suspension");
            EcologyMotion unwoven = WebAt(Home, Display);
            unwoven.CurrentWeb.Progress = 0;
            unwoven.HangingEnabled = true;
            unwoven.Update(QuietPointer, 0, 601, 1);
            Require(!unwoven.SuspensionActive, "an unconstructed web cannot support suspension");
            EcologyMotion central = WebAt(new PointF(800, 600), Display);
            central.HangingEnabled = true;
            central.Update(QuietPointer, 0, 601, 1);
            Require(!central.SuspensionActive, "a web outside an eligible upper corner is declined");

            // 2. Physical descent and a real stationary hanging endpoint.
            EcologyMotion hanging = AtBottom(160);
            PointF hangingPosition = hanging.Position;
            hanging.Update(QuietPointer, 0.1, 601, 1);
            Require(hanging.Position == hangingPosition && hanging.TetherPosition == hangingPosition &&
                hanging.CurrentWeb.Progress == 10 && !hanging.WebImpactPending,
                "hanging animation does not substitute a visual offset for the real spider position");
            Require(!hanging.ConfirmWebImpactRendered() && Count(hanging, "WEB_IMPACT_CONFIRMED") == 0,
                "a renderer acknowledgement away from home cannot manufacture an impact");

            // 3. A newly blocked remaining descent is rejected before execution.
            EcologyMotion blockedDrop = Prepared(240);
            blockedDrop.Update(QuietPointer, 0.1, 601, 1);
            PointF beforeBlock = blockedDrop.Position;
            PointF futureObstruction = new PointF(Home.X, Home.Y + 260);
            Require(EcologyMotion.Distance(beforeBlock, futureObstruction) > 240,
                "dynamic-corridor fixture starts outside the immediate assessment radius");
            blockedDrop.Update(futureObstruction, 0.1, 601, 1);
            Require(blockedDrop.Position.Y <= beforeBlock.Y + 0.01 &&
                EcologyMotion.SegmentDistance(beforeBlock, blockedDrop.Position, futureObstruction) >= 162 - 0.01,
                "a blocked future descent cannot execute another downward step");
            Require(!blockedDrop.TetherVisible && blockedDrop.CurrentWeb.Progress == 10,
                "a revoked descent releases its tether without losing the web");
            if (blockedDrop.Receipts.Contains("RETURN_CONFIRMED"))
                Require(At(blockedDrop.Position, blockedDrop.CurrentWeb.Anchor), "any fallback homecoming receipt requires actual arrival");

            // 4. Moderate threat selects a safe climb; later obstruction cancels it.
            EcologyMotion mild = AtBottom(160);
            PointF beforeClimb = mild.Position;
            PointF moderate = new PointF(beforeClimb.X, beforeClimb.Y + 200);
            mild.Update(moderate, 0.1, 601, 1);
            Require(mild.SuspensionState == "CLIMB_HOME" && mild.Position.Y < beforeClimb.Y &&
                EcologyMotion.SegmentDistance(beforeClimb, mild.Position, moderate) >= 162 - 0.01,
                "moderate threat produces a physically executed safe climb");
            PointF beforeReblock = mild.Position;
            PointF homeObstruction = new PointF(Home.X, (Home.Y + beforeReblock.Y) / 2);
            mild.Update(homeObstruction, 0.1, 601, 1);
            Require(!mild.SuspensionActive && !mild.TetherVisible && LastMovementWasEscape(mild) &&
                EcologyMotion.Distance(mild.Position, homeObstruction) > EcologyMotion.Distance(beforeReblock, homeObstruction),
                "a newly unsafe return releases the tether and hands control to ordinary escape");
            Require(mild.CurrentWeb.Progress == 10 && Count(mild, "RETURN_CONFIRMED") == 0,
                "a canceled climb preserves construction and cannot certify homecoming");

            // 5. Strong safe retreat can leave an initial danger region monotonically.
            EcologyMotion strong = AtBottom(160);
            PointF beforeSnap = strong.Position;
            PointF closeBelow = new PointF(beforeSnap.X, beforeSnap.Y + 90);
            strong.Update(closeBelow, 0.1, 601, 1);
            Require(strong.SuspensionState == "SNAP_HOME" && strong.Position.Y < beforeSnap.Y &&
                EcologyMotion.Distance(strong.Position, closeBelow) > EcologyMotion.Distance(beforeSnap, closeBelow),
                "strong threat permits a snap only as physically measured increasing clearance");
            Require(EcologyMotion.SegmentDistance(beforeSnap, strong.Position, closeBelow) + 0.01 >=
                EcologyMotion.Distance(beforeSnap, closeBelow), "the executed egress never approaches the pointer more closely");
            Require(Count(strong, "RETURN_CONFIRMED") == 0 && !strong.WebImpactPending && strong.WebBounceOffset == 0,
                "selecting or partially executing a fast return does not imply arrival or bounce");

            // 6. Return is measured; bounce is a visual request confirmed only by the host.
            int returns = 0;
            while (!strong.WebImpactPending && returns++ < 30) strong.Update(closeBelow, 0.05, 601, 1);
            Require(strong.WebImpactPending && At(strong.Position, Home) && Count(strong, "RETURN_CONFIRMED") == 1,
                "a safe return is certified exactly once after physical anchor arrival");
            Require(strong.CurrentWeb.Progress == 10 && Count(strong, "WEB_IMPACT_CONFIRMED") == 0,
                "verified arrival preserves progress and does not assert that the host rendered impact");
            for (int i = 0; i < 6 && Math.Abs(strong.WebBounceOffset) <= 0.001; i++)
                strong.Update(closeBelow, 0.05, 601, 1);
            Require(At(strong.Position, Home) && strong.WebImpactPending && Count(strong, "WEB_IMPACT_CONFIRMED") == 0,
                "a pending visual bounce request cannot move the spider or confirm its own rendering");
            Require(Math.Abs(strong.WebBounceOffset) > 0.001,
                "the host receives a nonzero visual bounce request after verified arrival");
            Require(strong.ConfirmWebImpactRendered() && !strong.ConfirmWebImpactRendered() &&
                Count(strong, "WEB_IMPACT_CONFIRMED") == 1, "only one valid host acknowledgement confirms the impact");
            int settling = 0;
            while (strong.SuspensionActive && settling++ < 30)
            {
                strong.Update(closeBelow, 0.05, 601, 1);
                Require(At(strong.Position, Home), "web-only settling never displaces the spider from its measured anchor");
                Require(strong.CurrentWeb.Progress == 10, "settling preserves the woven web rather than rebuilding it");
            }
            Require(!strong.SuspensionActive && !strong.TetherVisible && strong.WebBounceOffset == 0 &&
                strong.CurrentWeb.Progress >= 10, "settling terminates without a residual tether or destructive progress reset");

            // 7. A blocked anchor cannot be snapped through or replaced by intent.
            EcologyMotion unsafeSnap = AtBottom(160);
            PointF unsafeBefore = unsafeSnap.Position;
            PointF between = new PointF(Home.X, (Home.Y + unsafeBefore.Y) / 2);
            unsafeSnap.Update(between, 0.1, 601, 1);
            Require(!unsafeSnap.SuspensionActive && !unsafeSnap.TetherVisible && LastMovementWasEscape(unsafeSnap) &&
                !At(unsafeSnap.Position, Home), "strong threat between body and home releases into ordinary escape");
            Require(EcologyMotion.Distance(unsafeSnap.Position, between) > EcologyMotion.Distance(unsafeBefore, between) &&
                unsafeSnap.CurrentWeb.Progress == 10 && Count(unsafeSnap, "RETURN_CONFIRMED") == 0,
                "unsafe release increases actual clearance while retaining the interrupted web");

            // 8. New web identity, moved anchor, and display relocation invalidate the tether.
            EcologyMotion replaced = AtBottom(160);
            EcologyWeb oldWeb = replaced.CurrentWeb;
            EcologyWeb newWeb = new EcologyWeb { Anchor = Home, Progress = 23, Interrupted = false };
            replaced.CurrentWeb = newWeb;
            replaced.Update(QuietPointer, 0, 601, 1);
            Require(!replaced.SuspensionActive && !replaced.TetherVisible && object.ReferenceEquals(replaced.CurrentWeb, newWeb) &&
                oldWeb.Progress == 10 && newWeb.Progress == 23 && !replaced.ConfirmWebImpactRendered(),
                "replacement web identity cannot reuse the old tether or rendering acknowledgement");
            EcologyMotion movedAnchor = AtBottom(160);
            movedAnchor.CurrentWeb.Anchor = new PointF(Home.X + 40, Home.Y);
            movedAnchor.Update(QuietPointer, 0, 601, 1);
            Require(!movedAnchor.SuspensionActive && !movedAnchor.TetherVisible &&
                movedAnchor.CurrentWeb.Anchor.X == Home.X + 40 && movedAnchor.CurrentWeb.Progress == 10 &&
                !movedAnchor.ConfirmWebImpactRendered(), "moving an anchor retires the old tether without restoring stale coordinates");
            EcologyMotion resized = AtBottom(160);
            resized.SetBounds(new RectangleF(-800, -600, 1200, 900));
            Require(!resized.SuspensionActive && !resized.TetherVisible && Inside(resized.Position, resized.Bounds) &&
                resized.CurrentWeb.Progress == 10 && Count(resized, "RETURN_CONFIRMED") == 0,
                "display changes cancel suspension and cannot be relabeled as voluntary homecoming");
            EcologyMotion staleImpact = AtBottom(80);
            PointF impactThreat = new PointF(staleImpact.Position.X, staleImpact.Position.Y + 90);
            int impactFrames = 0;
            while (!staleImpact.WebImpactPending && impactFrames++ < 30)
                staleImpact.Update(impactThreat, 0.05, 601, 1);
            Require(staleImpact.WebImpactPending && At(staleImpact.Position, Home),
                "stale-impact fixture first proves physical arrival");
            staleImpact.Bounds = new RectangleF(20, 20, 1800, 1000);
            Require(!staleImpact.ConfirmWebImpactRendered() && !staleImpact.SuspensionActive &&
                Count(staleImpact, "WEB_IMPACT_CONFIRMED") == 0 && staleImpact.CurrentWeb.Progress == 10,
                "a changed display context between arrival and acknowledgement cannot certify stale rendering");

            // 9. Host lifecycle cancellations are immediate and effect-free.
            foreach (string reason in new string[] { "USER_PAUSED", "SESSION_LOCK", "DISPLAY_CHANGED", "SHUTDOWN" })
            {
                EcologyMotion canceled = Prepared(160);
                canceled.Update(QuietPointer, 0.1, 601, 1);
                PointF beforeCancel = canceled.Position;
                EcologyWeb retained = canceled.CurrentWeb;
                canceled.CancelHanging(reason);
                Require(!canceled.SuspensionActive && !canceled.TetherVisible && !canceled.WebImpactPending &&
                    canceled.WebBounceOffset == 0 && canceled.Position == beforeCancel,
                    reason + " cancels the tether without teleportation or a pending bounce");
                Require(object.ReferenceEquals(canceled.CurrentWeb, retained) && retained.Progress == 10 &&
                    !canceled.ConfirmWebImpactRendered() && Count(canceled, "RETURN_CONFIRMED") == 0,
                    reason + " preserves progress and rejects late render acknowledgements");
                canceled.Update(QuietPointer, 0.1, 601, 1);
                Require(!canceled.SuspensionActive && !canceled.TetherVisible,
                    reason + " cannot immediately resurrect the canceled suspension");
            }

            // 10. Signed coordinates, bounded large frames, and immutable evidence.
            RectangleF negativeDisplay = new RectangleF(-1900, -1100, 1880, 1040);
            PointF negativeHome = new PointF(-1820, -1020);
            EcologyMotion negative = WebAt(negativeHome, negativeDisplay);
            negative.HangingEnabled = true;
            negative.HangingDepth = 240;
            negative.Update(QuietPointer, 100, 601, 1);
            PointF beforeLargeFrame = negative.Position;
            negative.Update(QuietPointer, 100, 601, 1);
            Require(negative.SuspensionState == "DESCENDING" && negative.Position.Y > beforeLargeFrame.Y &&
                EcologyMotion.Distance(beforeLargeFrame, negative.Position) <= 35.01 && Inside(negative.Position, negativeDisplay),
                "negative-origin descent honors the 0.5-second cap rather than teleporting on a large frame");
            PointF beforeZero = negative.Position;
            negative.Update(QuietPointer, -1, 601, 1);
            Require(negative.Position == beforeZero && !negative.Receipts.Contains("MOVE_CONFIRMED") &&
                Count(negative, "RETURN_CONFIRMED") == 0, "negative elapsed time cannot reverse movement or fabricate return");
            foreach (FieldInfo field in typeof(HangingReceipt).GetFields(BindingFlags.Public | BindingFlags.Instance))
                Require(field.IsInitOnly, "published suspension evidence is immutable: " + field.Name);
            foreach (HangingReceipt receipt in negative.SuspensionLedger)
                if (receipt.Outcome == "DESCENT_CONFIRMED")
                    Require(receipt.DistanceMoved > 0 && receipt.To.Y > receipt.From.Y && receipt.RouteId != null && receipt.DecisionId != null,
                        "descent evidence contains actual displacement and approved route lineage");
            Require(negative.SuspensionLedger.Count <= 100, "acceptance scenarios retain bounded suspension history");

            return assertions;
        }
    }
}
