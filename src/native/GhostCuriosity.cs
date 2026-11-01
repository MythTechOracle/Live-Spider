using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;

namespace SpookyDesktopSpider
{
    // The host supplies only an already-approved geometry/role snapshot. No
    // provider, native window, process, control pattern, or user text is retained.
    internal sealed class GhostCandidate
    {
        public readonly RectangleF Bounds;
        public readonly HoverControlKind Kind;

        public GhostCandidate(RectangleF bounds, HoverControlKind kind)
        {
            Bounds = bounds;
            Kind = kind;
        }
    }

    internal sealed class GhostReceipt
    {
        public readonly long Sequence, Tick;
        public readonly string State, Outcome, Reason, Kind;

        internal GhostReceipt(long sequence, long tick, string state, string outcome, string reason, string kind)
        {
            Sequence = sequence; Tick = tick; State = state;
            Outcome = outcome; Reason = reason; Kind = kind;
        }
    }

    // Pure visual curiosity: it requests a pause and an optional glow. The host
    // owns freshness/policy checks and must acknowledge actual rendering. This
    // class has no APIs that dispatch input or inspect another application's state.
    internal sealed class GhostCuriosity
    {
        public const double ObserveSeconds = 0.18;
        public const double HoverSeconds = 0.6;
        public const double DepartSeconds = 0.4;
        public const double CooldownSeconds = 3;
        public const int HistoryLimit = 64;
        private readonly List<GhostReceipt> ledger = new List<GhostReceipt>();
        private readonly List<string> receipts = new List<string>();
        private GhostCandidate current, cooldownTarget;
        private double phaseAge, now, cooldownUntil;
        private bool hasTimestamp, renderAcknowledged;
        private string declineKey;
        private long sequence;

        public GhostCuriosity()
        {
            State = "EXPLORE";
            Ledger = ledger.AsReadOnly();
            Receipts = receipts.AsReadOnly();
        }

        public string State { get; private set; }
        public bool HoldMovement { get; private set; }
        public bool WantsGlow { get; private set; }
        public Rectangle? GlowBounds { get; private set; }
        public long Tick { get; private set; }
        public ReadOnlyCollection<GhostReceipt> Ledger { get; private set; }
        public ReadOnlyCollection<string> Receipts { get; private set; }

        private static bool Finite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static bool SameTarget(GhostCandidate a, GhostCandidate b)
        {
            return a != null && b != null && a.Kind == b.Kind && a.Bounds == b.Bounds;
        }

        private static bool Valid(GhostCandidate candidate, PointF point)
        {
            if (candidate == null || !Finite(point.X) || !Finite(point.Y) ||
                !Enum.IsDefined(typeof(HoverControlKind), candidate.Kind)) return false;
            RectangleF bounds = candidate.Bounds;
            return Finite(bounds.X) && Finite(bounds.Y) && Finite(bounds.Width) && Finite(bounds.Height) &&
                Finite(bounds.Right) && Finite(bounds.Bottom) && Math.Abs(bounds.X) <= 1000000 && Math.Abs(bounds.Y) <= 1000000 &&
                bounds.Width >= 8 && bounds.Height >= 8 && bounds.Width <= 600 && bounds.Height <= 200 &&
                point.X >= bounds.Left && point.X < bounds.Right && point.Y >= bounds.Top && point.Y < bounds.Bottom;
        }

        private void Record(string outcome, string reason, GhostCandidate candidate)
        {
            receipts.Add(outcome);
            ledger.Add(new GhostReceipt(++sequence, Tick, State, outcome, reason,
                candidate == null ? "None" : candidate.Kind.ToString()));
            if (ledger.Count > HistoryLimit) ledger.RemoveAt(0);
        }

        private void BeginDepart()
        {
            if (current != null)
            {
                cooldownTarget = current;
                cooldownUntil = now + CooldownSeconds;
            }
            current = null;
            renderAcknowledged = false;
            if (State != "DEPART") phaseAge = 0;
            State = "DEPART";
            HoldMovement = WantsGlow = false;
            GlowBounds = null;
        }

        private void Decline(string reason, GhostCandidate candidate)
        {
            bool observationExisted = current != null || candidate != null || WantsGlow || HoldMovement;
            string key = reason;
            GhostCandidate classified = candidate ?? current;
            BeginDepart();
            if (observationExisted && key != declineKey)
            {
                Record("OBSERVATION_DECLINED", reason, classified);
                declineKey = key;
            }
        }

