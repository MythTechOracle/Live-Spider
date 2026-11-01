using System;
using System.Collections.Generic;
using System.Drawing;

namespace SpookyDesktopSpider
{
    internal static class EcologyV11Checks
    {
        private static int count;
        private static readonly PointF Far = new PointF(-1000, -1000);
        private static readonly RectangleF Display = new RectangleF(20, 20, 1880, 1040);

        private static void Require(bool condition, string label)
        {
            count++;
            if (!condition) throw new InvalidOperationException("Native v11 check failed: " + label);
        }

        private static EcologyMotion Home()
        {
            EcologyMotion m = new EcologyMotion(new PointF(100, 100), Display, 11);
            m.CurrentWeb = new EcologyWeb { Anchor = new PointF(100, 100), Progress = 42, Interrupted = false };
            return m;
        }

        private static EcologyMotion Recovery()
        {
            EcologyMotion m = Home();
            m.Position = new PointF(800, 800);
            m.CurrentWeb.Interrupted = true;
            return m;
        }

        private static int Events(EcologyMotion m, string eventName)
        {
            int found = 0;
            foreach (EcologyReceipt receipt in m.Ledger) if (receipt.Event == eventName) found++;
            return found;
        }

        private static EcologyReceipt Last(EcologyMotion m, string eventName)
        {
            for (int i = m.Ledger.Count - 1; i >= 0; i--) if (m.Ledger[i].Event == eventName) return m.Ledger[i];
            throw new InvalidOperationException("Missing native v11 ledger event: " + eventName);
        }

