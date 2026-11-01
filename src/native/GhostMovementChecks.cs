using System;
using System.Drawing;

namespace SpookyDesktopSpider
{
    // Pure simulation checks: no windows, UI Automation, input, or files.
    internal static class GhostMovementChecks
    {
        private static int assertions;
        private static readonly PointF QuietPointer = new PointF(-1000, -1000);
        private static readonly RectangleF Display = new RectangleF(20, 20, 1880, 1040);

        private static void Require(bool condition, string reason)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException("Ghost movement check failed: " + reason);
        }

        private static bool Near(double actual, double expected)
        {
            return Math.Abs(actual - expected) < 0.001;
        }

        private static EcologyMotion Wandering()
        {
            EcologyMotion motion = new EcologyMotion(new PointF(500, 500), Display, 12);
            motion.Update(QuietPointer, 0.05, 130, 1, null, false);
            Require(motion.State == "WANDER" && motion.Moving && motion.CurrentRouteId != null,
                "fixture begins on an approved moving wander route");
            return motion;
        }

        private static EcologyReceipt LastMovement(EcologyMotion motion)
        {
            for (int i = motion.Ledger.Count - 1; i >= 0; i--)
                if (motion.Ledger[i].Event == "MOVE_CONFIRMED") return motion.Ledger[i];
            return null;
        }

