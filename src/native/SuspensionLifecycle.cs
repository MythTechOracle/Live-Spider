using System;

namespace SpookyDesktopSpider
{
    // Reconnecting a session does not unlock it. Track those facts separately.
    internal sealed class SuspensionSessionGate
    {
        public bool Locked { get; private set; }
        public bool Disconnected { get; private set; }
        public bool CanAnimate { get { return !Locked && !Disconnected; } }
        public bool ApplyChange(int change)
        {
            if (change == 7 || change == 6) { Locked = true; return true; }
            if (change == 2 || change == 4) { Disconnected = true; return true; }
            if (change == 8) Locked = false;
            if (change == 1 || change == 3) Disconnected = false;
            return false;
        }
    }

    internal static class SuspensionLifecycleChecks
    {
        public static int Run()
        {
            int count = 0;
            Action<bool> check = delegate(bool ok) { count++; if (!ok) throw new InvalidOperationException("Session lifecycle check " + count); };
            SuspensionSessionGate gate = new SuspensionSessionGate();
            check(gate.CanAnimate);
            check(gate.ApplyChange(7) && !gate.CanAnimate);
            check(!gate.ApplyChange(1) && !gate.CanAnimate);
            check(!gate.ApplyChange(3) && !gate.CanAnimate);
            check(!gate.ApplyChange(8) && gate.CanAnimate);
            check(gate.ApplyChange(4) && !gate.CanAnimate);
            check(gate.ApplyChange(7) && !gate.CanAnimate);
            check(!gate.ApplyChange(3) && !gate.CanAnimate);
            check(!gate.ApplyChange(8) && gate.CanAnimate);
            check(gate.ApplyChange(2) && !gate.CanAnimate);
            check(!gate.ApplyChange(1) && gate.CanAnimate);
            check(gate.ApplyChange(6) && !gate.CanAnimate);
            check(!gate.ApplyChange(1) && !gate.CanAnimate);
            return count;
        }
    }
}