        public static string Run()
        {
            count = 0;
            EcologyMotion m = Recovery();
            m.State = "WAIT_AT_ANCHOR";
            m.CurrentWeb.Interrupted = false;
            m.Update(Far, 0.1, 300, 1, 0);
            Require(m.CurrentWeb.Progress == 42 && !m.WeaveAuthorized, "a state label cannot authorize remote weaving");
            Require(Last(m, "WEAVE_DEFERRED").Reason == "NOT_AT_ANCHOR", "central gate measures actual anchor separation");
            Require(m.State == "RECOVER", "false anchor state resumes physical recovery");

            m = new EcologyMotion(new PointF(60, 60), Display, 12);
            m.Update(Far, 0.1, 300, 1, 0);
            Require(m.CurrentWeb != null && m.CurrentWeb.Progress > 0 && m.WeaveAuthorized, "new web requires fresh authorization");
            EcologyReceipt created = Last(m, "WEB_CREATED"), authorized = Last(m, "WEAVE_AUTHORIZED");
            Require(created.Evidence.AnchorDistance == 0 && !string.IsNullOrEmpty(created.Evidence.WebId), "creation receipt identifies a measured web");
            Require(created.Sequence < authorized.Sequence && created.Evidence.WebId == authorized.Evidence.WebId,
                "new web creation precedes authorization for that same web");
            Require(m.CurrentRouteId == null && Last(m, "ROUTE_RETIRED").Reason == "ANCHOR_REACHED", "anchor selection route retires on arrival");
            foreach (EcologyReceipt receipt in m.Ledger) Require(receipt.Timestamp == 0, "explicit timestamp zero is retained");
            EcologyReceipt construction = Last(m, "WEAVE_PROGRESS_CONFIRMED");
            Require(authorized.Sequence < construction.Sequence && construction.DecisionId == authorized.DecisionId,
                "construction effect follows its specific authorization decision");
            Require(construction.Evidence.ProgressAdded > 0 && construction.Evidence.Progress > construction.Evidence.PreviousProgress &&
                construction.Evidence.WebId == m.CurrentWebId, "progress receipt proves positive silk addition on the current web");
            int progressEvents = Events(m, "WEAVE_PROGRESS_CONFIRMED");
            for (int i = 0; i < 5; i++) m.Update(Far, 0.01, 300, 1);
            Require(Events(m, "WEAVE_PROGRESS_CONFIRMED") == progressEvents, "construction receipts are throttled while progress continues");
            m.Update(m.Position, 0, 300, 1);
            Require(Events(m, "WEAVE_PROGRESS_CONFIRMED") == progressEvents && !m.WeaveAuthorized,
                "revocation prevents new construction effects and receipts");

            m = Recovery();
            m.Update(Far, 0.1, 0, 1);
            string route = m.CurrentRouteId, decision = m.CurrentDecisionId;
            Require(!string.IsNullOrEmpty(route) && !string.IsNullOrEmpty(decision), "recovery receives route and decision identities");
            m.Update(new PointF(1500, 900), 0.1, 0, 1);
            Require(m.CurrentRouteId == route && m.CurrentDecisionId == decision, "clear pointer movement retains an unchanged route identity");
            bool returned = false;
            for (int i = 0; i < 100; i++)
            {
                m.Update(Far, 0.1, 0, 1);
                if (m.Receipts.Contains("RETURN_CONFIRMED")) { returned = true; break; }
                Require(m.CurrentRouteId == route, "one identity spans the full remaining recovery route");
            }
            Require(returned && m.CurrentWeb.Progress == 42 && !m.WeaveAuthorized, "physical homecoming is independent of weaving permission");
            Require(Last(m, "RETURN_CONFIRMED").RouteId == route && Last(m, "RETURN_CONFIRMED").DecisionId == decision,
                "verified return links to its executed route and decision");
            Require(Last(m, "WEAVE_DEFERRED").Reason == "IDLE_THRESHOLD_NOT_MET", "active return explicitly defers weaving");
            Require(m.CurrentRouteId == null, "completed recovery route is retired");
            foreach (EcologyReceipt receipt in m.Ledger)
                if (receipt.Event == "MOVE_CONFIRMED") Require(receipt.RouteId == route && receipt.Evidence.DistanceMoved > 0,
                    "movement receipt proves displacement under the executing route");

            m = Home();
            m.Update(Far, 0, 300, 1);
            Require(m.WeaveAuthorized, "safe measured anchor can authorize weaving");
            double progress = m.CurrentWeb.Progress;
            m.Update(m.Position, 0, 300, 1);
            Require(!m.WeaveAuthorized && m.CurrentWeb.Progress == progress && m.CurrentWeb.Interrupted, "threat revokes weaving before progress");
            int deferred = Events(m, "WEAVE_DEFERRED");
            for (int i = 0; i < 5; i++) m.Update(m.Position, 0, 300, 1);
            Require(Events(m, "WEAVE_DEFERRED") == deferred, "an unchanged threat deferral does not flood the ledger");

            m = Home();
            m.Update(Far, 0, 300, 1);
            string firstWeb = m.CurrentWebId;
            m.CurrentWeb = new EcologyWeb { Anchor = m.Position, Progress = 5 };
            Require(!m.CanWeave(Far, 300, 1), "replacing a web invalidates direct stale authorization");
            m.Update(Far, 0.1, 300, 1);
            Require(m.CurrentWebId != firstWeb && m.WeaveAuthorized, "replacement web obtains its own fresh authorization");
            Require(Last(m, "WEAVE_AUTHORIZED").Evidence.WebId == m.CurrentWebId, "authorization receipt identifies replacement web");
            int authCount = Events(m, "WEAVE_AUTHORIZED");
            m.SetBounds(new RectangleF(20, 20, 1800, 900));
            Require(!m.WeaveAuthorized && m.CurrentRouteId == null, "resized usable bounds revoke the previous permission and route");
            m.Update(Far, 0, 300, 1);
            Require(m.WeaveAuthorized && Events(m, "WEAVE_AUTHORIZED") == authCount + 1, "changed bounds require a new gate decision");

            m = Home();
            for (int i = 0; i < 5; i++) m.Update(Far, 0.1, 0, 1);
            Require(Events(m, "WEAVE_DEFERRED") == 1, "five unchanged idle deferrals produce one event");
            Require(Last(m, "WEAVE_DEFERRED").Reason == "IDLE_THRESHOLD_NOT_MET", "deferral records its actual reason");

            m = Recovery();
            m.Update(new PointF(450, 450), 0.1, 300, 1);
            string blocked = m.CurrentDecisionId;
            Require(m.CurrentRouteId == null && !string.IsNullOrEmpty(blocked), "a blocked decision is not an approved route");
            Require(Last(m, "ROUTE_BLOCKED").RouteId == null && Last(m, "ROUTE_BLOCKED").DecisionId == blocked,
                "blocked ledger receipt uses only its rejected decision identity");
            m.Update(new PointF(450, 450), 0.1, 300, 1);
            Require(m.CurrentDecisionId == blocked && m.CurrentRouteId == null, "remembered blockage retains its decision");

            m = Recovery();
            m.Update(Far, 0.1, 300, 1);
            route = m.CurrentRouteId;
            PointF stopped = m.Position;
            m.CancelMovement("TEST_CANCELLED");
            Require(!m.Moving && m.Speed == 0 && m.CurrentRouteId == null, "cancellation immediately retires motion intent");
            Require(Last(m, "ROUTE_CANCELLED").RouteId == route, "cancellation names the retired route");
            m.Update(Far, 0.1, 300, 1);
            Require(m.Position == stopped && m.DistanceMoved == 0 && !m.Receipts.Contains("MOVE_CONFIRMED"), "cancelled intent cannot execute on the next tick");
            m.Update(Far, 0.1, 300, 1);
            Require(m.CurrentRouteId != null && m.CurrentRouteId != route, "later recovery requires a new route identity");

            m = Home();
            for (int i = 0; i < 450; i++) m.Update(Far, 0, i % 2 == 0 ? 300 : 0, 1, i);
            Require(m.Ledger.Count == 200, "structured ledger is bounded at 200 events");
            long sequence = 0, tick = 0;
            foreach (EcologyReceipt receipt in m.Ledger)
            {
                Require(receipt.Sequence > sequence && receipt.Tick >= tick, "receipt sequence and tick are monotonic after eviction");
                Require(!double.IsNaN(receipt.Timestamp) && !double.IsInfinity(receipt.Timestamp), "receipt timestamp is finite");
                Require(!receipt.Evidence.AnchorDistance.HasValue || !double.IsNaN(receipt.Evidence.AnchorDistance.Value), "missing anchors use nullable evidence rather than NaN");
                sequence = receipt.Sequence; tick = receipt.Tick;
            }
            bool invalid = false;
            m.Position = new PointF(float.NaN, 0);
            try { m.CanWeave(Far, 300, 1); } catch (ArgumentException) { invalid = true; }
            Require(invalid, "standalone central gate rejects nonfinite positions");
            m = Home();
            m.CurrentWeb.Progress = double.NaN;
            invalid = false;
            try { m.CanWeave(Far, 300, 1); } catch (ArgumentException) { invalid = true; }
            Require(invalid, "standalone gate rejects invalid web progress");
            m = Recovery();
            PointF unchanged = m.Position;
            m.Target = new PointF(float.NaN, 100);
            invalid = false;
            try { m.Update(Far, 0.1, 300, 1); } catch (ArgumentException) { invalid = true; }
            Require(invalid && m.Position == unchanged && m.Tick == 0, "invalid mutable target is rejected before physical effect or tick mutation");
            m = Recovery();
            m.Speed = -1;
            invalid = false;
            try { m.Update(Far, 0.1, 300, 1); } catch (ArgumentException) { invalid = true; }
            Require(invalid && m.Tick == 0, "negative mutable speed is rejected before mutation");
            m = Recovery();
            m.Rotation = double.PositiveInfinity;
            invalid = false;
            try { m.Update(Far, 0.1, 300, 1); } catch (ArgumentException) { invalid = true; }
            Require(invalid && m.Tick == 0, "nonfinite mutable rotation is rejected before mutation");
            m = Home();
            m.Update(m.Position, 0, 300, 1);
            string oldEscape = m.CurrentRouteId;
            m.ClearWeb();
            Require(m.CurrentRouteId == null && !m.Moving && m.Speed == 0, "clearing a web also stops the cancelled escape plan");
            m.Update(m.Position, 0.1, 0, 1);
            Require(m.CurrentRouteId != oldEscape || m.CurrentRouteId == null, "any fresh escape after clearing uses a new route");
            m = new EcologyMotion(new PointF(500, 500), Display, 19);
            PointF stationaryThreat = m.Position;
            m.Update(stationaryThreat, 0.01, 0, 1);
            string originalEscape = m.CurrentRouteId;
            Require(!string.IsNullOrEmpty(originalEscape), "target-lineage scenario starts with an approved escape");
            unchanged = m.Position;
            m.Target = m.Clamp(new PointF(m.Position.X + 120, m.Position.Y - 50));
            m.Update(stationaryThreat, 0.01, 0, 1);
            Require(m.Position == unchanged && m.DistanceMoved == 0 && !m.Receipts.Contains("MOVE_CONFIRMED"),
                "mutated finite target cannot move under the previous escape lease");
            Require(m.CurrentRouteId == null && Last(m, "ROUTE_CANCELLED").RouteId == originalEscape &&
                Last(m, "ROUTE_CANCELLED").Reason == "EXECUTION_INTENT_CHANGED", "changed execution intent explicitly retires its old lineage");
            m.Update(stationaryThreat, 0.01, 0, 1);
            Require(m.CurrentRouteId != null && m.CurrentRouteId != originalEscape && m.DistanceMoved > 0,
                "fresh escape after target mutation requires a new executing route");
            Require(Last(m, "MOVE_CONFIRMED").RouteId == m.CurrentRouteId, "fresh movement links only to the replacement lease");
            return "PASS: native v11 " + count + " assertions; central weaving authorization, identity revocation, stable route/decision IDs, " +
                "active homecoming, cancellation, transition-only deferrals, injected zero clock, and bounded structured ledger.";
        }
    }
}