        public static int Run()
        {
            assertions = 0;

            EcologyMotion wander = Wandering();
            PointF heldPosition = wander.Position, heldTarget = wander.Target;
            string route = wander.CurrentRouteId, decision = wander.CurrentDecisionId;
            long tick = wander.Tick;
            wander.Update(QuietPointer, 0.25, 130, 1, null, true);
            Require(wander.CuriosityHolding && wander.Position == heldPosition && wander.DistanceMoved == 0,
                "curiosity holds the real wander effect without displacement");
            Require(wander.Target == heldTarget && wander.CurrentRouteId == route && wander.CurrentDecisionId == decision,
                "a held wander retains its target and approved lineage");
            Require(wander.Tick == tick + 1 && !wander.Receipts.Contains("MOVE_CONFIRMED"),
                "a held update still runs but claims no movement");
            wander.Update(QuietPointer, 0.2, 130, 1, null, true);
            Require(wander.CuriosityHolding && wander.Position == heldPosition && wander.CurrentRouteId == route,
                "continued curiosity does not repeatedly cancel or replace the route");
            wander.Update(QuietPointer, 0.1, 130, 1, null, false);
            Require(!wander.CuriosityHolding && EcologyMotion.Distance(wander.Position, heldPosition) > 0,
                "releasing curiosity resumes physical movement");
            Require(wander.Target == heldTarget && wander.CurrentRouteId == route && wander.CurrentDecisionId == decision,
                "release resumes the existing plan rather than inventing new approval");
            EcologyReceipt resumed = LastMovement(wander);
            Require(resumed != null && resumed.RouteId == route && resumed.DecisionId == decision,
                "resumed movement remains linked to its original approved route");
            Require(resumed != null && Near(resumed.Timestamp, 0.6),
                "normal simulation time advances during both held updates");

            EcologyMotion threatened = Wandering();
            threatened.Update(QuietPointer, 0.1, 130, 1, null, true);
            PointF beforeThreat = threatened.Position;
            string interruptedRoute = threatened.CurrentRouteId;
            PointF approachingPointer = new PointF(beforeThreat.X + 10, beforeThreat.Y);
            threatened.Update(approachingPointer, 0.1, 130, 1, null, true);
            Require(!threatened.CuriosityHolding && EcologyMotion.Distance(threatened.Position, beforeThreat) > 0,
                "a nearby real pointer preempts the hold with movement on the same update");
            Require(threatened.State == "ESCAPE" && threatened.CurrentRouteId != interruptedRoute &&
                threatened.Receipts.Contains("MOVE_CONFIRMED"),
                "threat response executes its own escape lineage instead of the held wander");

            EcologyMotion recover = new EcologyMotion(new PointF(140, 100), Display, 12);
            recover.CurrentWeb = new EcologyWeb { Anchor = new PointF(100, 100), Progress = 4, Interrupted = true };
            recover.Update(QuietPointer, 0.05, 40, 1, null, true);
            Require(recover.State == "RECOVER" && !recover.CuriosityHolding && Near(recover.DistanceMoved, 10),
                "curiosity cannot hold recovery toward an existing web");
            string recoveryRoute = recover.CurrentRouteId;
            recover.Update(QuietPointer, 0.05, 40, 1, null, true);
            Require(!recover.CuriosityHolding && Near(recover.DistanceMoved, 10) && recover.CurrentRouteId == recoveryRoute,
                "successive recovery updates retain lineage and continue moving");
            recover.Update(QuietPointer, 0.1, 40, 1, null, true);
            Require(recover.Position == recover.CurrentWeb.Anchor && recover.Receipts.Contains("RETURN_CONFIRMED"),
                "recovery still verifies actual physical homecoming while the hold is requested");
            Require(!recover.CuriosityHolding && !recover.WeaveAuthorized && Near(recover.CurrentWeb.Progress, 4),
                "homecoming does not authorize construction during an active session");

            EcologyMotion selecting = new EcologyMotion(new PointF(500, 500), Display, 12);
            selecting.Update(QuietPointer, 0.1, 310, 1, null, true);
            Require(selecting.State == "SELECT_ANCHOR" && !selecting.CuriosityHolding && selecting.DistanceMoved > 0,
                "curiosity cannot hold initial travel to a construction anchor");

            EcologyMotion weaving = new EcologyMotion(new PointF(100, 100), Display, 12);
            weaving.CurrentWeb = new EcologyWeb { Anchor = new PointF(100, 100), Progress = 4, Interrupted = false };
            weaving.Update(QuietPointer, 0.1, 310, 1, null, true);
            Require(weaving.State == "WEAVE" && weaving.WeaveAuthorized && !weaving.CuriosityHolding,
                "a curiosity request does not override current weaving authorization");
            Require(Near(weaving.CurrentWeb.Progress, 4.1) && weaving.Receipts.Contains("WEAVE_PROGRESS_CONFIRMED"),
                "authorized silk continues through the normal guarded effect");

            EcologyMotion firstWeb = new EcologyMotion(new PointF(60, 60), Display, 12);
            firstWeb.Update(QuietPointer, 0.1, 310, 1, null, true);
            Require(firstWeb.CurrentWeb != null && firstWeb.State == "WEAVE" && firstWeb.WeaveAuthorized &&
                !firstWeb.CuriosityHolding && firstWeb.CurrentWeb.Progress > 0,
                "a newly reached anchor can begin authorized construction despite curiosity");

            EcologyMotion threshold = Wandering();
            threshold.Update(QuietPointer, 0.1, 130, 1, null, true);
            PointF beforeThreshold = threshold.Position;
            string wanderRoute = threshold.CurrentRouteId;
            threshold.Update(QuietPointer, 0.1, 310, 1, null, true);
            Require(!threshold.CuriosityHolding && threshold.State == "SELECT_ANCHOR" &&
                EcologyMotion.Distance(threshold.Position, beforeThreshold) > 0,
                "the construction idle threshold preempts a held wander immediately");
            Require(threshold.CurrentRouteId != null && threshold.CurrentRouteId != wanderRoute,
                "anchor selection receives fresh lineage when it replaces the wander");

            EcologyMotion withWeb = Wandering();
            withWeb.CurrentWeb = new EcologyWeb { Anchor = new PointF(100, 100), Progress = 2, Interrupted = false };
            withWeb.Update(QuietPointer, 0.1, 130, 1, null, true);
            Require(!withWeb.CuriosityHolding && withWeb.DistanceMoved > 0 && Near(withWeb.CurrentWeb.Progress, 2),
                "even safe wandering with an existing web is outside the curiosity hold boundary");

            return assertions;
        }
    }
}
