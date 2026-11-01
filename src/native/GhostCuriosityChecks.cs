using System;
using System.Drawing;
using System.Reflection;

namespace SpookyDesktopSpider
{
    internal static class GhostCuriosityChecks
    {
        private static int assertions;

        private static void Require(bool condition, string reason)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException("Ghost curiosity check failed: " + reason);
        }

        private static GhostCandidate Button()
        {
            return new GhostCandidate(new RectangleF(90, 90, 80, 30), HoverControlKind.Button);
        }

        private static int Count(GhostCuriosity engine, string outcome)
        {
            int result = 0;
            foreach (GhostReceipt receipt in engine.Ledger) if (receipt.Outcome == outcome) result++;
            return result;
        }

        private static GhostCuriosity Hovering()
        {
            GhostCuriosity engine = new GhostCuriosity();
            engine.Update(0, 0, new PointF(100, 100), Button(), true, false);
            engine.Update(0.1, 0.1, new PointF(100, 100), Button(), true, false);
            engine.Update(0.1, 0.2, new PointF(100, 100), Button(), true, false);
            return engine;
        }

        public static int Run()
        {
            assertions = 0;
            GhostCuriosity engine = new GhostCuriosity();
            Require(engine.State == "EXPLORE" && !engine.HoldMovement && !engine.WantsGlow && engine.Ledger.Count == 0,
                "initial exploration has no visual effect or claimed observation");
            engine.ConfirmRendered();
            Require(engine.Ledger.Count == 0, "acknowledgement without a render request proves nothing");
            engine.Update(0, 0, new PointF(100, 100), Button(), true, false);
            Require(engine.State == "TARGET_DETECTED" && engine.HoldMovement && !engine.WantsGlow, "detection pauses movement without glowing");
            Require(Count(engine, "TARGET_OBSERVED") == 1 && Count(engine, "GHOST_HOVER_RENDERED") == 0, "intent is distinct from a rendered effect");
            engine.Update(0.1, 0.1, new PointF(100, 100), Button(), true, false);
            Require(engine.State == "OBSERVE" && engine.HoldMovement && !engine.WantsGlow, "short observation is a real intermediate phase");
            engine.Update(0.1, 0.2, new PointF(100, 100), Button(), true, false);
            Require(engine.State == "GHOST_HOVER" && engine.HoldMovement && engine.WantsGlow, "observation eventually requests the visual hover");
            Require(engine.GlowBounds == new Rectangle(90, 90, 80, 30), "glow is restricted to the approved target rectangle");
            Require(Count(engine, "GHOST_HOVER_RENDERED") == 0, "a request alone is not a rendering receipt");
            engine.ConfirmRendered();
            Require(Count(engine, "GHOST_HOVER_RENDERED") == 1 && Count(engine, "NO_INPUT_DISPATCHED") == 1,
                "host acknowledgement records rendering and the engine-scoped capability statement");
            int afterAck = engine.Ledger.Count;
            engine.ConfirmRendered();
            Require(engine.Ledger.Count == afterAck, "duplicate rendering acknowledgements do not flood history");
            for (int i = 0; i < 6; i++) engine.Update(0.1, 0.3 + i * 0.1, new PointF(100, 100), Button(), true, false);
            Require(engine.State == "DEPART" && !engine.HoldMovement && !engine.WantsGlow && !engine.GlowBounds.HasValue,
                "visual hover releases movement after its bounded dwell");
            engine.ConfirmRendered();
            Require(engine.Ledger.Count == afterAck, "a late acknowledgement after departure cannot certify a new render");
            engine.Update(0.25, 0.95, new PointF(100, 100), Button(), true, false);
            engine.Update(0.2, 1.15, new PointF(100, 100), Button(), true, false);
            Require(engine.State == "EXPLORE" && !engine.HoldMovement, "departure ends while the same target remains on cooldown");
            int cooldownCount = engine.Ledger.Count;
            for (int i = 0; i < 100; i++) engine.Update(0, 1.15, new PointF(100, 100), Button(), true, false);
            Require(engine.Ledger.Count == cooldownCount && Count(engine, "TARGET_OBSERVED") == 1, "stationary same-target cooldown is nonflooding");
            engine.Update(0, 4, new PointF(100, 100), Button(), true, false);
            Require(engine.State == "TARGET_DETECTED" && Count(engine, "TARGET_OBSERVED") == 2, "elapsed cooldown permits another deliberate visit");

            foreach (string interruption in new string[] { "threat", "override", "stale", "changed" })
            {
                GhostCuriosity interrupted = Hovering();
                GhostCandidate candidate = interruption == "stale" ? null : interruption == "changed" ?
                    new GhostCandidate(new RectangleF(85, 85, 90, 40), HoverControlKind.Button) : Button();
                interrupted.Update(0.01, 0.21, new PointF(100, 100), candidate, interruption != "override", interruption == "threat");
                Require(interrupted.State == "DEPART" && !interrupted.HoldMovement && !interrupted.WantsGlow && !interrupted.GlowBounds.HasValue,
                    interruption + " immediately clears glow and movement hold");
                interrupted.ConfirmRendered();
                Require(Count(interrupted, "GHOST_HOVER_RENDERED") == 0, interruption + " prevents stale rendering certification");
                int firstDecline = interrupted.Ledger.Count;
                for (int i = 0; i < 20; i++) interrupted.Update(0, 0.21, new PointF(100, 100), candidate, interruption != "override", interruption == "threat");
                Require(interrupted.Ledger.Count == firstDecline, interruption + " does not log the same refusal every frame");
            }

            GhostCuriosity departed = Hovering();
            departed.Update(0.01, 0.21, new PointF(300, 300), null, true, false);
            departed.Update(0.25, 0.46, new PointF(300, 300), null, true, false);
            departed.Update(0.2, 0.66, new PointF(300, 300), null, true, false);
            departed.Update(0, 0.67, new PointF(100, 100), Button(), true, false);
            Require(departed.State == "TARGET_DETECTED", "leaving a target permits a genuinely new visit without a residual freeze");

            GhostCuriosity reset = Hovering();
            reset.Reset("PAUSED");
            Require(!reset.HoldMovement && !reset.WantsGlow && !reset.GlowBounds.HasValue, "host pause reset immediately revokes all visual intent");
            Require(reset.Receipts.Count == 1 && reset.Receipts[0] == "OBSERVATION_DECLINED", "reset receipts do not retain an earlier visual request");
            int afterReset = reset.Ledger.Count;
            reset.Reset("PAUSED");
            Require(reset.Ledger.Count == afterReset, "unchanged host reset is deduplicated after its target has been discarded");
            reset.DeclineObservation("an application label must never enter the ledger");
            Require(reset.Ledger[reset.Ledger.Count - 1].Reason == "HOST_DECLINED", "host denial stores a classification instead of arbitrary text");

            foreach (GhostCandidate invalid in new GhostCandidate[] {
                new GhostCandidate(new RectangleF(float.NaN, 90, 80, 30), HoverControlKind.Button),
                new GhostCandidate(new RectangleF(90, 90, float.PositiveInfinity, 30), HoverControlKind.Button),
                new GhostCandidate(new RectangleF(90, 90, 601, 30), HoverControlKind.Button),
                new GhostCandidate(new RectangleF(90, 90, 80, 201), HoverControlKind.Button),
                new GhostCandidate(new RectangleF(90, 90, 7, 30), HoverControlKind.Button),
                new GhostCandidate(new RectangleF(90, 90, 80, 30), (HoverControlKind)100),
                new GhostCandidate(new RectangleF(200, 200, 80, 30), HoverControlKind.Button) })
            {
                GhostCuriosity rejected = new GhostCuriosity();
                rejected.Update(0, 0, new PointF(100, 100), invalid, true, false);
                Require(!rejected.HoldMovement && !rejected.WantsGlow && Count(rejected, "TARGET_OBSERVED") == 0,
                    "invalid geometry, kind or point containment is fail-closed");
            }
            GhostCuriosity badTime = Hovering();
            badTime.Update(double.NaN, 0.3, new PointF(100, 100), Button(), true, false);
            Require(!badTime.HoldMovement && !badTime.WantsGlow, "invalid elapsed time releases the observation safely");
            badTime = Hovering();
            badTime.Update(0.1, double.NaN, new PointF(100, 100), Button(), true, false);
            Require(!badTime.HoldMovement && !badTime.WantsGlow, "invalid timestamp cannot leave an old glow alive");
            badTime = Hovering();
            badTime.Update(0.1, 0.1, new PointF(100, 100), Button(), true, false);
            Require(!badTime.HoldMovement && !badTime.WantsGlow, "backward simulation time cannot retain a hold");

            GhostCuriosity bounded = new GhostCuriosity();
            for (int i = 0; i < 100; i++)
            {
                bounded.Update(0.25, i * 2, new PointF(100, 100), null, true, false);
                bounded.Update(0.25, i * 2 + 0.25, new PointF(100, 100), null, true, false);
                bounded.Update(0, i * 2 + 0.5, new PointF(100, 100), Button(), true, false);
                bounded.Update(0, i * 2 + 0.6, new PointF(100, 100), Button(), false, false);
            }
            Require(bounded.Ledger.Count == GhostCuriosity.HistoryLimit, "session history is bounded at 64 records");
            long previous = 0;
            foreach (GhostReceipt receipt in bounded.Ledger)
            {
                Require(receipt.Sequence > previous, "global sequence remains increasing after eviction");
                previous = receipt.Sequence;
            }
            Require(bounded.Ledger[0].Sequence > 1, "oldest records are evicted without resetting sequence");
            foreach (FieldInfo field in typeof(GhostReceipt).GetFields(BindingFlags.Public | BindingFlags.Instance))
                Require(field.Name == "Sequence" || field.Name == "Tick" || field.Name == "State" || field.Name == "Outcome" ||
                    field.Name == "Reason" || field.Name == "Kind", "receipts contain only approved lightweight classifications");
            return assertions;
        }
    }
}
