using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
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
        public static void RunFixture()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (Fixture fixture = new Fixture()) Application.Run(fixture);
        }
        public static void Run()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (Harness harness = new Harness()) Application.Run(harness);
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
            check(!HoverProbe.TryControlKind(System.Windows.Automation.ControlType.Edit.Id, out kind));
            check(!HoverProbe.TryControlKind(System.Windows.Automation.ControlType.ListItem.Id, out kind));
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

        private sealed class Fixture : Form
        {
            private readonly Button button = new Button { Text = "Harmless test button", Bounds = new Rectangle(50, 80, 220, 44), TabStop = false };
            private readonly Label label = new Label { Text = "This label must not glow", Bounds = new Rectangle(50, 160, 220, 44) };
            private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 40 };
            private readonly Stopwatch age = Stopwatch.StartNew();
            private int enters, hovers, clicks, focuses, activations;
            private Form cover;
            private Panel panel;
            private string command = "";
            public Fixture()
            {
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
                Controls.Add(new Label { Text = "Visual-only spider glow verification", Bounds = new Rectangle(24, 20, 320, 32) });
                button.MouseEnter += delegate { enters++; };
                button.MouseHover += delegate { hovers++; };
                button.Click += delegate { clicks++; };
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
                if (age.Elapsed.TotalSeconds > 24) { Close(); return; }
                string next = File.Exists(FilePath("hover-fixture-command.txt")) ? File.ReadAllText(FilePath("hover-fixture-command.txt")) : "";
                if (next != command)
                {
                    command = next;
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
                Dictionary<string, object> data = new Dictionary<string, object> {
                    {"pid", Process.GetCurrentProcess().Id}, {"root", Handle.ToInt64()},
                    {"x", b.X}, {"y", b.Y}, {"w", b.Width}, {"h", b.Height},
                    {"labelX", l.X + l.Width / 2}, {"labelY", l.Y + l.Height / 2},
                    {"enters", enters}, {"hovers", hovers}, {"clicks", clicks}, {"focuses", focuses}, {"activations", activations},
                    {"command", command}
                };
                try { File.WriteAllText(FilePath("hover-fixture-state.json"), new JavaScriptSerializer().Serialize(data)); }
                catch (IOException) { }
            }
            protected override void OnFormClosed(FormClosedEventArgs e)
            {
                timer.Dispose(); if (cover != null) cover.Dispose(); base.OnFormClosed(e);
            }
        }
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
            private Process fixture;
            private Native.POINT initialCursor;
            private IntPtr initialForeground;
            private Dictionary<string, object> baseline;
            private int phase, verified, stableSamples;
            private double phaseSince;
            private Rectangle button;
            private PointF point;
            private HoverTarget closingTarget;
            private bool pointerStayed = true, focusStayed = true;
            public Harness()
            {
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
                    File.Delete(FilePath("hover-fixture-state.json"));
                    File.WriteAllText(FilePath("hover-fixture-command.txt"), "");
                    fixture = Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--hover-fixture") {
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
                point = p;
                using (Bitmap art = SpiderArt.Draw(160, 0, false, 0, 1))
                    Native.SetBitmap(Handle, art, (int)p.X - 80, (int)p.Y - 80);
                glow.Clear(); stableSamples = 0; phaseSince = clock.Elapsed.TotalSeconds;
            }
            private void TickCheck()
            {
                try
                {
                    Native.POINT sampledCursor;
                    pointerStayed &= Native.GetCursorPos(out sampledCursor) && sampledCursor.X == initialCursor.X && sampledCursor.Y == initialCursor.Y;
                    focusStayed &= Native.GetForegroundWindow() == initialForeground;
                    if (clock.Elapsed.TotalSeconds > 20) throw new TimeoutException("Hover test timed out in phase " + phase);
                    Dictionary<string, object> state;
                    try { state = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(FilePath("hover-fixture-state.json"))); }
                    catch (IOException) { return; }
                    if (phase == 0)
                    {
                        baseline = state;
                        button = new Rectangle(Convert.ToInt32(state["x"]), Convert.ToInt32(state["y"]), Convert.ToInt32(state["w"]), Convert.ToInt32(state["h"]));
                        MoveSpider(new PointF(button.X + button.Width / 2, button.Y + button.Height / 2));
                        phase = 1;
                    }
                    if (phase == 7)
                    {
                        if (fixture.HasExited)
                        {
                            IntPtr currentRoot = HoverProbe.FindExternalRootAtPoint(point, Process.GetCurrentProcess().Id);
                            Require(currentRoot != closingTarget.RootWindow, "closed fixture no longer owns the point");
                            bool stillValid = closingTarget.IsFresh(point, currentRoot, 300);
                            glow.UpdateTarget(stillValid ? (Rectangle?)Rectangle.Round(closingTarget.Bounds) : null);
                            Require(!glow.Visible, "closed fixture leaves no glow");
                            Require(pointerStayed && focusStayed, "pointer and focus stayed unchanged across all sampled ticks");
                            File.WriteAllText(FilePath("hover-smoke-test-results.txt"), "PASS: " + verified + " live hover checks. Button detected beneath spider and glow; disabled, text, occluded, and closed targets clear. No fixture hover/click/focus events during probing; real pointer and foreground window unchanged.\r\n");
                            Close();
                        }
                        return;
                    }
                    IntPtr root = HoverProbe.FindExternalRootAtPoint(point, Process.GetCurrentProcess().Id);
                    probe.Request(point, root);
                    HoverTarget target = probe.Latest;
                    bool valid = target != null && target.IsFresh(point, root, 300);
                    glow.UpdateTarget(valid ? (Rectangle?)Rectangle.Round(target.Bounds) : null);
                    if (phase == 1 && valid && target.Bounds == button && glow.Visible)
                    {
                        if (++stableSamples < 6) return;
                        Require(root == new IntPtr(Convert.ToInt64(state["root"])), "detects external fixture beneath spider and glow");
                        Require((Native.GetWindowLong(glow.Handle, -20) & HoverGlow.RequiredExtendedStyles) == HoverGlow.RequiredExtendedStyles, "glow is layered, click-through, nonactivating tool window");
                        Native.RECT rectangle; Native.GetWindowRect(glow.Handle, out rectangle);
                        Require(rectangle.Left == button.Left - 6 && rectangle.Top == button.Top - 6, "glow aligns to physical button bounds");
                        glow.Clear(); Require(!glow.Visible, "toggle/clear hides glow immediately");
                        MoveSpider(new PointF(Convert.ToInt32(state["labelX"]), Convert.ToInt32(state["labelY"]))); phase = 2;
                    }
                    else if (phase == 2 && clock.Elapsed.TotalSeconds - phaseSince > 0.7)
                    {
                        Require(!valid && !glow.Visible, "plain text does not receive button glow");
                        File.WriteAllText(FilePath("hover-fixture-command.txt"), "disable");
                        MoveSpider(new PointF(button.X + button.Width / 2, button.Y + button.Height / 2)); phase = 3;
                    }
                    else if (phase == 3 && (string)state["command"] == "disable" && clock.Elapsed.TotalSeconds - phaseSince > 0.7)
                    {
                        Require(!valid && !glow.Visible, "disabled control clears glow");
                        File.WriteAllText(FilePath("hover-fixture-command.txt"), "panel"); phaseSince = clock.Elapsed.TotalSeconds; phase = 4;
                    }
                    else if (phase == 4 && (string)state["command"] == "panel" && clock.Elapsed.TotalSeconds - phaseSince > 0.7)
                    {
                        Require(!valid && !glow.Visible, "an in-app panel covering the button prevents glow");
                        File.WriteAllText(FilePath("hover-fixture-command.txt"), "occlude"); phaseSince = clock.Elapsed.TotalSeconds; phase = 5;
                    }
                    else if (phase == 5 && (string)state["command"] == "occlude" && clock.Elapsed.TotalSeconds - phaseSince > 0.7)
                    {
                        Require(!valid && !glow.Visible, "another window covering the button prevents glow");
                        foreach (string counter in new string[] { "enters", "hovers", "clicks", "focuses", "activations" })
                            Require(Convert.ToInt32(state[counter]) == Convert.ToInt32(baseline[counter]), "fixture received no " + counter + " events");
                        Native.POINT cursor; Require(Native.GetCursorPos(out cursor) && cursor.X == initialCursor.X && cursor.Y == initialCursor.Y, "real pointer stayed in place");
                        Require(Native.GetForegroundWindow() == initialForeground, "keyboard focus retained");
                        File.WriteAllText(FilePath("hover-fixture-command.txt"), "restore"); phaseSince = clock.Elapsed.TotalSeconds; phase = 6;
                    }
                    else if (phase == 6 && (string)state["command"] == "restore" && valid && target.Bounds == button && glow.Visible)
                    {
                        closingTarget = target;
                        File.WriteAllText(FilePath("hover-fixture-command.txt"), "close"); phase = 7;
                    }
                }
                catch (Exception error) { Fail(error); }
            }
            private void Fail(Exception error)
            {
                File.WriteAllText(FilePath("hover-smoke-test-results.txt"), "FAIL: " + error + "\r\n");
                Environment.ExitCode = 1; Close();
            }
            protected override void OnFormClosed(FormClosedEventArgs e)
            {
                timer.Stop(); timer.Dispose(); probe.Dispose(); glow.Dispose();
                File.WriteAllText(FilePath("hover-fixture-command.txt"), "close");
                if (fixture != null) fixture.Dispose();
                base.OnFormClosed(e);
            }
        }
    }
}
