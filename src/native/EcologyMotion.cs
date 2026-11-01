using System;
using System.Collections.Generic;
using System.Drawing;

namespace SpookyDesktopSpider
{
    internal sealed class EcologyWeb
    {
        public PointF Anchor;
        public double Progress;
        public bool Interrupted;
        internal string Identity;
        public string Id { get { return Identity; } }
    }

    internal sealed class EcologyEvidence
    {
        public double DistanceMoved, PreThreatDistance, PostThreatDistance, IdleSeconds, WeaveThreshold;
        public double? AnchorDistance;
        public double? PreviousProgress, Progress, ProgressAdded;
        public bool WeaveAuthorized;
        public string WebId, State, RouteStatus;
    }

    internal sealed class EcologyReceipt
    {
        public long Sequence, Tick;
        public string Event, Outcome, RouteId, DecisionId, Reason;
        public double Timestamp;
        public PointF From, To;
        public EcologyEvidence Evidence;
    }

    // Intent, effect, and verification are separate even in this conservative
    // navigator: it waits at a blocked corridor instead of retrying it blindly.
    internal sealed class EcologyMotion
    {
        private const double Epsilon = 0.01;
        private const int MemoryLimit = 16;
        private const double MemorySeconds = 3;
        private readonly Random random;
        private readonly List<RejectedCorridor> rejectedRoutes = new List<RejectedCorridor>();
        private readonly Dictionary<string, int> activity = new Dictionary<string, int>();
        private readonly Queue<string> activityOrder = new Queue<string>();
        private bool hasCursor, hasHeatCursor, dangerEpisode, hasBeenAway;
        private PointF previousCursor, lastHeatCursor;
        private string waitingFor;
        private double previousPostDistance = double.PositiveInfinity;
        private double now, nextEscapeDecision;
        private double wantedRotation;
        private long receiptSequence, decisionSequence, routeSequence, webSequence;
        private double ledgerTimestamp, frameIdle, frameClearance;
        private PointF frameStart, frameCursor;
        private EcologyWeb observedWeb;
        private PointF observedAnchor;
        private string lastWeaveDecision, weaveDecisionId;
        private string progressWebId, progressDecisionId;
        private double lastProgressAt = double.NegativeInfinity;
        private RouteLease approvedRoute;
        private bool holdCancelledMovement;

        private sealed class RouteLease
        {
            public string Id, DecisionId, Kind;
            public PointF Start, Goal;
            public RectangleF Bounds;
            public EcologyWeb Web;
            public PointF Anchor;
            public double Clearance;
        }

        private sealed class RejectedCorridor
        {
            public PointF Start, End, Cursor;
            public double ExpiresAt, Clearance;
            public string DecisionId;
        }

        public PointF Position;
        public PointF Target;
        public PointF IntendedTarget;
        public RectangleF Bounds;
        public double Rotation;
        public double Speed;
        public string State = "ACTIVE_EVASION";
        public EcologyWeb CurrentWeb;
        public string MotionStatus = "IDLE";
        public string RouteStatus = "NOT_REQUIRED";
        public string BlockedReason;
        public double DistanceMoved;
        public double PreThreatDistance;
        public double PostThreatDistance;
        public readonly List<string> Receipts = new List<string>();
        public double WanderThreshold = 120;
        public double WeaveThreshold = 300;
        public string CurrentRouteId { get; private set; }
        public string CurrentDecisionId { get; private set; }
        public string CurrentWebId { get; private set; }
        public bool WeaveAuthorized { get; private set; }
        public long Tick { get; private set; }
        public readonly List<EcologyReceipt> Ledger = new List<EcologyReceipt>();
        public int RejectedRouteCount { get { return rejectedRoutes.Count; } }
        public int RouteChecks { get; private set; }
        public bool Moving { get { return Speed > 0 && Distance(Position, Target) > Epsilon; } }

        public EcologyMotion(PointF start, RectangleF bounds, int seed)
        {
            ValidatePoint(start, "start");
            ValidateBounds(bounds);
            Bounds = bounds;
            random = new Random(seed);
            Position = Target = IntendedTarget = Clamp(start);
        }

        private static bool Finite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static void ValidatePoint(PointF point, string label)
        {
            if (!Finite(point.X) || !Finite(point.Y)) throw new ArgumentException(label + " must have finite coordinates.");
        }

