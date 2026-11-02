using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SpookyDesktopSpider
{
    // Owned native preview only. Synthetic pointer positions in automated mode
    // are inputs to the simulation; they are never sent to Windows as input.
    internal static class HangingVisualChecks
    {
        public static void Run(bool verify)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            using (HangingBoard board = new HangingBoard(verify)) Application.Run(board);
        }
        public static void ShowDemo() { new HangingBoard(false).Show(); }

        private sealed class TestSpider : Form
        {
            public TestSpider()
            {
                Text = "Halloweb hanging preview spider"; FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false; TopMost = true; AutoScaleMode = AutoScaleMode.None;
                StartPosition = FormStartPosition.Manual; ClientSize = new Size(160, 160);
            }
            protected override bool ShowWithoutActivation { get { return true; } }
            protected override CreateParams CreateParams
            {
                get { CreateParams cp = base.CreateParams; cp.ExStyle |= 0x80000 | 0x20 | 0x80 | 0x8000000; return cp; }
            }
            protected override void WndProc(ref Message message)
            {
                if (message.Msg == 0x0084) { message.Result = new IntPtr(-1); return; }
                if (message.Msg == 0x0021) { message.Result = new IntPtr(3); return; }
                base.WndProc(ref message);
            }
            public void Render(EcologyMotion motion, double time)
            {
                using (Bitmap bitmap = SpiderArt.Draw(160, motion.Rotation, motion.Moving, time, 1, motion.HangingAnimation))
                    Native.SetBitmap(Handle, bitmap, (int)Math.Round(motion.Position.X) - 80, (int)Math.Round(motion.Position.Y) - 80);
                if (!Visible) Show(); Native.ShowWindow(Handle, 4);
            }
        }

        private sealed class HangingBoard : Form
        {
            private readonly bool verify;
            private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 25 };
            private readonly Stopwatch clock = Stopwatch.StartNew();
            private readonly TestSpider spider = new TestSpider();
            private readonly WebOverlay web = new WebOverlay();
            private readonly TetherOverlay tether = new TetherOverlay();
            private readonly Label status = new Label { Bounds = new Rectangle(330, 100, 390, 230) };
            private readonly Button underlying = new Button { TabStop = false, Text = "Owned test control" };
            private readonly List<string> states = new List<string>();
            private EcologyMotion motion;
            private EcologyWeb home;
            private Native.POINT initialPointer;
            private IntPtr initialForeground;
            private double lastTime, hangingSince = -1, roundSince;
            private int assertions, clicks, round;
            private bool pointerStill = true, focusStill = true, sawTether, sawBounce, lifeRegistered, previewSuspended;
            private double maximumBounce;

            public HangingBoard(bool automatic)
            {
                verify = automatic;
                Text = "Halloweb LIVE-SPIDER-14 - owned hanging preview";
                ClientSize = new Size(740, 480); AutoScaleMode = AutoScaleMode.None;
                FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
                StartPosition = FormStartPosition.Manual; ShowInTaskbar = true;
                BackColor = Color.FromArgb(27, 34, 47); ForeColor = Color.Gainsboro; Font = new Font("Segoe UI", 10);
                Controls.Add(new Label { Text = "A peaceful hanging spider", Bounds = new Rectangle(28, 22, 680, 35), Font = new Font("Segoe UI", 15, FontStyle.Bold) });
                Controls.Add(new Label { Text = automatic ? "Owned verification. No pointer movement or desktop input is generated." :
                    "Move your pointer near this preview spider to send it home. This is a separate test web.", Bounds = new Rectangle(28, 66, 680, 48) });
                Controls.Add(status);
                Controls.Add(new Label { Text = "The preview supplies a 600-second idle condition. Your desktop spider and its web are separate.", Bounds = new Rectangle(28, 408, 680, 52) });
                underlying.BackColor = Color.FromArgb(49, 59, 76); underlying.FlatStyle = FlatStyle.Flat;
                underlying.Click += delegate { clicks++; }; Controls.Add(underlying);
                timer.Tick += delegate { Step(); };
                Native.POINT pointer; Native.GetCursorPos(out pointer);
                Rectangle area = Screen.FromPoint(new Point(pointer.X, pointer.Y)).WorkingArea;
                Location = new Point(pointer.X < area.Left + area.Width / 2 ? area.Right - Width - 18 : area.Left + 18,
                    pointer.Y < area.Top + area.Height / 2 ? area.Bottom - Height - 18 : area.Top + 18);
            }
            protected override bool ShowWithoutActivation { get { return true; } }
            protected override CreateParams CreateParams
            {
                get { CreateParams cp = base.CreateParams; cp.ExStyle |= Native.WS_EX_NOACTIVATE; return cp; }
            }
            protected override void OnShown(EventArgs e)
            {
                base.OnShown(e);
                try
                {
                    lifeRegistered = Native.WTSRegisterSessionNotification(Handle, 0);
                    Require(lifeRegistered, "current-session lifecycle notifications registered");
                    Require(Native.GetCursorPos(out initialPointer), "live pointer polling available");
                    initialForeground = Native.GetForegroundWindow(); ResetRound(); timer.Start();
                }
                catch (Exception error)
                {
                    File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hanging-smoke-test-results.txt"), "FAIL: " + error + "\r\n");
                    if (verify) Environment.ExitCode = 1;
                    Close();
                }
            }
            private void ResetRound()
            {
                Rectangle client = RectangleToScreen(ClientRectangle);
                RectangleF safe = new RectangleF(client.Left + 90, client.Top + 160, 550, 220);
                PointF anchor = new PointF(safe.Left + 35, safe.Top + 20);
                motion = new EcologyMotion(anchor, safe, 14) { HangingEnabled = true, HangingDepth = 160 };
                home = new EcologyWeb { Anchor = anchor, Progress = 24, Interrupted = false };
                motion.CurrentWeb = home;
                underlying.Bounds = new Rectangle((int)(anchor.X - client.Left - 75), (int)(anchor.Y - client.Top + 140), 150, 40);
                hangingSince = -1; roundSince = clock.Elapsed.TotalSeconds;
                sawTether = sawBounce = false; maximumBounce = 0;
            }
            private void Require(bool condition, string reason)
            {
                assertions++; if (!condition) throw new InvalidOperationException(reason);
            }
            private bool HasReceipt(string outcome)
            {
                foreach (HangingReceipt receipt in motion.SuspensionLedger) if (receipt.Outcome == outcome) return true;
                return false;
            }
            private void Step()
            {
                try
                {
                    if (previewSuspended)
                    {
                        if (verify && clock.Elapsed.TotalSeconds > 25) throw new TimeoutException("The owned preview was suspended by a lifecycle event.");
                        return;
                    }
                    double now = clock.Elapsed.TotalSeconds, dt = lastTime == 0 ? 0.025 : Math.Min(0.1, now - lastTime);
                    lastTime = now;
                    Native.POINT pointer;
                    if (!Native.GetCursorPos(out pointer)) { SuspendPreview("POINTER_UNAVAILABLE"); return; }
                    pointerStill &= pointer.X == initialPointer.X && pointer.Y == initialPointer.Y;
                    focusStill &= Native.GetForegroundWindow() == initialForeground;
                    if (verify && now > 25) throw new TimeoutException("Hanging preview timed out: " + motion.SuspensionState);
                    PointF threat = verify ? new PointF(motion.Bounds.Right + 900, motion.Bounds.Bottom + 900) : new PointF(pointer.X, pointer.Y);
                    if (motion.SuspensionState == "HANGING" && hangingSince < 0) hangingSince = now;
                    if (verify && hangingSince >= 0 && now - hangingSince > 0.7)
                        threat = round == 0 ? new PointF(home.Anchor.X + 210, home.Anchor.Y + 160) : new PointF(home.Anchor.X, home.Anchor.Y + 225);
                    motion.Update(threat, dt, 601, 1);
                    if (states.Count == 0 || states[states.Count - 1] != motion.SuspensionState) states.Add(motion.SuspensionState);
                    spider.Render(motion, now);
                    web.UpdateWeb(home, 1, motion.WebBounceOffset);
                    if (web.Visible && motion.WebImpactPending && Math.Abs(motion.WebBounceOffset) > 0.1)
                        motion.ConfirmWebImpactRendered();
                    tether.UpdateTether(motion.TetherAnchor, motion.Position, 1, motion.TetherVisible);
                    if (tether.Visible)
                    {
                        sawTether = true;
                        Native.SetWindowPos(tether.Handle, spider.Handle, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
                    }
                    if (Math.Abs(motion.WebBounceOffset) > 0.2) sawBounce = true;
                    maximumBounce = Math.Max(maximumBounce, Math.Abs(motion.WebBounceOffset));
                    status.Text = "State: " + motion.SuspensionState + "\r\n\r\n" +
                        "Silk visible: " + tether.Visible + "\r\nWeb progress: " + home.Progress.ToString("F1") +
                        "\r\nVerified movement: " + motion.DistanceMoved.ToString("F2") +
                        "\r\nOwned control clicks: " + clicks +
                        "\r\n\r\n" + (verify ? "Automatic pass " + (round + 1) + ": " + (round == 0 ? "climb home" : "snap home and bounce") :
                        "Mild approach: climb\r\nStrong safe approach: snap\r\nBlocked tether: release and escape");
                    if (!verify) return;
                    Require(object.ReferenceEquals(home, motion.CurrentWeb) && home.Progress == 24, "same web and progress survive suspension");
                    if (motion.SuspensionState == "HANGING") CheckOverlayStyles();
                    if (HasReceipt("SETTLED_AT_HOME"))
                    {
                        Require(HasReceipt("DESCENT_AUTHORIZED") && HasReceipt("SILK_ANCHOR_CONFIRMED") && HasReceipt("DESCENT_CONFIRMED") && HasReceipt("HANGING_CONFIRMED"), "verified descent/hanging receipts");
                        Require(HasReceipt("THREAT_ASSESSED") && HasReceipt("RETURN_METHOD_SELECTED") && HasReceipt("RETURN_CONFIRMED"), "verified threat and actual return receipts");
                        Require(HasReceipt("WEB_IMPACT_CONFIRMED") && sawBounce && maximumBounce > 0.2 && maximumBounce < 8, "actual bounded web bounce rendered after arrival");
                        Require(sawTether && !tether.Visible && EcologyMotion.Distance(motion.Position, home.Anchor) < 0.01, "tether clears at measured home position");
                        Require(pointerStill && focusStill && clicks == 0, "real pointer, foreground and owned controls remain untouched");
                        if (round == 0)
                        {
                            Require(states.Contains("CLIMB_HOME"), "mild disturbance climbed home");
                            round = 1; ResetRound();
                        }
                        else
                        {
                            Require(states.Contains("SNAP_HOME"), "strong safe disturbance snapped along the tether");
                            CheckActiveLifecycleCancellation();
                            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hanging-smoke-test-results.txt"),
                                "PASS: " + assertions + " owned hanging overlay assertions. Actual descent, hanging, climb home, safe snap, bounded rendered web bounce, native click-through hit testing, unchanged web/progress, stationary real pointer and retained foreground verified. Active pause/lock/display/shutdown handler cancellation simulated without changing the Windows session; no tether remains.\r\nStates: " + string.Join(" -> ", states.ToArray()) + "\r\n");
                            Close();
                        }
                    }
                }
                catch (Exception error)
                {
                    timer.Stop(); Environment.ExitCode = 1;
                    File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hanging-smoke-test-results.txt"), "FAIL: " + error + "\r\n");
                    Close();
                }
            }
            private void CheckOverlayStyles()
            {
                int expected = Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
                foreach (Form layer in new Form[] { spider, web, tether })
                    Require(layer.Visible && (Native.GetWindowLong(layer.Handle, -20) & expected) == expected && Native.GetForegroundWindow() != layer.Handle, "native layered click-through nonactivating suspension layer");
                IntPtr hit = WindowFromPoint(new HitPoint { X = (int)Math.Round(motion.Position.X), Y = (int)Math.Round(motion.Position.Y) });
                Require(hit == underlying.Handle, "Windows hit testing reaches the owned control beneath the hanging spider");
            }
            private void CheckActiveLifecycleCancellation()
            {
                foreach (string reason in new string[] { "PAUSED", "SESSION_LOCK", "DISPLAY_CHANGED", "SHUTDOWN" })
                {
                    ResetRound();
                    PointF quiet = new PointF(motion.Bounds.Right + 900, motion.Bounds.Bottom + 900);
                    for (int tick = 0; tick < 80 && motion.SuspensionState != "HANGING"; tick++) motion.Update(quiet, 0.1, 601, 1);
                    spider.Render(motion, clock.Elapsed.TotalSeconds);
                    web.UpdateWeb(home, 1);
                    tether.UpdateTether(motion.TetherAnchor, motion.Position, 1, motion.TetherVisible);
                    Require(motion.SuspensionState == "HANGING" && tether.Visible, "lifecycle test begins with an active visible tether");
                    PointF suspended = motion.Position;
                    SuspendPreview(reason);
                    Require(!motion.SuspensionActive && !tether.Visible && !spider.Visible && !web.Visible,
                        "lifecycle handler hides all suspension visuals: " + reason);
                    Require(motion.Position == suspended && home.Progress == 24 && object.ReferenceEquals(home, motion.CurrentWeb) && HasReceipt("TETHER_RELEASED"),
                        "lifecycle cancellation preserves physical position and existing web: " + reason);
                    previewSuspended = false;
                }
            }
            private void SuspendPreview(string reason)
            {
                previewSuspended = true;
                if (motion != null) motion.CancelHanging(reason);
                tether.Clear(); spider.Hide(); web.Hide();
                status.Text = "Preview suspended: " + reason + "\r\nClose and reopen the preview to restart.";
            }
            protected override void WndProc(ref Message message)
            {
                if (message.Msg == 0x02B1 && (message.WParam.ToInt32() == 7 || message.WParam.ToInt32() == 2 || message.WParam.ToInt32() == 4)) SuspendPreview("SESSION_LOCK");
                if (message.Msg == 0x007E || message.Msg == 0x02E0) SuspendPreview("DISPLAY_CHANGED");
                base.WndProc(ref message);
            }
            protected override void OnLocationChanged(EventArgs e)
            {
                base.OnLocationChanged(e);
                if (motion != null) SuspendPreview("PREVIEW_MOVED");
            }
            protected override void OnSizeChanged(EventArgs e)
            {
                base.OnSizeChanged(e);
                if (motion != null) SuspendPreview("PREVIEW_RESIZED");
            }
            protected override void OnVisibleChanged(EventArgs e)
            {
                base.OnVisibleChanged(e);
                if (motion != null && !Visible) SuspendPreview("PREVIEW_HIDDEN");
            }
            protected override void OnFormClosed(FormClosedEventArgs e)
            {
                timer.Stop(); timer.Dispose(); SuspendPreview("SHUTDOWN");
                if (lifeRegistered) Native.WTSUnRegisterSessionNotification(Handle);
                tether.Dispose(); spider.Dispose(); web.Dispose(); base.OnFormClosed(e);
            }
        }
        [StructLayout(LayoutKind.Sequential)] private struct HitPoint { public int X, Y; }
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(HitPoint point);
    }
}