        private void ApplyVisualRequest()
        {
            HoldMovement = State == "TARGET_DETECTED" || State == "OBSERVE" || State == "GHOST_HOVER";
            WantsGlow = State == "GHOST_HOVER" && current != null;
            GlowBounds = WantsGlow ? (Rectangle?)Rectangle.Ceiling(current.Bounds) : null;
        }

        private static string HostReason(string reason)
        {
            // This API accepts classifications, not arbitrary application text.
            switch (reason)
            {
                case "PAUSED": case "USER_PAUSED": case "HIDDEN": case "RECALLED":
                case "DISABLED": case "CONSENT_REVOKED": case "SHUTDOWN":
                case "NO_TARGET": case "TARGET_STALE": case "TARGET_CHANGED":
                case "POLICY_DECLINED": case "CAPABILITY_UNAVAILABLE": case "NO_SAFE_TARGET":
                case "NOT_EXPLORING": case "THREAT_PREEMPTED_CURIOSITY":
                    return reason;
                default: return "HOST_DECLINED";
            }
        }

        public void Reset(string reason)
        {
            receipts.Clear();
            DeclineObservation(reason);
        }

        public void DeclineObservation(string reason)
        {
            string classification = HostReason(reason);
            GhostCandidate observed = current;
            BeginDepart();
            if (declineKey == classification) return;
            declineKey = classification;
            Record("OBSERVATION_DECLINED", classification, observed);
        }

        public void Update(double elapsed, double timestamp, PointF spider, GhostCandidate candidate, bool canExplore, bool threat)
        {
            Tick++;
            receipts.Clear();
            if (!Finite(elapsed) || !Finite(timestamp) || !Finite(spider.X) || !Finite(spider.Y) ||
                (hasTimestamp && timestamp < now))
            {
                Decline("INVALID_SIMULATION_INPUT", candidate);
                return;
            }
            double dt = Math.Max(0, Math.Min(0.25, elapsed));
            now = timestamp;
            hasTimestamp = true;
            bool candidateValid = Valid(candidate, spider);
            if (cooldownTarget != null && (!candidateValid || !SameTarget(candidate, cooldownTarget)))
                cooldownTarget = null; // Leaving the target permits a later new visit.
            if (threat) { Decline("THREAT_PREEMPTED_CURIOSITY", candidate); return; }
            if (!canExplore) { Decline("EXPLORATION_NOT_ALLOWED", candidate); return; }
            if (!candidateValid)
            {
                if (current != null || candidate != null) Decline(candidate == null ? "TARGET_NO_LONGER_CURRENT" : "INVALID_TARGET_GEOMETRY_OR_KIND", candidate);
                else
                {
                    HoldMovement = WantsGlow = false; GlowBounds = null;
                    if (State == "DEPART") { phaseAge += dt; if (phaseAge >= DepartSeconds) { State = "EXPLORE"; phaseAge = 0; } }
                }
                return;
            }
            if (current != null && !SameTarget(current, candidate))
            {
                Decline("TARGET_CHANGED", candidate);
                return;
            }
            if (State == "DEPART")
            {
                phaseAge += dt;
                if (phaseAge < DepartSeconds) { ApplyVisualRequest(); return; }
                State = "EXPLORE"; phaseAge = 0;
            }
            if (State == "EXPLORE")
            {
                if (cooldownTarget != null && SameTarget(candidate, cooldownTarget) && now < cooldownUntil)
                {
                    HoldMovement = WantsGlow = false; GlowBounds = null;
                    return;
                }
                current = candidate;
                State = "TARGET_DETECTED";
                phaseAge = 0;
                renderAcknowledged = false;
                declineKey = null;
                Record("TARGET_OBSERVED", "HOST_APPROVED_GEOMETRY_AND_KIND", current);
                ApplyVisualRequest();
                return;
            }
            if (State == "TARGET_DETECTED" || State == "OBSERVE")
            {
                State = "OBSERVE";
                phaseAge += dt;
                if (phaseAge + 0.000000001 >= ObserveSeconds) { State = "GHOST_HOVER"; phaseAge = 0; }
            }
            else if (State == "GHOST_HOVER")
            {
                phaseAge += dt;
                if (phaseAge + 0.000000001 >= HoverSeconds) BeginDepart();
            }
            ApplyVisualRequest();
        }

        public void ConfirmRendered()
        {
            if (renderAcknowledged || !WantsGlow || !GlowBounds.HasValue || State != "GHOST_HOVER" || current == null) return;
            renderAcknowledged = true;
            Record("GHOST_HOVER_RENDERED", "HOST_ACKNOWLEDGED_VISUAL_RENDER", current);
            Record("NO_INPUT_DISPATCHED", "CURIOSITY_ENGINE_HAS_NO_INPUT_CAPABILITY", current);
        }
    }
}