        private static void ValidateBounds(RectangleF bounds)
        {
            if (!Finite(bounds.Left) || !Finite(bounds.Top) || !Finite(bounds.Width) || !Finite(bounds.Height) ||
                !Finite(bounds.Right) || !Finite(bounds.Bottom) || bounds.Width <= 0 || bounds.Height <= 0)
                throw new ArgumentException("Usable display bounds must be finite and positive.");
        }

        public static double Distance(PointF a, PointF b)
        {
            double x = (double)a.X - b.X, y = (double)a.Y - b.Y;
            return Math.Sqrt(x * x + y * y);
        }

        public PointF Clamp(PointF point)
        {
            ValidatePoint(point, "point");
            return new PointF(Math.Max(Bounds.Left, Math.Min(Bounds.Right, point.X)),
                Math.Max(Bounds.Top, Math.Min(Bounds.Bottom, point.Y)));
        }

        public static double SegmentDistance(PointF start, PointF end, PointF point)
        {
            double x = (double)end.X - start.X, y = (double)end.Y - start.Y;
            double denominator = x * x + y * y;
            if (denominator <= Epsilon * Epsilon) return Distance(start, point);
            double fraction = (((double)point.X - start.X) * x + ((double)point.Y - start.Y) * y) / denominator;
            fraction = Math.Max(0, Math.Min(1, fraction));
            double dx = point.X - (start.X + fraction * x), dy = point.Y - (start.Y + fraction * y);
            return Math.Sqrt(dx * dx + dy * dy);
        }

        public void SetBounds(RectangleF bounds)
        {
            ValidateBounds(bounds);
            if (Bounds == bounds) return;
            EndRoute("ROUTE_CANCELLED", "BOUNDS_CHANGED");
            RevokeWeave("BOUNDS_CHANGED");
            Bounds = bounds;
            Position = Clamp(Position);
            Target = Clamp(Target);
            IntendedTarget = Clamp(IntendedTarget);
            if (CurrentWeb != null)
            {
                PointF anchor = Clamp(CurrentWeb.Anchor);
                if (anchor != CurrentWeb.Anchor) CurrentWeb.Interrupted = true;
                CurrentWeb.Anchor = anchor;
                hasBeenAway = Distance(Position, anchor) > Epsilon;
            }
            rejectedRoutes.Clear();
            Speed = 0;
            Target = Position;
            State = "ACTIVE_EVASION";
        }

        public void Recall(PointF position, RectangleF bounds)
        {
            ValidatePoint(position, "recall position");
            EndRoute("ROUTE_CANCELLED", "RECALLED");
            RevokeWeave("RECALLED");
            SetBounds(bounds);
            Position = Target = IntendedTarget = Clamp(position);
            ClearWeb();
            hasCursor = dangerEpisode = false;
            previousPostDistance = double.PositiveInfinity;
            Speed = 0;
            State = "ACTIVE_EVASION";
            Receipts.Clear();
            MotionStatus = "IDLE";
            RouteStatus = "NOT_REQUIRED";
            DistanceMoved = 0;
        }

        public void ClearWeb()
        {
            EndRoute("ROUTE_CANCELLED", "WEB_CLEARED");
            RevokeWeave("WEB_CLEARED");
            CurrentWeb = null;
            observedWeb = null;
            CurrentWebId = null;
            hasBeenAway = false;
            rejectedRoutes.Clear();
            State = "ACTIVE_EVASION";
            Speed = 0;
            Target = IntendedTarget = Position;
        }

        private void Receipt(string status)
        {
            if (Receipts.Contains(status)) return;
            Receipts.Add(status);
            Record(status, status, null, CurrentRouteId, CurrentDecisionId, frameStart, Position);
        }

        private string NewDecision() { return "decision-" + (++decisionSequence).ToString("D6"); }

        private void Record(string eventName, string outcome, string reason, string routeId, string decisionId, PointF from, PointF to,
            double? previousProgress = null, double? progress = null, double? progressAdded = null)
        {
            EcologyEvidence evidence = new EcologyEvidence {
                DistanceMoved = DistanceMoved, PreThreatDistance = PreThreatDistance, PostThreatDistance = Distance(Position, frameCursor),
                AnchorDistance = CurrentWeb == null ? (double?)null : Distance(Position, CurrentWeb.Anchor),
                IdleSeconds = frameIdle, WeaveThreshold = WeaveThreshold, WeaveAuthorized = WeaveAuthorized,
                WebId = CurrentWebId, State = State, RouteStatus = RouteStatus,
                PreviousProgress = previousProgress, Progress = progress, ProgressAdded = progressAdded };
            Ledger.Add(new EcologyReceipt { Sequence = ++receiptSequence, Tick = Tick, Event = eventName, Outcome = outcome,
                RouteId = routeId, DecisionId = decisionId, Timestamp = ledgerTimestamp, Reason = reason,
                From = from, To = to, Evidence = evidence });
            if (Ledger.Count > 200) Ledger.RemoveAt(0);
        }

