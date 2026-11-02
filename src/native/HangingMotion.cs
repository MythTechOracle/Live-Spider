using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;

namespace SpookyDesktopSpider
{
    internal sealed class HangingReceipt
    {
        public readonly long Sequence, Tick;
        public readonly double Timestamp, DistanceMoved, AnchorDistance;
        public readonly string State, Outcome, Reason, RouteId, DecisionId, WebId;
        public readonly PointF From, To;
        internal HangingReceipt(long sequence, long tick, double timestamp, string state, string outcome, string reason,
            string route, string decision, string web, PointF from, PointF to, double moved, double anchorDistance)
        {
            Sequence = sequence; Tick = tick; Timestamp = timestamp; State = state; Outcome = outcome; Reason = reason;
            RouteId = route; DecisionId = decision; WebId = web; From = from; To = to;
            DistanceMoved = moved; AnchorDistance = anchorDistance;
        }
    }

    internal sealed class HangingIntent
    {
        public PointF Goal;
        public double Speed;
        public bool Egress;
    }

    // Pure suspension controller. It proposes routes; EcologyMotion alone owns
    // route approval, real displacement, collision checking, and position.
    internal sealed class HangingMotion
    {
        public const int LedgerLimit = 100;
        public const double CooldownSeconds = 15;
        private const double Epsilon = 0.01;
        private readonly List<HangingReceipt> ledger = new List<HangingReceipt>();
        private readonly List<string> receipts = new List<string>();
        private readonly Action<HangingReceipt> record;
        private EcologyWeb web;
        private string webId, routeId, decisionId;
        private string impactRouteId, impactDecisionId;
        private RectangleF bounds;
        private PointF anchor, bottom, before, actual, cursor;
        private double savedScale, savedDepth, cooldownUntil, settleAge, timestamp, clearance, simulationNow;
        private long sequence, tick;
        private bool pendingDescent, pendingMethod, descentMoved, returnMoved, impactAcknowledged;
        private string pendingReason, lastAssessment;

        public HangingMotion(Action<HangingReceipt> recordReceipt)
        {
            record = recordReceipt;
            State = "DISABLED";
            Ledger = ledger.AsReadOnly();
            Receipts = receipts.AsReadOnly();
        }

        public string State { get; private set; }
        public ReadOnlyCollection<HangingReceipt> Ledger { get; private set; }
        public ReadOnlyCollection<string> Receipts { get; private set; }
        public bool Active { get { return State == "PREPARE_DESCENT" || State == "DESCENDING" || State == "HANGING" ||
            State == "THREAT_ASSESS" || State == "CLIMB_HOME" || State == "SNAP_HOME" || State == "WEB_IMPACT" || State == "SETTLING"; } }
        public bool TetherVisible { get { return State == "PREPARE_DESCENT" || State == "DESCENDING" || State == "HANGING" ||
            State == "THREAT_ASSESS" || State == "CLIMB_HOME" || State == "SNAP_HOME"; } }
        public bool HangingAnimation { get { return State == "HANGING"; } }
        public bool WebImpactPending { get { return State == "WEB_IMPACT" && !impactAcknowledged; } }
        public PointF Anchor { get { return anchor; } }
        public PointF Position { get { return actual; } }
        public double WebBounceOffset
        {
            get { return (State == "WEB_IMPACT" || State == "SETTLING") && returnMoved ?
                6 * savedScale * Math.Sin(settleAge * 18) * Math.Exp(-settleAge * 5) : 0; }
        }

        public void BeginFrame(long frameTick, double frameTimestamp, double now, PointF position, PointF pointer, double safetyClearance)
        {
            tick = frameTick; timestamp = frameTimestamp; simulationNow = now;
            before = actual = position; cursor = pointer; clearance = safetyClearance;
            receipts.Clear();
        }

        public void SetRoute(string route, string decision)
        {
            SetRouteContext(route, decision);
            if (pendingDescent)
            {
                Emit("SILK_ANCHOR_CONFIRMED", "CURRENT_WEB_IDENTITY_AND_MEASURED_ANCHOR");
                Emit("DESCENT_AUTHORIZED", "FULL_DESCENT_CORRIDOR_CLEAR");
                pendingDescent = false;
            }
            if (pendingMethod)
            {
                Emit("RETURN_METHOD_SELECTED", pendingReason);
                pendingMethod = false;
            }
        }

        private void SetRouteContext(string route, string decision)
        {
            bool impact = State == "WEB_IMPACT" || State == "SETTLING";
            routeId = impact && route == null ? impactRouteId : route;
            decisionId = impact && route == null ? impactDecisionId : decision;
        }

        private void Emit(string outcome, string reason)
        {
            if (!receipts.Contains(outcome)) receipts.Add(outcome);
            HangingReceipt receipt = new HangingReceipt(++sequence, tick, timestamp, State, outcome, reason, routeId, decisionId, webId,
                before, actual, EcologyMotion.Distance(before, actual), EcologyMotion.Distance(actual, anchor));
            ledger.Add(receipt);
            if (ledger.Count > LedgerLimit) ledger.RemoveAt(0);
            if (record != null) record(receipt);
        }

