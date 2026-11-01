using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace SpookyDesktopSpider
{
    // Opt-in desktop verification, separate from the headless behavior checks.
    // All inspected controls belong to the child fixture, never a user's app.
    internal static class HoverIntegrationChecks
    {
        internal static string FilePath(string name) { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name); }
        public static void RunFixture(string channelName)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (Fixture fixture = new Fixture(channelName)) Application.Run(fixture);
        }
        public static void Run()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (Harness harness = new Harness(false)) Application.Run(harness);
        }
        public static void RunManual()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (Harness harness = new Harness(true)) Application.Run(harness);
        }

        public static int PureChecks()
        {
            int count = 0;
            RectangleF rectangle;
            Action<bool> check = delegate(bool ok) { count++; if (!ok) throw new InvalidOperationException("Hover target validation check failed at " + count); };
            PointF point = new PointF(-200, -50);
            check(HoverProbe.TryTargetBounds(-240, -80, 96, 36, point, out rectangle));
            foreach (double bad in new double[] { double.NaN, double.NegativeInfinity, double.PositiveInfinity })
            {
                check(!HoverProbe.TryTargetBounds(bad, -80, 96, 36, point, out rectangle));
                check(!HoverProbe.TryTargetBounds(-240, bad, 96, 36, point, out rectangle));
                check(!HoverProbe.TryTargetBounds(-240, -80, bad, 36, point, out rectangle));
                check(!HoverProbe.TryTargetBounds(-240, -80, 96, bad, point, out rectangle));
            }
            check(!HoverProbe.TryTargetBounds(-240, -80, 601, 36, point, out rectangle));
            check(!HoverProbe.TryTargetBounds(-240, -80, 96, 201, point, out rectangle));
            check(!HoverProbe.TryTargetBounds(-240, -80, 0, 36, point, out rectangle));
            check(!HoverProbe.TryTargetBounds(-240, -80, 96, 36, new PointF(-144, -50), out rectangle));
            HoverControlKind kind;
            check(HoverProbe.TryControlKind(System.Windows.Automation.ControlType.Button.Id, out kind));
            check(HoverProbe.TryControlKind(System.Windows.Automation.ControlType.MenuItem.Id, out kind));
            check(!HoverProbe.TryControlKind(System.Windows.Automation.ControlType.Hyperlink.Id, out kind));
            check(!HoverProbe.TryControlKind(System.Windows.Automation.ControlType.CheckBox.Id, out kind));
            check(!HoverProbe.TryControlKind(System.Windows.Automation.ControlType.RadioButton.Id, out kind));
            check(!HoverProbe.TryControlKind(System.Windows.Automation.ControlType.TabItem.Id, out kind));
            check(!HoverProbe.TryControlKind(System.Windows.Automation.ControlType.Edit.Id, out kind));
            check(!HoverProbe.TryControlKind(System.Windows.Automation.ControlType.ListItem.Id, out kind));
            check(HoverProbe.IsDeniedWindowClass("#32770"));
            check(HoverProbe.IsDeniedWindowClass("Progman"));
            check(HoverProbe.IsDeniedWindowClass("WorkerW"));
            check(HoverProbe.IsDeniedWindowClass("Shell_TrayWnd"));
            long now = Stopwatch.GetTimestamp();
            HoverTarget fresh = new HoverTarget(new RectangleF(-240, -80, 96, 36), point, new IntPtr(123), 456, HoverControlKind.Button, now, 1);
            check(fresh.IsFresh(point, new IntPtr(123), 300));
            check(!fresh.IsFresh(point, IntPtr.Zero, 300));
            check(!fresh.IsFresh(point, new IntPtr(789), 300));
            check(!fresh.IsFresh(new PointF(0, 0), new IntPtr(123), 300));
            check(!HoverProbe.IsFreshTimestamp(now - Stopwatch.Frequency, now, 300));
            check(!HoverProbe.IsFreshTimestamp(now + 1, now, 300));
            check(!HoverProbe.IsFreshTimestamp(now, now, double.NaN));
            return count;
        }

        // Named kernel memory, never a disk-backed map. Test coordination does
        // not read files or inspect documents, directories, clipboard or pixels.
        private sealed class FixtureChannel : IDisposable
        {
            private readonly MemoryMappedFile map;
            private readonly MemoryMappedViewAccessor view;
            private readonly Mutex gate;
            private static readonly string[] commands = { "", "disable", "panel", "occlude", "restore", "close", "manual" };
            public FixtureChannel(string name, bool create)
            {
                if (!name.StartsWith("Local\\HallowebGhostFixture-", StringComparison.Ordinal)) throw new ArgumentException("Invalid fixture channel.");
                map = create ? MemoryMappedFile.CreateNew(name, 4096) : MemoryMappedFile.OpenExisting(name);
                view = map.CreateViewAccessor(); gate = new Mutex(false, name + "-gate");
            }
            public string Command
            {
                get { int value = view.ReadInt32(0); return value >= 0 && value < commands.Length ? commands[value] : "close"; }
                set { int index = Array.IndexOf(commands, value); if (index < 0) throw new ArgumentException("Invalid fixture command."); view.Write(0, index); }
            }
            public void WriteState(Dictionary<string, object> state)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(state));
                if (bytes.Length > 4000 || !gate.WaitOne(50)) return;
                try { view.Write(4, bytes.Length); view.WriteArray(8, bytes, 0, bytes.Length); }
                finally { gate.ReleaseMutex(); }
            }
            public Dictionary<string, object> ReadState()
            {
                if (!gate.WaitOne(50)) return null;
                try
                {
                    int length = view.ReadInt32(4); if (length <= 0 || length > 4000) return null;
                    byte[] bytes = new byte[length]; view.ReadArray(8, bytes, 0, length);
                    return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(bytes));
                }
                finally { gate.ReleaseMutex(); }
            }
            public void Dispose() { view.Dispose(); map.Dispose(); gate.Dispose(); }
        }

        private sealed class Fixture : Form
        {
            private readonly Button button = new Button { Text = "Harmless test button", Bounds = new Rectangle(50, 80, 220, 44), TabStop = false, FlatStyle = FlatStyle.System };
            private readonly Label label = new Label { Text = "This label must not glow", Bounds = new Rectangle(50, 160, 220, 44) };
            private readonly Label instruction = new Label { Text = "Visual-only spider glow verification", Bounds = new Rectangle(24, 20, 320, 44) };
            private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 40 };
            private readonly Stopwatch age = Stopwatch.StartNew();
            private int enters, hovers, clicks, focuses, activations, downs, ups, initialHotState = -1, hotChanges;
            private long lastClickTimestamp;
            private Form cover;
            private Panel panel;
            private string command = "";
            private readonly FixtureChannel channel;
            public Fixture(string channelName)
            {
                channel = new FixtureChannel(channelName, false);
                Text = "Halloweb isolated hover test";
                FormBorderStyle = FormBorderStyle.FixedToolWindow;
                ShowInTaskbar = false;
                TopMost = true;
                AutoScaleMode = AutoScaleMode.None;
                StartPosition = FormStartPosition.Manual;
                ClientSize = new Size(370, 260);
                Native.POINT cursor; Native.GetCursorPos(out cursor);
                Rectangle area = Screen.FromPoint(new Point(cursor.X, cursor.Y)).WorkingArea;
                int x = cursor.X < area.Left + area.Width / 2 ? area.Right - Width - 24 : area.Left + 24;
                int y = cursor.Y < area.Top + area.Height / 2 ? area.Bottom - Height - 24 : area.Top + 24;
                Location = new Point(x, y);
                Controls.Add(button); Controls.Add(label);
                Controls.Add(instruction);
                button.MouseEnter += delegate { enters++; };
                button.MouseHover += delegate { hovers++; };
                button.Click += delegate { clicks++; lastClickTimestamp = Stopwatch.GetTimestamp(); };
                button.MouseDown += delegate { downs++; };
                button.MouseUp += delegate { ups++; };
                button.GotFocus += delegate { focuses++; };
                Activated += delegate { activations++; };
                timer.Tick += delegate { TickFixture(); };
            }
            protected override bool ShowWithoutActivation { get { return true; } }
            protected override CreateParams CreateParams
            {
                get { CreateParams cp = base.CreateParams; cp.ExStyle |= Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW; return cp; }
            }
            protected override void OnShown(EventArgs e) { base.OnShown(e); ActiveControl = null; timer.Start(); }
            private void TickFixture()
            {
                if (age.Elapsed.TotalSeconds > (command == "manual" ? 180 : 24)) { Close(); return; }
                string next = channel.Command;
                if (next != command)
                {
                    command = next;
                    if (command == "manual")
                    {
                        button.Text = "Click once to test pass-through";
                        instruction.Text = "Wait for the amber glow, then physically click the button once.";
                    }
                    if (command == "disable") button.Enabled = false;
                    if (command == "panel")
                    {
                        button.Enabled = true;
                        panel = new Panel { Bounds = button.Bounds, BackColor = Color.SlateGray };
                        Controls.Add(panel); panel.BringToFront();
                    }
                    if (command == "occlude")
                    {
                        button.Enabled = true;
                        cover = new FixtureCover { Bounds = RectangleToScreen(button.Bounds) };
                        cover.Show();
                    }
                    if (command == "restore")
                    {
                        if (cover != null) { cover.Dispose(); cover = null; }
                        if (panel != null) { panel.Dispose(); panel = null; }
                    }
                    if (command == "close") { Close(); return; }
                }
                Rectangle b = RectangleToScreen(button.Bounds), l = RectangleToScreen(label.Bounds);
                int hotState = ReadOwnButtonState() & 0x0200; // BST_HOT, a query on this fixture's own native button.
                if (initialHotState < 0) initialHotState = hotState;
                else if (hotState != initialHotState) hotChanges++;
                Dictionary<string, object> data = new Dictionary<string, object> {
                    {"pid", Process.GetCurrentProcess().Id}, {"root", Handle.ToInt64()},
                    {"buttonHwnd", button.Handle.ToInt64()}, {"buttonHotState", hotState}, {"hotChanges", hotChanges},
                    {"x", b.X}, {"y", b.Y}, {"w", b.Width}, {"h", b.Height},
                    {"labelX", l.X + l.Width / 2}, {"labelY", l.Y + l.Height / 2},
                    {"enters", enters}, {"hovers", hovers}, {"clicks", clicks}, {"focuses", focuses}, {"activations", activations},
                    {"downs", downs}, {"ups", ups},
                    {"lastClickTimestamp", lastClickTimestamp},
                    {"command", command}
                };
                channel.WriteState(data);
            }
            private int ReadOwnButtonState()
            {
                if (HoverProbe.WindowProcessId(button.Handle) != Process.GetCurrentProcess().Id || !IsChild(Handle, button.Handle))
                    throw new InvalidOperationException("Native button-state query must remain inside the owned fixture.");
                return (int)ReadButtonStateMessage(button.Handle, 0x00F2, IntPtr.Zero, IntPtr.Zero); // BM_GETSTATE only.
            }
            protected override void OnFormClosed(FormClosedEventArgs e)
            {
                timer.Dispose(); channel.Dispose(); if (cover != null) cover.Dispose(); base.OnFormClosed(e);
            }
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct HitPoint { public int X, Y; }
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(HitPoint point);
        [DllImport("user32.dll")] private static extern bool IsChild(IntPtr parent, IntPtr child);
        [DllImport("user32.dll", EntryPoint = "SendMessageW")] private static extern IntPtr ReadButtonStateMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        private sealed class FixtureCover : Form
        {
            public FixtureCover()
            {
                Text = "Halloweb test occluder"; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false;
                TopMost = true; AutoScaleMode = AutoScaleMode.None; StartPosition = FormStartPosition.Manual;
                BackColor = Color.SlateGray;
            }
            protected override bool ShowWithoutActivation { get { return true; } }
            protected override CreateParams CreateParams
            {
                get { CreateParams cp = base.CreateParams; cp.ExStyle |= Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW; return cp; }
            }
        }

        private sealed class Harness : Form
        {
            private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 50 };
            private readonly Stopwatch clock = Stopwatch.StartNew();
            private readonly HoverGlow glow = new HoverGlow();
            private readonly HoverProbe probe = new HoverProbe(Process.GetCurrentProcess().Id);
            private readonly bool manualOnly;
            private long manualGlowTimestamp;
            private GhostCuriosity curiosity = new GhostCuriosity();
            private readonly List<string> ghostStates = new List<string>();
            private Process fixture;
            private FixtureChannel channel;
            private Native.POINT initialCursor;
            private IntPtr initialForeground;
            private Dictionary<string, object> baseline;
            private int phase, verified, stableSamples, heldFrames;
            private double phaseSince, lastTick;
            private Rectangle button;
            private PointF point;
            private PointF heldPosition;
            private bool hasHeldPosition, sawGhostGlow, sawDeparture;
            private IntPtr fixtureRoot, buttonWindow;
            private int fixturePid;
            private long revokedLookups, occludedLookups;
            private HoverTarget closingTarget;
            private bool pointerStayed = true, focusStayed = true;
            public Harness(bool manual)
            {
                manualOnly = manual;
                Text = "Halloweb hover verification spider";
                FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
                AutoScaleMode = AutoScaleMode.None; StartPosition = FormStartPosition.Manual;
                ClientSize = new Size(160, 160); Location = new Point(-32000, -32000);
                timer.Tick += delegate { TickCheck(); };
            }
            protected override bool ShowWithoutActivation { get { return true; } }
            protected override CreateParams CreateParams
            {
                get { CreateParams cp = base.CreateParams; cp.ExStyle |= HoverGlow.RequiredExtendedStyles; return cp; }
            }
            protected override void WndProc(ref Message m)
            {
                if (m.Msg == 0x0084) { m.Result = new IntPtr(-1); return; }
                if (m.Msg == 0x0021) { m.Result = new IntPtr(3); return; }
                base.WndProc(ref m);
            }
            protected override void OnShown(EventArgs e)
            {
                base.OnShown(e);
                try
                {
                    Require(Native.GetCursorPos(out initialCursor), "live pointer polling available");
                    initialForeground = Native.GetForegroundWindow();
                    string channelName = "Local\\HallowebGhostFixture-" + Guid.NewGuid().ToString("N");
                    channel = new FixtureChannel(channelName, true);
                    fixture = Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--hover-fixture " + channelName) {
                        UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
                    });
                    timer.Start();
                }
                catch (Exception error) { Fail(error); }
            }
            private void Require(bool condition, string description)
            {
                if (!condition) throw new InvalidOperationException(description); verified++;
            }
            private void MoveSpider(PointF p)
            {
                RenderSpider(p, false);
                glow.Clear(); stableSamples = 0; phaseSince = clock.Elapsed.TotalSeconds;
            }
            private void RenderSpider(PointF p, bool running)
            {
                point = p;
                using (Bitmap art = SpiderArt.Draw(160, 90, running, clock.Elapsed.TotalSeconds, 1))
                    Native.SetBitmap(Handle, art, (int)p.X - 80, (int)p.Y - 80);
            }
            private void RecordGhostState()
            {
                if (ghostStates.Count == 0 || ghostStates[ghostStates.Count - 1] != curiosity.State) ghostStates.Add(curiosity.State);
            }
            private void UpdateGhost(HoverTarget target, bool valid, double dt, double now, bool threat)
            {
                GhostCandidate candidate = valid ? new GhostCandidate(target.Bounds, target.Kind) : null;
                curiosity.Update(dt, now, point, candidate, true, threat);
                RecordGhostState();
                glow.UpdateTarget(curiosity.WantsGlow ? curiosity.GlowBounds : null);
                if (curiosity.WantsGlow && glow.Visible) curiosity.ConfirmRendered();
            }
            private bool GhostHasReceipt(string outcome)
            {
                foreach (GhostReceipt receipt in curiosity.Ledger) if (receipt.Outcome == outcome) return true;
                return false;
            }
            private void VerifyOwnedCounters(Dictionary<string, object> state)
            {
                foreach (string counter in new string[] { "enters", "hovers", "clicks", "focuses", "activations", "downs", "ups", "hotChanges" })
                    Require(Convert.ToInt32(state[counter]) == Convert.ToInt32(baseline[counter]), "owned fixture received no " + counter + " change");
                Require(Convert.ToInt32(state["buttonHotState"]) == Convert.ToInt32(baseline["buttonHotState"]), "native button BST_HOT stayed unchanged");
            }
            private void TickCheck()
            {
                try
                {
                    if (manualOnly) { TickManual(); return; }
                    Native.POINT sampledCursor;
                    pointerStayed &= Native.GetCursorPos(out sampledCursor) && sampledCursor.X == initialCursor.X && sampledCursor.Y == initialCursor.Y;
                    focusStayed &= Native.GetForegroundWindow() == initialForeground;
                    if (clock.Elapsed.TotalSeconds > 20) throw new TimeoutException("Hover test timed out in phase " + phase);
                    double now = clock.Elapsed.TotalSeconds;
                    double dt = lastTick == 0 ? 0.05 : Math.Max(0, Math.Min(0.1, now - lastTick));
                    lastTick = now;
                    Dictionary<string, object> state = channel.ReadState();
                    if (state == null) return;
                    if (phase == 0)
                    {
                        baseline = state;
                        fixturePid = Convert.ToInt32(state["pid"]);
                        fixtureRoot = new IntPtr(Convert.ToInt64(state["root"]));
                        buttonWindow = new IntPtr(Convert.ToInt64(state["buttonHwnd"]));
                        Require(fixturePid == fixture.Id && HoverProbe.WindowProcessId(fixtureRoot) == fixturePid &&
                            HoverProbe.WindowProcessId(buttonWindow) == fixturePid, "all test geometry belongs to the launched child fixture");
                        button = new Rectangle(Convert.ToInt32(state["x"]), Convert.ToInt32(state["y"]), Convert.ToInt32(state["w"]), Convert.ToInt32(state["h"]));
                        MoveSpider(new PointF(button.X + button.Width / 2, button.Y + button.Height / 2));
                        probe.Request(point, fixtureRoot);
                        Require(probe.UiaLookupCount == 0, "unapproved fixture cannot cause a UIA lookup");
                        phase = 1;
                        return;
                    }
                    if (phase == 1)
                    {
                        probe.Request(point, fixtureRoot);
                        if (now - phaseSince < 0.35) return;
                        Require(probe.UiaLookupCount == 0 && probe.Latest == null, "unapproved requests remain UIA-free across multiple worker intervals");
                        Require(probe.ApproveWindow(fixtureRoot, fixturePid), "owned fixture explicitly approved");
                        MoveSpider(new PointF(button.Left - 90, button.Top + button.Height / 2));
                        RecordGhostState(); phase = 2; return;
                    }
                    if (phase == 11)
                    {
                        if (fixture.HasExited)
                        {
                            IntPtr currentRoot = HoverProbe.FindExternalRootAtPoint(point, Process.GetCurrentProcess().Id);
                            Require(currentRoot != closingTarget.RootWindow, "closed fixture no longer owns the point");
                            bool stillValid = closingTarget.IsFresh(point, currentRoot, 300);
                            glow.UpdateTarget(stillValid ? (Rectangle?)Rectangle.Round(closingTarget.Bounds) : null);
                            Require(!glow.Visible, "closed fixture leaves no glow");
                            Require(!probe.IsApprovedWindow(fixtureRoot, HoverProbe.WindowProcessId(fixtureRoot)), "closing the fixture invalidates its approval");
                            Require(pointerStayed && focusStayed, "pointer and focus stayed unchanged across all sampled ticks");
                            File.WriteAllText(FilePath("hover-smoke-test-results.txt"), "PASS: " + verified +
                                " owned LIVE-SPIDER-12 checks. Rendered spider walked into an approved Button, physically paused, " +
                                "acknowledged ghost glow, departed, and cleared glow on threat. Windows hit testing passed through spider/glow " +
                                "to the owned native button. Unapproved/revoked requests caused no new UIA; text, disabled, panel-covered, " +
                                "occluded, and closed targets declined. Native BST_HOT and hover/down/up/click/focus counters stayed unchanged. " +
                                "Real pointer and foreground window stayed unchanged. MenuItem classification covered by pure checks only.\r\n" +
                                "Ghost states: " + string.Join(" -> ", ghostStates.ToArray()) + "\r\n");
                            Close();
                        }
                        return;
                    }
                    IntPtr root = HoverProbe.FindExternalRootAtPoint(point, Process.GetCurrentProcess().Id);
                    probe.Request(point, root);
                    HoverTarget target = probe.Latest;
                    bool valid = target != null && target.IsFresh(point, root, 300);
                    if (phase == 2)
                    {
                        UpdateGhost(target, valid, dt, now, false);
                        if (curiosity.HoldMovement)
                        {
                            heldFrames++;
                            if (!hasHeldPosition) { heldPosition = point; hasHeldPosition = true; }
                            Require(point == heldPosition, "curiosity hold preserves actual spider coordinates");
                            Native.RECT spiderRect; Native.GetWindowRect(Handle, out spiderRect);
                            Require(Math.Abs((spiderRect.Left + spiderRect.Right) / 2.0 - point.X) <= 1 &&
                                Math.Abs((spiderRect.Top + spiderRect.Bottom) / 2.0 - point.Y) <= 1, "rendered spider physically holds at the observed control");
                        }
                        if (curiosity.WantsGlow && glow.Visible)
                        {
                            sawGhostGlow = true;
                            if (stableSamples++ == 0)
                            {
                                Require(valid && target.Kind == HoverControlKind.Button && target.Bounds == button, "ghost observes the approved owned Button");
                                Require((Native.GetWindowLong(glow.Handle, -20) & HoverGlow.RequiredExtendedStyles) == HoverGlow.RequiredExtendedStyles, "ghost glow stays layered, click-through, and nonactivating");
                                Native.RECT glowRect; Native.GetWindowRect(glow.Handle, out glowRect);
                                Require(glowRect.Left == button.Left - 6 && glowRect.Top == button.Top - 6, "ghost glow aligns to physical button bounds");
                                IntPtr hit = WindowFromPoint(new HitPoint { X = (int)Math.Round(point.X), Y = (int)Math.Round(point.Y) });
                                Require(hit == buttonWindow && HoverProbe.WindowProcessId(hit) == fixturePid,
                                    "Windows point hit testing passes through the spider and glow to the owned button");
                            }
                        }
                        if (curiosity.State == "DEPART")
                        {
                            sawDeparture = true;
                            Require(!curiosity.HoldMovement && !curiosity.WantsGlow && !glow.Visible, "departure immediately releases movement and clears glow");
                        }
                        if (!curiosity.HoldMovement) RenderSpider(new PointF(point.X + (float)(120 * dt), point.Y), true);
                        if (point.X > button.Right + 16)
                        {
                            Require(sawGhostGlow && sawDeparture && heldFrames >= 3, "the rendered walking spider completed observation, hover, and departure");
                            Require(ghostStates.IndexOf("TARGET_DETECTED") >= 0 && ghostStates.IndexOf("OBSERVE") > ghostStates.IndexOf("TARGET_DETECTED") &&
                                ghostStates.IndexOf("GHOST_HOVER") > ghostStates.IndexOf("OBSERVE") && ghostStates.IndexOf("DEPART") > ghostStates.IndexOf("GHOST_HOVER"),
                                "curiosity states occur in physical encounter order");
                            Require(GhostHasReceipt("TARGET_OBSERVED") && GhostHasReceipt("GHOST_HOVER_RENDERED") && GhostHasReceipt("NO_INPUT_DISPATCHED"),
                                "ghost receipts follow observation and acknowledged rendering");
                            VerifyOwnedCounters(state);
                            curiosity = new GhostCuriosity();
                            MoveSpider(new PointF(button.Left + button.Width / 2, button.Top + button.Height / 2)); phase = 3;
                        }
                        return;
                    }
                    if (phase == 3)
                    {
                        UpdateGhost(target, valid, dt, now, false);
                        if (curiosity.State == "GHOST_HOVER" && glow.Visible)
                        {
                            UpdateGhost(target, valid, 0, now, true); // Pure test threat, never real pointer input.
                            Require(curiosity.State == "DEPART" && !curiosity.HoldMovement && !curiosity.WantsGlow && !glow.Visible,
                                "a pure threat signal immediately preempts actual ghost rendering");
                            Require(GhostHasReceipt("OBSERVATION_DECLINED"), "threat preemption records an observation decline");
                            MoveSpider(new PointF(Convert.ToInt32(state["labelX"]), Convert.ToInt32(state["labelY"]))); phase = 4;
                        }
                        return;
                    }
                    glow.UpdateTarget(valid ? (Rectangle?)Rectangle.Round(target.Bounds) : null);
                    if (phase == 4 && now - phaseSince > 0.7)
                    {
                        Require(!valid && !glow.Visible, "plain text does not receive button glow");
                        channel.Command = "disable";
                        MoveSpider(new PointF(button.X + button.Width / 2, button.Y + button.Height / 2)); phase = 5;
                    }
                    else if (phase == 5 && (string)state["command"] == "disable" && now - phaseSince > 0.7)
                    {
                        Require(!valid && !glow.Visible, "disabled control clears glow");
                        channel.Command = "panel"; phaseSince = now; phase = 6;
                    }
                    else if (phase == 6 && (string)state["command"] == "panel" && now - phaseSince > 0.7)
                    {
                        Require(!valid && !glow.Visible, "an in-app panel covering the button prevents glow");
                        channel.Command = "occlude"; phaseSince = now; phase = 7; occludedLookups = -1;
                    }
                    else if (phase == 7 && (string)state["command"] == "occlude")
                    {
                        if (occludedLookups < 0) { occludedLookups = probe.UiaLookupCount; phaseSince = now; return; }
                        if (now - phaseSince <= 0.7) return;
                        Require(!valid && !glow.Visible, "another window covering the button prevents glow");
                        Require(probe.UiaLookupCount == occludedLookups, "the unapproved occluding root causes no UIA lookup");
                        VerifyOwnedCounters(state);
                        Native.POINT cursor; Require(Native.GetCursorPos(out cursor) && cursor.X == initialCursor.X && cursor.Y == initialCursor.Y, "real pointer stayed in place");
                        Require(Native.GetForegroundWindow() == initialForeground, "keyboard focus retained");
                        channel.Command = "restore"; phaseSince = now; phase = 8;
                    }
                    else if (phase == 8 && (string)state["command"] == "restore" && valid && target.Bounds == button && glow.Visible)
                    {
                        probe.RevokeApproval(); revokedLookups = probe.UiaLookupCount; glow.Clear();
                        Require(probe.Latest == null && !glow.Visible, "revocation immediately clears the observed target and glow");
                        phaseSince = now; phase = 9;
                    }
                    else if (phase == 9 && now - phaseSince > 0.7)
                    {
                        Require(probe.UiaLookupCount == revokedLookups && probe.Latest == null && !glow.Visible,
                            "revoked requests produce zero new UIA lookups or glow");
                        Require(probe.ApproveWindow(fixtureRoot, fixturePid), "only explicit renewed approval resumes observation");
                        phaseSince = now; phase = 10;
                    }
                    else if (phase == 10 && valid && target.Bounds == button && glow.Visible)
                    {
                        VerifyOwnedCounters(state);
                        closingTarget = target;
                        channel.Command = "close"; phase = 11;
                    }
                }
                catch (Exception error) { Fail(error); }
            }
            private void TickManual()
            {
                if (clock.Elapsed.TotalSeconds > 180) throw new TimeoutException("Manual owned-window click check timed out while waiting for a physical click.");
                Dictionary<string, object> state = channel.ReadState();
                if (state == null) return;
                Rectangle currentButton = new Rectangle(Convert.ToInt32(state["x"]), Convert.ToInt32(state["y"]),
                    Convert.ToInt32(state["w"]), Convert.ToInt32(state["h"]));
                if (phase == 0)
                {
                    baseline = state;
                    fixturePid = Convert.ToInt32(state["pid"]);
                    fixtureRoot = new IntPtr(Convert.ToInt64(state["root"]));
                    buttonWindow = new IntPtr(Convert.ToInt64(state["buttonHwnd"]));
                    Require(fixturePid == fixture.Id && HoverProbe.WindowProcessId(fixtureRoot) == fixturePid &&
                        HoverProbe.WindowProcessId(buttonWindow) == fixturePid, "manual check remains inside its own child fixture");
                    Require(probe.ApproveWindow(fixtureRoot, fixturePid), "manual fixture explicitly approved");
                    channel.Command = "manual";
                    button = currentButton;
                    MoveSpider(new PointF(button.Left + button.Width / 2, button.Top + button.Height / 2));
                    phase = 1;
                    return;
                }
                if (currentButton != button)
                {
                    button = currentButton;
                    manualGlowTimestamp = 0;
                    MoveSpider(new PointF(button.Left + button.Width / 2, button.Top + button.Height / 2));
                }
                IntPtr root = HoverProbe.FindExternalRootAtPoint(point, Process.GetCurrentProcess().Id);
                if (root != fixtureRoot || !probe.IsApprovedWindow(root, fixturePid))
                { glow.Clear(); manualGlowTimestamp = 0; return; }
                probe.Request(point, root);
                HoverTarget target = probe.Latest;
                if (manualGlowTimestamp == 0)
                {
                    if (target == null || !target.IsFresh(point, root, 300) || target.Bounds != button) return;
                    glow.UpdateTarget(button);
                    if (!glow.Visible) return;
                    manualGlowTimestamp = Stopwatch.GetTimestamp();
                }
                // An owned laboratory artifact stays positioned for the human;
                // this does not add persistence or stale-target permission to the app.
                glow.UpdateTarget(button);
                IntPtr hit = WindowFromPoint(new HitPoint { X = (int)Math.Round(point.X), Y = (int)Math.Round(point.Y) });
                if (hit != buttonWindow || HoverProbe.WindowProcessId(hit) != fixturePid)
                { glow.Clear(); manualGlowTimestamp = 0; return; }
                long clickedAt = Convert.ToInt64(state["lastClickTimestamp"]);
                if (Convert.ToInt32(state["clicks"]) <= Convert.ToInt32(baseline["clicks"]) || clickedAt < manualGlowTimestamp) return;
                Require(glow.Visible && (Native.GetWindowLong(glow.Handle, -20) & HoverGlow.RequiredExtendedStyles) == HoverGlow.RequiredExtendedStyles &&
                    (Native.GetWindowLong(Handle, -20) & HoverGlow.RequiredExtendedStyles) == HoverGlow.RequiredExtendedStyles,
                    "physical click crossed the visible click-through spider and ghost glow");
                Require(Convert.ToInt32(state["downs"]) > Convert.ToInt32(baseline["downs"]) &&
                    Convert.ToInt32(state["ups"]) > Convert.ToInt32(baseline["ups"]), "owned button received physical down, up, and click events");
                File.WriteAllText(FilePath("manual-click-test-results.txt"),
                    "PASS: a human physical click reached the owned native Button through the visible click-through spider and ghost glow. " +
                    "Owned button down/up/click counters increased after glow rendering; WindowFromPoint reached that same Button HWND. " +
                    "Both overlays retained layered/transparent/no-activation styles. No automated input was dispatched.\r\n");
                Close();
            }
            private void Fail(Exception error)
            {
                File.WriteAllText(FilePath(manualOnly ? "manual-click-test-results.txt" : "hover-smoke-test-results.txt"), "FAIL: " + error + "\r\n" +
                    "Phase: " + phase + "\r\nProbe outcome: " + probe.LatestOutcome +
                    "\r\nUIA lookup count: " + probe.UiaLookupCount + "\r\nCompleted probes: " + probe.CompletedProbes +
                    "\r\nGhost states: " + string.Join(" -> ", ghostStates.ToArray()) +
                    "\r\nSaw glow: " + sawGhostGlow + "\r\nSaw departure: " + sawDeparture +
                    "\r\nHeld frames: " + heldFrames + "\r\n");
                Environment.ExitCode = 1; Close();
            }
            protected override void OnFormClosed(FormClosedEventArgs e)
            {
                timer.Stop(); timer.Dispose(); probe.Dispose(); glow.Dispose();
                if (channel != null) { channel.Command = "close"; channel.Dispose(); }
                if (fixture != null) fixture.Dispose();
                base.OnFormClosed(e);
            }
        }
    }
}