        private void EndRoute(string outcome, string reason)
        {
            if (approvedRoute != null)
                Record(outcome, outcome, reason, approvedRoute.Id, approvedRoute.DecisionId, Position, Position);
            else if (CurrentDecisionId != null && CurrentDecisionId != weaveDecisionId)
                Record("DECISION_RETIRED", "DECISION_RETIRED", reason, null, CurrentDecisionId, Position, Position);
            approvedRoute = null;
            CurrentRouteId = null;
            CurrentDecisionId = null;
        }

        public void CancelMovement(string reason)
        {
            EndRoute("ROUTE_CANCELLED", string.IsNullOrEmpty(reason) ? "CANCELLED" : reason);
            RevokeWeave("MOVEMENT_CANCELLED");
            Speed = 0;
            Target = IntendedTarget = Position;
            State = "ACTIVE_EVASION";
            holdCancelledMovement = true;
        }

        private void ApproveRoute(PointF goal, string kind, double clearance)
        {
            bool same = approvedRoute != null && approvedRoute.Kind == kind && approvedRoute.Bounds == Bounds &&
                Distance(approvedRoute.Goal, goal) <= Epsilon && Math.Abs(approvedRoute.Clearance - clearance) <= Epsilon &&
                object.ReferenceEquals(approvedRoute.Web, CurrentWeb) &&
                (CurrentWeb == null || Distance(approvedRoute.Anchor, CurrentWeb.Anchor) <= Epsilon);
            if (same) return;
            EndRoute("ROUTE_CANCELLED", "REPLANNED");
            RevokeWeave("ROUTE_STARTED");
            string decision = NewDecision();
            approvedRoute = new RouteLease { Id = "route-" + (++routeSequence).ToString("D6"), DecisionId = decision,
                Kind = kind, Start = Position, Goal = goal, Bounds = Bounds, Web = CurrentWeb,
                Anchor = CurrentWeb == null ? Position : CurrentWeb.Anchor, Clearance = clearance };
            CurrentRouteId = approvedRoute.Id;
            CurrentDecisionId = decision;
            Record("ROUTE_SELECTED", "ROUTE_APPROVED", kind, CurrentRouteId, decision, Position, goal);
        }

        private bool ExecutedIntentMatchesLease(double clearance)
        {
            return approvedRoute != null && approvedRoute.Kind == State && approvedRoute.Bounds == Bounds &&
                Distance(approvedRoute.Goal, Target) <= Epsilon && Distance(approvedRoute.Goal, IntendedTarget) <= Epsilon &&
                Math.Abs(approvedRoute.Clearance - (State == "ESCAPE" ? 0 : clearance)) <= Epsilon &&
                object.ReferenceEquals(approvedRoute.Web, CurrentWeb) &&
                (CurrentWeb == null || Distance(approvedRoute.Anchor, CurrentWeb.Anchor) <= Epsilon) &&
                SegmentDistance(approvedRoute.Start, approvedRoute.Goal, Position) <= Epsilon;
        }

        private void RevokeWeave(string reason)
        {
            bool wasAuthorized = WeaveAuthorized;
            WeaveAuthorized = false;
            if (wasAuthorized)
            {
                weaveDecisionId = NewDecision();
                lastWeaveDecision = (CurrentWebId ?? "none") + "|" + reason;
                Record("WEAVE_DEFERRED", "WEAVE_DEFERRED", reason, null, weaveDecisionId, Position, Position);
                if (!Receipts.Contains("WEAVE_DEFERRED")) Receipts.Add("WEAVE_DEFERRED");
            }
            else if (reason != "THREAT_NEARBY") lastWeaveDecision = null;
        }