        public bool Cancel(string reason, PointF position, double now)
        {
            cooldownUntil = Math.Max(cooldownUntil, now + CooldownSeconds);
            actual = before = position;
            if (!Active) return false;
            State = "RELEASE_AND_ESCAPE";
            pendingDescent = pendingMethod = impactAcknowledged = false;
            settleAge = 0;
            Emit("TETHER_RELEASED", string.IsNullOrEmpty(reason) ? "CANCELLED" : reason);
            routeId = decisionId = null;
            return true;
        }

        public static bool IsSafeEgress(PointF start, PointF goal, PointF pointer, double safetyClearance)
        {
            if (EcologyMotion.Distance(start, goal) <= Epsilon || EcologyMotion.Distance(goal, pointer) + Epsilon < safetyClearance) return false;
            double vx = (double)goal.X - start.X, vy = (double)goal.Y - start.Y;
            double awayX = (double)start.X - pointer.X, awayY = (double)start.Y - pointer.Y;
            return awayX * vx + awayY * vy >= -Epsilon &&
                EcologyMotion.Distance(goal, pointer) > EcologyMotion.Distance(start, pointer) + Epsilon;
        }

        public static bool IsSafeEgressStep(PointF start, PointF candidate, PointF goal, PointF pointer, double safetyClearance)
        {
            return IsSafeEgress(start, goal, pointer, safetyClearance) &&
                EcologyMotion.Distance(candidate, pointer) + Epsilon >= EcologyMotion.Distance(start, pointer) &&
                EcologyMotion.SegmentDistance(start, candidate, pointer) + Epsilon >= EcologyMotion.Distance(start, pointer);
        }

        private bool SameTether(EcologyWeb current, string currentId, RectangleF currentBounds, double scale)
        {
            return current != null && object.ReferenceEquals(current, web) && currentId == webId && current.Id == webId &&
                EcologyMotion.Distance(current.Anchor, anchor) <= Epsilon && currentBounds == bounds && Math.Abs(scale - savedScale) <= Epsilon;
        }

        private static bool At(PointF a, PointF b) { return EcologyMotion.Distance(a, b) <= Epsilon; }
        private static bool InBounds(PointF point, RectangleF area)
        {
            return point.X >= area.Left && point.X <= area.Right && point.Y >= area.Top && point.Y <= area.Bottom;
        }

        private bool SafeReturn(PointF position, PointF pointer, bool strong, out bool egress)
        {
            egress = false;
            if (At(position, anchor) || EcologyMotion.Distance(anchor, pointer) + Epsilon < clearance) return false;
            if (EcologyMotion.SegmentDistance(position, anchor, pointer) + Epsilon >= clearance) return true;
            egress = strong && IsSafeEgress(position, anchor, pointer, clearance);
            return egress;
        }