        private void ObserveWeb()
        {
            bool changed = !object.ReferenceEquals(observedWeb, CurrentWeb);
            bool moved = !changed && CurrentWeb != null && Distance(observedAnchor, CurrentWeb.Anchor) > Epsilon;
            if (!changed && !moved) return;
            EndRoute("ROUTE_CANCELLED", changed ? "WEB_CHANGED" : "ANCHOR_CHANGED");
            Speed = 0;
            Target = IntendedTarget = Position;
            State = "ACTIVE_EVASION";
            RevokeWeave(changed ? "WEB_CHANGED" : "ANCHOR_CHANGED");
            observedWeb = CurrentWeb;
            rejectedRoutes.Clear();
            if (CurrentWeb == null) { CurrentWebId = null; return; }
            if (changed) CurrentWeb.Identity = "web-" + (++webSequence).ToString("D6");
            CurrentWebId = CurrentWeb.Identity;
            observedAnchor = CurrentWeb.Anchor;
            hasBeenAway = Distance(Position, CurrentWeb.Anchor) > Epsilon;
            string decision = NewDecision();
            Record(changed ? "WEB_OBSERVED" : "ANCHOR_CHANGED", "WEB_CURRENT", null, null, decision, Position, CurrentWeb.Anchor);
        }

        // This is the sole authority for entering WEAVE and for increasing silk.
        // State names, intended targets, and old permissions are never evidence.
        public bool CanWeave(PointF cursor, double idleSeconds, double scale)
        {
            ValidatePoint(cursor, "cursor");
            ValidatePoint(Position, "position");
            ValidateBounds(Bounds);
            if (!Finite(idleSeconds) || !Finite(scale) || !Finite(162 * scale) || scale <= 0 || !Finite(WeaveThreshold) || WeaveThreshold < 0)
                throw new ArgumentException("Weaving inputs must be finite with positive scale and nonnegative threshold.");
            if (CurrentWeb != null && (!Finite(CurrentWeb.Progress) || CurrentWeb.Progress < 0))
                throw new ArgumentException("Web progress must be finite and nonnegative.");
            if (CurrentWeb != null) ValidatePoint(CurrentWeb.Anchor, "web anchor");
            frameCursor = cursor;
            frameIdle = Math.Max(0, idleSeconds);
            string reason = null;
            if (CurrentWeb == null) reason = "NO_WEB";
            else if (!object.ReferenceEquals(observedWeb, CurrentWeb) || CurrentWebId != CurrentWeb.Identity) reason = "WEB_IDENTITY_CHANGED";
            else if (!Finite(CurrentWeb.Anchor.X) || !Finite(CurrentWeb.Anchor.Y) || Clamp(CurrentWeb.Anchor) != CurrentWeb.Anchor) reason = "ANCHOR_OUTSIDE_BOUNDS";
            else if (Distance(Position, CurrentWeb.Anchor) > Epsilon) reason = "NOT_AT_ANCHOR";
            else if (Distance(Position, cursor) < 162 * scale) reason = "THREAT_NEARBY";
            else if (CurrentWeb.Interrupted) reason = "WEB_INTERRUPTED";
            else if (idleSeconds < WeaveThreshold) reason = "IDLE_THRESHOLD_NOT_MET";
            else if (CurrentRouteId != null) reason = "ROUTE_ACTIVE";
            bool authorized = reason == null;
            string key = (CurrentWebId ?? "none") + "|" + (authorized ? "AUTHORIZED" : reason);
            WeaveAuthorized = authorized;
            // NO_WEB is normal before construction; emit a defer only if a web
            // exists or an earlier authorization is being replaced.
            if (key != lastWeaveDecision)
            {
                if (CurrentWeb != null || lastWeaveDecision != null)
                {
                    weaveDecisionId = NewDecision();
                    string outcome = authorized ? "WEAVE_AUTHORIZED" : "WEAVE_DEFERRED";
                    if (!Receipts.Contains(outcome)) Receipts.Add(outcome);
                    Record(outcome, outcome, reason, null, weaveDecisionId, Position,
                        CurrentWeb == null ? Position : CurrentWeb.Anchor);
                }
                lastWeaveDecision = key;
            }
            if (CurrentWeb == null) { weaveDecisionId = null; if (CurrentRouteId == null && State != "WAIT_FOR_ROUTE") CurrentDecisionId = null; }
            else if (CurrentRouteId == null && State != "WAIT_FOR_ROUTE" && weaveDecisionId != null) CurrentDecisionId = weaveDecisionId;
            return authorized;
        }

        private void TrackActivity(PointF cursor)
        {
            if (hasHeatCursor && Distance(cursor, lastHeatCursor) <= 5) return;
            string key = Cell(cursor);
            int heat;
            if (!activity.TryGetValue(key, out heat))
            {
                if (activity.Count >= 256) activity.Remove(activityOrder.Dequeue());
                activityOrder.Enqueue(key);
            }
            activity[key] = Math.Min(100, heat + 1);
            lastHeatCursor = cursor;
            hasHeatCursor = true;
        }

        private static string Cell(PointF point)
        {
            return Math.Floor(point.X / 100).ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                Math.Floor(point.Y / 100).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private void PruneMemory(PointF cursor, double clearance)
        {
            for (int i = rejectedRoutes.Count - 1; i >= 0; i--)
                if (rejectedRoutes[i].ExpiresAt <= now || Math.Abs(rejectedRoutes[i].Clearance - clearance) > Epsilon ||
                    Distance(rejectedRoutes[i].Cursor, cursor) > 32)
                    rejectedRoutes.RemoveAt(i);
        }

        private bool PlanSafeRoute(PointF goal, PointF cursor, double clearance, string movingState, double speed)
        {
            goal = Clamp(goal);
            IntendedTarget = goal;
            RejectedCorridor remembered = null;
            foreach (RejectedCorridor entry in rejectedRoutes)
            {
                if (Distance(entry.Start, Position) <= 0.5 && Distance(entry.End, goal) <= 0.5 &&
                    Distance(entry.Cursor, cursor) <= 0.5)
                { remembered = entry; break; }
            }
            if (remembered == null)
            {
                RouteChecks++;
                if (SegmentDistance(Position, goal, cursor) + Epsilon < clearance)
                {
                    EndRoute("ROUTE_CANCELLED", "CORRIDOR_BLOCKED");
                    if (rejectedRoutes.Count == MemoryLimit) rejectedRoutes.RemoveAt(0);
                    remembered = new RejectedCorridor { Start = Position, End = goal, Cursor = cursor,
                        ExpiresAt = now + MemorySeconds, Clearance = clearance, DecisionId = NewDecision() };
                    rejectedRoutes.Add(remembered);
                    CurrentDecisionId = remembered.DecisionId;
                    if (!Receipts.Contains("ROUTE_BLOCKED")) Receipts.Add("ROUTE_BLOCKED");
                    Record("ROUTE_BLOCKED", "ROUTE_BLOCKED", "CURSOR_CORRIDOR", null, remembered.DecisionId, Position, goal);
                }
            }
            if (remembered != null)
            {
                if (CurrentRouteId != null) EndRoute("ROUTE_CANCELLED", "CORRIDOR_BLOCKED");
                CurrentDecisionId = remembered.DecisionId;
                State = "WAIT_FOR_ROUTE";
                waitingFor = movingState;
                Speed = 0;
                Target = Position;
                RouteStatus = "BLOCKED";
                BlockedReason = "RECENTLY_REJECTED_CURSOR_CORRIDOR";
                return false;
            }
            ApproveRoute(goal, movingState, clearance);
            State = movingState;
            waitingFor = null;
            Target = goal;
            Speed = speed;
            RouteStatus = "CLEAR";
            return true;
        }

        private PointF QuietCorner(double scale)
        {
            // Bounds already include the spider margin; leave another 40 pixels
            // for the radial web so silk stays on the usable display as well.
            float x = (float)Math.Min(40 * scale, Bounds.Width / 4);
            float y = (float)Math.Min(40 * scale, Bounds.Height / 4);
            PointF[] corners = { new PointF(Bounds.Left + x, Bounds.Top + y),
                new PointF(Bounds.Right - x, Bounds.Top + y), new PointF(Bounds.Left + x, Bounds.Bottom - y),
                new PointF(Bounds.Right - x, Bounds.Bottom - y) };
            int lowestHeat = int.MaxValue;
            PointF best = corners[0];
            foreach (PointF corner in corners)
            {
                int heat;
                activity.TryGetValue(Cell(corner), out heat);
                if (heat < lowestHeat) { lowestHeat = heat; best = corner; }
            }
            return best;
        }

        private void PlanEscape(PointF cursor, double scale)
        {
            nextEscapeDecision = now + 0.19;
            double away = Math.Atan2(Position.Y - cursor.Y, Position.X - cursor.X);
            if (Distance(Position, cursor) < Epsilon) away = random.NextDouble() * Math.PI * 2;
            double jitter = (random.NextDouble() - 0.5) * 0.85;
            double bestScore = double.NegativeInfinity;
            PointF best = Position;
            for (int i = 0; i < 32; i++)
            {
                double angle = away + jitter + i * Math.PI * 2 / 32;
                PointF candidate = Clamp(new PointF(Position.X + (float)(Math.Cos(angle) * 160 * scale),
                    Position.Y + (float)(Math.Sin(angle) * 160 * scale)));
                double travel = Distance(Position, candidate);
                if (travel < Math.Min(20 * scale, Math.Min(Bounds.Width, Bounds.Height) / 4)) continue;
                double clearance = Math.Min(Math.Min(candidate.X - Bounds.Left, Bounds.Right - candidate.X),
                    Math.Min(candidate.Y - Bounds.Top, Bounds.Bottom - candidate.Y));
                double score = Distance(candidate, cursor) + travel * 0.22 + Math.Min(clearance, 40 * scale) * 0.12;
                if (score > bestScore) { bestScore = score; best = candidate; }
            }
            Target = IntendedTarget = best;
            ApproveRoute(best, "ESCAPE", 0);
            Speed = 820 * scale;
            State = "ESCAPE";
            RouteStatus = "ESCAPE_MANEUVER";
        }

        public void Update(PointF cursor, double deltaTime, double idleSeconds, double scale)
        {
            Update(cursor, deltaTime, idleSeconds, scale, null);
        }

        public void Update(PointF cursor, double deltaTime, double idleSeconds, double scale, double? timestamp)
        {
            ValidatePoint(cursor, "cursor");
            ValidatePoint(Position, "position");
            ValidatePoint(Target, "target");
            ValidatePoint(IntendedTarget, "intended target");
            ValidateBounds(Bounds);
            if (!Finite(deltaTime) || !Finite(idleSeconds) || !Finite(scale) || !Finite(820 * scale) || scale <= 0 ||
                !Finite(Speed) || Speed < 0 || !Finite(Rotation) ||
                (timestamp.HasValue && !Finite(timestamp.Value)) ||
                !Finite(WanderThreshold) || !Finite(WeaveThreshold) || WanderThreshold < 0 || WeaveThreshold < 0)
                throw new ArgumentException("Time, scale, and thresholds must be finite; scale must be positive and thresholds nonnegative.");
            if (CurrentWeb != null)
            {
                ValidatePoint(CurrentWeb.Anchor, "web anchor");
                if (!Finite(CurrentWeb.Progress) || CurrentWeb.Progress < 0) throw new ArgumentException("Web progress must be finite and nonnegative.");
            }
            double dt = Math.Max(0, Math.Min(0.5, deltaTime));
            idleSeconds = Math.Max(0, idleSeconds);
            now += dt;
            Tick++;
            ledgerTimestamp = timestamp.HasValue ? timestamp.Value : now;
            frameIdle = idleSeconds;
            frameStart = Position;
            frameCursor = cursor;
            Receipts.Clear();
            RouteStatus = "NOT_REQUIRED";
            BlockedReason = null;
            DistanceMoved = 0;
            MotionStatus = "IDLE";
            Position = Clamp(Position);
            PointF before = Position;
            frameStart = before;
            double radius = 150 * scale, clearance = radius + 12 * scale;
            frameClearance = clearance;
            PreThreatDistance = Distance(Position, cursor);
            ObserveWeb();
            if (approvedRoute != null && approvedRoute.Bounds != Bounds)
            {
                EndRoute("ROUTE_CANCELLED", "BOUNDS_CHANGED");
                Speed = 0;
                Target = IntendedTarget = Position;
                State = "ACTIVE_EVASION";
            }
            TrackActivity(cursor);
            PruneMemory(cursor, clearance);
            bool isThreatened = PreThreatDistance < radius;
            bool pointerWithdrew = hasCursor && previousPostDistance < radius && !isThreatened &&
                Distance(previousCursor, cursor) > Epsilon;
            bool escapedWithoutMoving = State == "ESCAPE" && !isThreatened;
            if (isThreatened) dangerEpisode = true;
            if (CurrentWeb != null && Distance(Position, CurrentWeb.Anchor) > Epsilon) hasBeenAway = true;

            // Intent: dangerous positions get escape intent; a safe spider never
            // continues a stale escape target back through a newly moved pointer.
            if (isThreatened)
            {
                RevokeWeave("THREAT_NEARBY");
                if (CurrentWeb != null) CurrentWeb.Interrupted = true;
                if (State != "ESCAPE" || !Moving || now >= nextEscapeDecision) PlanEscape(cursor, scale);
                else { Speed = 820 * scale; RouteStatus = "ESCAPE_MANEUVER"; }
            }
            else if (escapedWithoutMoving)
            {
                EndRoute("ROUTE_RETIRED", "THREAT_CLEARED");
                State = "ACTIVE_EVASION";
                Speed = 0;
                Target = IntendedTarget = Position;
                if (pointerWithdrew) Receipt("POINTER_WITHDREW");
            }
            else if (holdCancelledMovement)
            {
                holdCancelledMovement = false;
                Speed = 0;
                Target = IntendedTarget = Position;
                State = "ACTIVE_EVASION";
            }
            else if (CurrentWeb != null && (CurrentWeb.Interrupted ||
                (idleSeconds >= WeaveThreshold && Distance(Position, CurrentWeb.Anchor) > Epsilon)))
            {
                if (Distance(Position, CurrentWeb.Anchor) > Epsilon)
                {
                    CurrentWeb.Interrupted = true;
                    PlanSafeRoute(CurrentWeb.Anchor, cursor, clearance, "RECOVER", 200 * scale);
                }
                else
                {
                    CurrentWeb.Interrupted = false;
                    EndRoute("ROUTE_RETIRED", "AT_ANCHOR");
                    State = CanWeave(cursor, idleSeconds, scale) ? "WEAVE" : "ACTIVE_EVASION";
                    Speed = 0;
                    Target = IntendedTarget = Position;
                }
            }
            else if (idleSeconds >= WeaveThreshold)
            {
                if (CurrentWeb == null)
                {
                    PointF anchor = State == "SELECT_ANCHOR" || (State == "WAIT_FOR_ROUTE" && waitingFor == "SELECT_ANCHOR")
                        ? IntendedTarget : QuietCorner(scale);
                    PlanSafeRoute(anchor, cursor, clearance, "SELECT_ANCHOR", 150 * scale);
                }
                else
                {
                    EndRoute("ROUTE_RETIRED", "AT_ANCHOR");
                    State = CanWeave(cursor, idleSeconds, scale) ? "WEAVE" : "ACTIVE_EVASION";
                    Speed = 0;
                    Target = IntendedTarget = Position;
                }
            }
            else if (idleSeconds >= WanderThreshold)
            {
                PointF wanderTarget = State == "WANDER" && Moving ? Target : new PointF(
                    Bounds.Left + (float)random.NextDouble() * Bounds.Width,
                    Bounds.Top + (float)random.NextDouble() * Bounds.Height);
                PlanSafeRoute(wanderTarget, cursor, clearance, "WANDER", 100 * scale);
            }
            else
            {
                EndRoute("ROUTE_CANCELLED", "ACTIVITY_RESUMED");
                State = "ACTIVE_EVASION";
                Speed = 0;
                Target = IntendedTarget = Position;
            }

            // Effect: derive a bounded candidate before committing real position.
            if (Speed > 0 && !ExecutedIntentMatchesLease(clearance))
            {
                EndRoute("ROUTE_CANCELLED", "EXECUTION_INTENT_CHANGED");
                Speed = 0;
                Target = IntendedTarget = Position;
                State = "ACTIVE_EVASION";
                RouteStatus = "CANCELLED";
                BlockedReason = "EXECUTION_INTENT_CHANGED";
                waitingFor = null;
            }
            string movementState = State;
            double remaining = Distance(Position, Target);
            if (Speed > 0 && remaining > Epsilon && dt > 0)
            {
                if (approvedRoute == null) throw new InvalidOperationException("Motion requires an approved route identity.");
                double travel = Math.Min(remaining, Speed * dt);
                PointF candidate = Clamp(new PointF(Position.X + (float)((Target.X - Position.X) * travel / remaining),
                    Position.Y + (float)((Target.Y - Position.Y) * travel / remaining)));
                if (State == "ESCAPE" || SegmentDistance(Position, candidate, cursor) + Epsilon >= clearance)
                {
                    Position = candidate;
                    wantedRotation = Math.Atan2(Position.Y - before.Y, Position.X - before.X) * 180 / Math.PI + 90;
                }
                else
                {
                    EndRoute("ROUTE_CANCELLED", "CANDIDATE_SEGMENT_UNSAFE");
                    CurrentDecisionId = NewDecision();
                    Speed = 0;
                    Target = Position;
                    State = "WAIT_FOR_ROUTE";
                    RouteStatus = "BLOCKED";
                    BlockedReason = "CANDIDATE_SEGMENT_UNSAFE";
                    Receipt("ROUTE_BLOCKED");
                }
            }

            // Verification: receipts use physical displacement and the final
            // pointer distance. Intent or target arrival alone proves neither.
            DistanceMoved = Distance(before, Position);
            PostThreatDistance = Distance(Position, cursor);
            if (DistanceMoved > Epsilon) Receipt("MOVE_CONFIRMED");
            bool safe = PostThreatDistance >= radius;
            if (dangerEpisode && safe)
            {
                Receipt("THREAT_CLEARED");
                if (pointerWithdrew && DistanceMoved <= Epsilon) Receipt("POINTER_WITHDREW");
                dangerEpisode = false;
            }
            if (State == "ESCAPE" && safe)
            {
                EndRoute("ROUTE_RETIRED", "THREAT_CLEARED");
                State = "ACTIVE_EVASION";
                Speed = 0;
                Target = IntendedTarget = Position;
            }
            if (CurrentWeb != null && Distance(Position, CurrentWeb.Anchor) > Epsilon) hasBeenAway = true;
            bool arrived = Distance(Position, Target) <= Epsilon;
            if (arrived && safe && movementState == "SELECT_ANCHOR" && State != "WAIT_FOR_ROUTE")
            {
                CurrentWeb = new EcologyWeb { Anchor = Position, Progress = 0, Interrupted = false };
                CurrentWeb.Identity = "web-" + (++webSequence).ToString("D6");
                CurrentWebId = CurrentWeb.Identity;
                observedWeb = CurrentWeb;
                observedAnchor = Position;
                Receipt("WEB_CREATED");
                EndRoute("ROUTE_RETIRED", "ANCHOR_REACHED");
                hasBeenAway = false;
                Speed = 0;
                State = CanWeave(cursor, idleSeconds, scale) ? "WEAVE" : "ACTIVE_EVASION";
            }
            if (CurrentWeb != null && Distance(Position, CurrentWeb.Anchor) <= Epsilon && safe &&
                movementState == "RECOVER" && State != "WAIT_FOR_ROUTE")
            {
                if (hasBeenAway && DistanceMoved > Epsilon) Receipt("RETURN_CONFIRMED");
                EndRoute("ROUTE_RETIRED", "HOME_REACHED");
                hasBeenAway = false;
                CurrentWeb.Interrupted = false;
                Speed = 0;
                State = CanWeave(cursor, idleSeconds, scale) ? "WEAVE" : "ACTIVE_EVASION";
            }
            bool weaveAllowed = CanWeave(cursor, idleSeconds, scale);
            if (State == "WEAVE" && !weaveAllowed) State = "ACTIVE_EVASION";
            if (State == "WEAVE" && weaveAllowed)
            {
                double previousProgress = CurrentWeb.Progress;
                CurrentWeb.Progress += dt;
                double added = CurrentWeb.Progress - previousProgress;
                if (added > 0 && (progressWebId != CurrentWebId || progressDecisionId != weaveDecisionId || now - lastProgressAt >= 1))
                {
                    if (!Receipts.Contains("WEAVE_PROGRESS_CONFIRMED")) Receipts.Add("WEAVE_PROGRESS_CONFIRMED");
                    Record("WEAVE_PROGRESS_CONFIRMED", "WEAVE_PROGRESS_CONFIRMED", "AUTHORIZED_SILK_ADDED", null, weaveDecisionId,
                        Position, Position, previousProgress, CurrentWeb.Progress, added);
                    progressWebId = CurrentWebId;
                    progressDecisionId = weaveDecisionId;
                    lastProgressAt = now;
                }
            }
            if (arrived && approvedRoute != null && movementState != "ESCAPE" && movementState != "RECOVER" && movementState != "SELECT_ANCHOR")
                EndRoute("ROUTE_RETIRED", "ARRIVED");

            double difference = ((wantedRotation - Rotation + 540) % 360 + 360) % 360 - 180;
            Rotation = ((Rotation + difference * Math.Min(1, dt * 16)) % 360 + 360) % 360;
            if (Receipts.Contains("RETURN_CONFIRMED")) MotionStatus = "RETURN_CONFIRMED";
            else if (Receipts.Contains("POINTER_WITHDREW")) MotionStatus = "POINTER_WITHDREW";
            else if (Receipts.Contains("THREAT_CLEARED")) MotionStatus = "THREAT_CLEARED";
            else if (Receipts.Contains("MOVE_CONFIRMED")) MotionStatus = "MOVE_CONFIRMED";
            else if (Receipts.Contains("ROUTE_BLOCKED") || RouteStatus == "BLOCKED") MotionStatus = "ROUTE_BLOCKED";
            previousCursor = cursor;
            previousPostDistance = PostThreatDistance;
            hasCursor = true;
        }
    }
}