        public HangingIntent Prepare(bool enabled, double idleThreshold, double depth, double idle, double scale, double now,
            PointF position, PointF pointer, RectangleF area, EcologyWeb currentWeb, string currentWebId, bool identityValid)
        {
            actual = position; cursor = pointer; simulationNow = now;
            if (!enabled)
            {
                if (Active) Cancel("DISABLED", position, now);
                if (!Active && State != "RELEASE_AND_ESCAPE") State = "DISABLED";
                return null;
            }
            if (Active && (!SameTether(currentWeb, currentWebId, area, scale) || !identityValid || !InBounds(position, area)))
            { Cancel("TETHER_IDENTITY_OR_BOUNDS_CHANGED", position, now); return null; }
            if (Active && Math.Abs(savedDepth - depth) > Epsilon)
            { Cancel("TETHER_DEPTH_CHANGED", position, now); return null; }
            if (!Active)
            {
                if (now < cooldownUntil) { State = "REST_COOLDOWN"; return null; }
                if (currentWeb == null || !identityValid || currentWeb.Id != currentWebId || currentWeb.Progress <= 0 ||
                    currentWeb.Interrupted || !At(position, currentWeb.Anchor)) { State = "DISABLED"; return null; }
                State = "IDLE_AT_WEB";
                if (idle < idleThreshold) return null;
                PointF home = currentWeb.Anchor;
                double cornerX = Math.Min(160 * scale, area.Width / 4), cornerY = Math.Min(120 * scale, area.Height / 4);
                if (home.Y > area.Top + cornerY || (home.X > area.Left + cornerX && home.X < area.Right - cornerX)) return null;
                PointF proposedBottom = new PointF(home.X, home.Y + (float)(depth * scale));
                if (!InBounds(home, area) || !InBounds(proposedBottom, area) ||
                    EcologyMotion.SegmentDistance(home, proposedBottom, pointer) + Epsilon < clearance) return null;
                web = currentWeb; webId = currentWebId; anchor = home; bottom = proposedBottom; bounds = area; savedScale = scale; savedDepth = depth;
                State = "PREPARE_DESCENT";
                pendingDescent = true; descentMoved = returnMoved = impactAcknowledged = false;
                pendingMethod = false; lastAssessment = null; settleAge = 0;
                return new HangingIntent { Goal = bottom, Speed = 0 };
            }

            double threatDistance = EcologyMotion.Distance(position, pointer);
            bool strong = threatDistance <= 150 * scale, moderate = threatDistance <= 240 * scale;
            if (State == "WEB_IMPACT" || State == "SETTLING")
            {
                if (!At(position, anchor) || threatDistance + Epsilon < clearance)
                { Cancel("THREAT_OR_POSITION_CHANGED_AT_HOME", position, now); return null; }
                return new HangingIntent { Goal = position, Speed = 0 };
            }
            bool returning = State == "CLIMB_HOME" || State == "SNAP_HOME";
            if (strong || moderate || idle < idleThreshold || returning)
            {
                bool chooseSnap = strong || State == "SNAP_HOME";
                if (!returning || (strong && State != "SNAP_HOME"))
                {
                    State = "THREAT_ASSESS";
                    string assessment = strong ? "STRONG" : moderate ? "MODERATE" : "ACTIVITY_RESUMED";
                    if (assessment != lastAssessment) { Emit("THREAT_ASSESSED", assessment); lastAssessment = assessment; }
                }
                bool egress;
                if (!SafeReturn(position, pointer, chooseSnap, out egress))
                { Cancel("RETURN_CORRIDOR_UNSAFE_OR_NO_DISPLACEMENT", position, now); return null; }
                string selected = chooseSnap ? "SNAP_HOME" : "CLIMB_HOME";
                if (!returning || State != selected)
                { pendingMethod = true; pendingReason = selected + (egress ? "_VERIFIED_EGRESS" : "_CLEAR_CORRIDOR"); }
                State = selected;
                return new HangingIntent { Goal = anchor, Speed = (chooseSnap ? 620 : 180) * scale, Egress = egress };
            }
            if (State == "PREPARE_DESCENT") State = "DESCENDING";
            if (State == "DESCENDING")
            {
                if (EcologyMotion.SegmentDistance(position, bottom, pointer) + Epsilon < clearance)
                { Cancel("DESCENT_CORRIDOR_BLOCKED", position, now); return null; }
                return new HangingIntent { Goal = bottom, Speed = 70 * scale };
            }
            if (State == "HANGING")
            {
                if (EcologyMotion.SegmentDistance(anchor, position, pointer) + Epsilon < clearance)
                { Cancel("TETHER_CORRIDOR_BLOCKED", position, now); return null; }
                return new HangingIntent { Goal = position, Speed = 0 };
            }
            return null;
        }

        public bool VerifyEffect(PointF position, double dt, double now, double postThreatDistance, string route, string decision)
        {
            actual = position; SetRouteContext(route, decision); simulationNow = now;
            double moved = EcologyMotion.Distance(before, actual);
            if (State == "DESCENDING")
            {
                if (moved > Epsilon && !descentMoved) { descentMoved = true; Emit("DESCENT_CONFIRMED", "MEASURED_DOWNWARD_DISPLACEMENT"); }
                if (descentMoved && At(actual, bottom)) { State = "HANGING"; Emit("HANGING_CONFIRMED", "MEASURED_DESCENT_ENDPOINT"); }
            }
            else if (State == "CLIMB_HOME" || State == "SNAP_HOME")
            {
                if (moved > Epsilon) returnMoved = true;
                if (returnMoved && moved > Epsilon && At(actual, anchor) && postThreatDistance + Epsilon >= clearance)
                {
                    Emit("RETURN_CONFIRMED", "MEASURED_SAFE_ANCHOR_ARRIVAL");
                    impactRouteId = routeId; impactDecisionId = decisionId;
                    State = "WEB_IMPACT";
                    impactAcknowledged = false;
                    return true;
                }
            }
            else if (State == "WEB_IMPACT" || (State == "SETTLING" && impactAcknowledged))
            {
                settleAge += dt;
                if (State == "SETTLING" && settleAge >= 0.7)
                {
                    State = "REST_COOLDOWN"; cooldownUntil = now + CooldownSeconds;
                    Emit("SETTLED_AT_HOME", "ACKNOWLEDGED_WEB_IMPACT_SETTLED");
                }
            }
            return false;
        }

        public bool ContextMatches(EcologyWeb currentWeb, string currentWebId, RectangleF currentBounds, double currentScale, double currentDepth)
        {
            return SameTether(currentWeb, currentWebId, currentBounds, currentScale) && Math.Abs(currentDepth - savedDepth) <= Epsilon;
        }

        public bool ConfirmImpact(PointF position, EcologyWeb currentWeb, string currentWebId,
            RectangleF currentBounds, double currentScale, double currentDepth)
        {
            actual = position;
            if (!WebImpactPending || !ContextMatches(currentWeb, currentWebId, currentBounds, currentScale, currentDepth) || !At(actual, anchor) ||
                EcologyMotion.Distance(actual, cursor) + Epsilon < clearance) return false;
            impactAcknowledged = true;
            Emit("WEB_IMPACT_CONFIRMED", "HOST_ACKNOWLEDGED_PHYSICAL_HOME_IMPACT_RENDER");
            State = "SETTLING";
            return true;
        }
    }
}
