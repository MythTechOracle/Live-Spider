using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using System.Web.Script.Serialization;

namespace SpookyDesktopSpider
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            bool selfTest = Array.IndexOf(args, "--self-test") >= 0;
            bool smokeTest = Array.IndexOf(args, "--smoke-test") >= 0;
            try
            {
                try { Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); }
                catch (EntryPointNotFoundException) { Native.SetProcessDPIAware(); }
                if (selfTest) { Checks.Run(); return; }
                int fixtureArgument = Array.IndexOf(args, "--hover-fixture");
                if (fixtureArgument >= 0)
                {
                    if (fixtureArgument + 1 >= args.Length) throw new ArgumentException("Fixture channel required.");
                    HoverIntegrationChecks.RunFixture(args[fixtureArgument + 1]); return;
                }
                if (Array.IndexOf(args, "--hover-smoke-test") >= 0) { HoverIntegrationChecks.Run(); return; }
                if (Array.IndexOf(args, "--hover-manual-check") >= 0) { HoverIntegrationChecks.RunManual(); return; }
                if (Array.IndexOf(args, "--quit") >= 0)
                {
                    IntPtr existing = Native.FindWindow(null, SpiderForm.WindowTitle);
                    if (existing != IntPtr.Zero) Native.PostMessage(existing, Native.QuitMessage, IntPtr.Zero, IntPtr.Zero);
                    return;
                }
                if (Array.IndexOf(args, "--status") >= 0)
                {
                    IntPtr existing = Native.FindWindow(null, SpiderForm.WindowTitle);
                    if (existing != IntPtr.Zero) Native.PostMessage(existing, Native.StatusMessage, IntPtr.Zero, IntPtr.Zero);
                    return;
                }
                if (Array.IndexOf(args, "--ledger") >= 0)
                {
                    IntPtr existing = Native.FindWindow(null, SpiderForm.WindowTitle);
                    if (existing != IntPtr.Zero) Native.PostMessage(existing, Native.LedgerMessage, IntPtr.Zero, IntPtr.Zero);
                    return;
                }
                bool created;
                using (Mutex instance = new Mutex(true, smokeTest ? "Local\\Halloweb.Portable.Smoke" : "Local\\Halloweb.Portable.v1", out created))
                {
                    if (!created) return;
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
                    using (SpiderForm spider = new SpiderForm(smokeTest)) Application.Run(spider);
                }
            }
            catch (Exception error)
            {
                string report = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "spider-error.txt");
                File.WriteAllText(report, error.ToString());
                if (!selfTest && !smokeTest)
                    MessageBox.Show("The spider could not start. Details are saved in:\n" + report,
                        "Halloweb", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Environment.ExitCode = 1;
            }
        }
    }

    // All movement and rendering coordinates are physical screen pixels.
    internal sealed class EscapeMotion
    {
        private readonly Random random;
        public PointF Position;
        public PointF Target;
        public double Rotation;
        public double WantedRotation;
        public RectangleF Bounds;
        public double NextDecision;

        public EscapeMotion(PointF start, RectangleF bounds, int seed)
        {
            random = new Random(seed);
            Bounds = bounds;
            Position = Target = Clamp(start);
        }

        public static float Distance(PointF a, PointF b)
        {
            float x = a.X - b.X, y = a.Y - b.Y;
            return (float)Math.Sqrt(x * x + y * y);
        }

        public PointF Clamp(PointF p)
        {
            return new PointF(Math.Max(Bounds.Left, Math.Min(Bounds.Right, p.X)),
                Math.Max(Bounds.Top, Math.Min(Bounds.Bottom, p.Y)));
        }

        public void SetBounds(RectangleF bounds)
        {
            Bounds = bounds;
            Position = Clamp(Position);
            Target = Clamp(Target);
        }

        public bool Moving { get { return Distance(Position, Target) > 0.5f; } }

        public void Think(PointF threat, double now, double scale)
        {
            if (now < NextDecision || Distance(Position, threat) >= 150 * scale) return;
            NextDecision = now + 0.19;
            double away = Math.Atan2(Position.Y - threat.Y, Position.X - threat.X);
            if (Distance(Position, threat) < 0.01f) away = random.NextDouble() * Math.PI * 2;
            double jitter = (random.NextDouble() - 0.5) * 0.85;
            PointF best = Position;
            double bestScore = double.NegativeInfinity;
            // Score several headings; tangential escapes keep corners from trapping it.
            for (int i = 0; i < 32; i++)
            {
                double angle = away + jitter + i * Math.PI * 2 / 32;
                PointF candidate = Clamp(new PointF(
                    Position.X + (float)(Math.Cos(angle) * 160 * scale),
                    Position.Y + (float)(Math.Sin(angle) * 160 * scale)));
                float travel = Distance(Position, candidate);
                if (travel < 20 * scale) continue;
                double clearance = Math.Min(Math.Min(candidate.X - Bounds.Left, Bounds.Right - candidate.X),
                    Math.Min(candidate.Y - Bounds.Top, Bounds.Bottom - candidate.Y));
                double score = Distance(candidate, threat) + travel * 0.22 + Math.Min(clearance, 40 * scale) * 0.12;
                if (score > bestScore) { bestScore = score; best = candidate; }
            }
            Target = best;
            if (Distance(Position, Target) > 0.5f)
                WantedRotation = Math.Atan2(Target.Y - Position.Y, Target.X - Position.X) * 180 / Math.PI + 90;
        }

        public void Step(double seconds, double scale)
        {
            seconds = Math.Max(0, Math.Min(0.05, seconds));
            float distance = Distance(Position, Target);
            if (distance > 0.5f)
            {
                double travel = Math.Min(distance, 820 * scale * seconds);
                Position = Clamp(new PointF(Position.X + (float)((Target.X - Position.X) * travel / distance),
                    Position.Y + (float)((Target.Y - Position.Y) * travel / distance)));
            }
            else Position = Target;
            double difference = ((WantedRotation - Rotation + 540) % 360 + 360) % 360 - 180;
            Rotation += difference * Math.Min(1, seconds * 16);
            Rotation = (Rotation % 360 + 360) % 360;
        }
    }

    internal sealed class SpiderForm : Form
    {
        public const string WindowTitle = "Halloweb";
        private const int SurfaceSize = 160;
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly bool smokeTest;
        private readonly NotifyIcon tray = new NotifyIcon();
        private readonly ToolStripMenuItem pauseItem = new ToolStripMenuItem("Pause spider");
        private readonly ToolStripMenuItem desktopItem = new ToolStripMenuItem("Show only on desktop");
        private readonly ToolStripMenuItem glowItem = new ToolStripMenuItem("Ghost Pointer curiosity");
        private readonly ToolStripMenuItem approveItem = new ToolStripMenuItem();
        private readonly GhostCuriosity curiosity = new GhostCuriosity();
        private readonly List<ToolStripMenuItem> sizes = new List<ToolStripMenuItem>();
        private EcologyMotion motion;
        private WebOverlay webOverlay;
        private HoverProbe hoverProbe;
        private HoverGlow hoverGlow;
        private readonly int ownProcessId = Process.GetCurrentProcess().Id;
        private double scale = 1, lastTime, nextMonitorCheck, forceWeaveUntil;
        private bool paused, desktopOnly, shown = true, pauseHotkey, quitHotkey, approvalHotkey;
        private int smokePhase;
        private Icon trayIcon;

        public SpiderForm(bool smoke)
        {
            smokeTest = smoke;
            Text = smoke ? WindowTitle + " (smoke test)" : WindowTitle;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(SurfaceSize, SurfaceSize);
            Native.POINT cursor;
            Native.GetCursorPos(out cursor);
            Screen screen = Screen.FromPoint(new Point(cursor.X, cursor.Y));
            RectangleF bounds = SafeBounds(screen.WorkingArea);
            PointF start = new PointF(screen.WorkingArea.Left + screen.WorkingArea.Width * 0.58f,
                screen.WorkingArea.Top + screen.WorkingArea.Height * 0.52f);
            motion = new EcologyMotion(start, bounds, Environment.TickCount);
            Location = new Point((int)motion.Position.X - SurfaceSize / 2, (int)motion.Position.Y - SurfaceSize / 2);
            timer.Interval = 75;
            timer.Tick += TickSpider;
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Native.SetWindowPos(Handle, new IntPtr(-1), Left, Top, SurfaceSize, SurfaceSize, 0x0010);
            Native.ShowWindow(Handle, 4);
            pauseHotkey = Native.RegisterHotKey(Handle, 1, 0x4003, (uint)Keys.S);
            quitHotkey = Native.RegisterHotKey(Handle, 2, 0x4003, (uint)Keys.Q);
            approvalHotkey = Native.RegisterHotKey(Handle, 3, 0x4003, (uint)Keys.G);
            SetupTray();
            webOverlay = new WebOverlay();
            hoverGlow = new HoverGlow();
            // Accessibility providers can block. All provider calls stay on the
            // probe's single background worker, never on the animation thread.
            hoverProbe = new HoverProbe(ownProcessId);
            RenderSpider();
            lastTime = clock.Elapsed.TotalSeconds;
            timer.Start();
        }

        private RectangleF SafeBounds(Rectangle area)
        {
            float margin = (float)(64 * scale);
            margin = Math.Min(margin, Math.Max(0, Math.Min(area.Width, area.Height) / 2f - 1));
            return new RectangleF(area.Left + margin, area.Top + margin,
                Math.Max(1, area.Width - 2 * margin), Math.Max(1, area.Height - 2 * margin));
        }

        private void SetupTray()
        {
            using (Bitmap image = SpiderArt.Draw(48, 0, false, 0, 0.44))
            {
                IntPtr iconHandle = image.GetHicon();
                using (Icon temporary = Icon.FromHandle(iconHandle)) trayIcon = (Icon)temporary.Clone();
                Native.DestroyIcon(iconHandle);
            }
            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem title = new ToolStripMenuItem("Halloweb") { Enabled = false };
            menu.Items.Add(title);
            menu.Items.Add(new ToolStripSeparator());
            pauseItem.ShortcutKeyDisplayString = pauseHotkey ? "Ctrl+Alt+S" : "";
            pauseItem.Click += delegate { TogglePause(); };
            menu.Items.Add(pauseItem);
            menu.Items.Add("Bring spider near pointer", null, delegate { Recall(); });
            menu.Items.Add("Build a web now", null, delegate { forceWeaveUntil = clock.Elapsed.TotalSeconds + 90; });
            menu.Items.Add("Clear web and route memory", null, delegate
            {
                motion.ClearWeb();
                forceWeaveUntil = 0;
                if (webOverlay != null) webOverlay.Hide();
            });
            desktopItem.CheckOnClick = true;
            desktopItem.CheckedChanged += delegate { desktopOnly = desktopItem.Checked; };
            menu.Items.Add(desktopItem);
            glowItem.CheckOnClick = true;
            glowItem.Checked = true;
            glowItem.CheckedChanged += delegate { if (!glowItem.Checked) ClearHoverGlow(); };
            menu.Items.Add(glowItem);
            approveItem.Text = approvalHotkey ? "Approve active window: Ctrl+Alt+G" : "Window approval shortcut unavailable";
            approveItem.Enabled = false;
            menu.Items.Add(approveItem);
            menu.Items.Add("Revoke Ghost Pointer window approval", null, delegate
            {
                if (hoverProbe != null) hoverProbe.RevokeApproval();
                ClearHoverGlow("CONSENT_REVOKED");
            });
            ToolStripMenuItem sizeMenu = new ToolStripMenuItem("Spider size");
            AddSize(sizeMenu, "Small", 0.75);
            AddSize(sizeMenu, "Original", 1);
            AddSize(sizeMenu, "Large", 1.35);
            menu.Items.Add(sizeMenu);
            menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem exit = new ToolStripMenuItem("Exit spider");
            exit.ShortcutKeyDisplayString = quitHotkey ? "Ctrl+Alt+Q" : "";
            exit.Click += delegate { Close(); };
            menu.Items.Add(exit);
            tray.Icon = trayIcon;
            tray.Text = "Halloweb - right-click for controls";
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { TogglePause(); };
            tray.Visible = true;
            if (!smokeTest)
            {
                string shortcuts = quitHotkey ? "Ctrl+Alt+Q quits. " : "";
                tray.ShowBalloonTip(6000, "Halloweb is awake!",
                    shortcuts + "Right-click this spider icon for pause, size and exit.", ToolTipIcon.Info);
            }
        }

        private void AddSize(ToolStripMenuItem parent, string label, double value)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(label) { Checked = value == scale };
            item.Click += delegate
            {
                scale = value;
                foreach (ToolStripMenuItem other in sizes) other.Checked = other == item;
                RefreshBounds();
                RenderSpider();
            };
            sizes.Add(item);
            parent.DropDownItems.Add(item);
        }

        private void TogglePause()
        {
            paused = !paused;
            if (paused) ClearHoverGlow("PAUSED");
            pauseItem.Text = paused ? "Resume spider" : "Pause spider";
            tray.Text = paused ? "Halloweb (paused) - right-click for controls" : "Halloweb - right-click for controls";
            RenderSpider();
        }

        private void Recall()
        {
            Native.POINT cursor;
            if (!Native.GetCursorPos(out cursor)) return;
            Screen screen = Screen.FromPoint(new Point(cursor.X, cursor.Y));
            motion.Recall(new PointF(cursor.X + 230, cursor.Y + 100), SafeBounds(screen.WorkingArea));
            forceWeaveUntil = 0;
            if (webOverlay != null) webOverlay.Hide();
            ClearHoverGlow("RECALLED");
            RenderSpider();
        }

        private void RefreshBounds()
        {
            // Stay on a real display, including when a monitor is unplugged.
            PointF previous = motion.Position;
            Screen screen = Screen.FromPoint(Point.Round(motion.Position));
            motion.SetBounds(SafeBounds(screen.WorkingArea));
            if (previous != motion.Position) RenderSpider();
        }

        private bool DesktopHasFocus()
        {
            IntPtr foreground = Native.GetForegroundWindow();
            System.Text.StringBuilder name = new System.Text.StringBuilder(256);
            Native.GetClassName(foreground, name, name.Capacity);
            string value = name.ToString();
            return value == "Progman" || value == "WorkerW" ||
                Native.FindWindowEx(foreground, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero;
        }

        private void TickSpider(object sender, EventArgs args)
        {
            double now = clock.Elapsed.TotalSeconds;
            double elapsed = now - lastTime;
            lastTime = now;
            if (now > nextMonitorCheck) { RefreshBounds(); nextMonitorCheck = now + 1; }
            bool shouldShow = !desktopOnly || DesktopHasFocus();
            if (shouldShow != shown)
            {
                Native.ShowWindow(Handle, shouldShow ? 4 : 0);
                shown = shouldShow;
                if (!shown && webOverlay != null) webOverlay.Hide();
                if (!shown) ClearHoverGlow("HIDDEN");
                if (shown) RenderSpider();
            }
            if (shown && !paused)
            {
                Native.POINT cursor;
                bool wasHolding = motion.CuriosityHolding;
                bool moved = motion.Moving && !wasHolding;
                if (Native.GetCursorPos(out cursor))
                {
                    double idle = ReadIdleSeconds();
                    if (now < forceWeaveUntil) idle = Math.Max(idle, motion.WeaveThreshold);
                    PointF pointer = new PointF(cursor.X, cursor.Y);
                    HoverTarget observed = PrepareCuriosity(elapsed, now, pointer, idle);
                    motion.Update(pointer, elapsed, idle, scale, null, curiosity.HoldMovement);
                    if (curiosity.HoldMovement && !motion.CuriosityHolding) ClearHoverGlow("NOT_EXPLORING");
                    RenderCuriosity(observed);
                }
                else ClearHoverGlow();
                if (moved || (motion.Moving && !motion.CuriosityHolding) || motion.DistanceMoved > 0 || wasHolding != motion.CuriosityHolding) RenderSpider();
            }
            if (webOverlay != null && shown) webOverlay.UpdateWeb(motion.CurrentWeb, scale);
            timer.Interval = shown && !paused && motion.Moving && !motion.CuriosityHolding ? 16 : 75;
            if (smokeTest && now > 0.5 && smokePhase == 0)
            {
                TogglePause();
                if (!paused) throw new InvalidOperationException("Pause check failed.");
                motion.CurrentWeb = new EcologyWeb { Anchor = motion.Position, Progress = 8, Interrupted = false };
                smokePhase = 1;
            }
            if (smokeTest && now > 1 && smokePhase == 1)
            {
                TogglePause();
                if (paused) throw new InvalidOperationException("Resume check failed.");
                smokePhase = 2;
            }
            if (smokeTest && now > 2)
            {
                Checks.CheckWindow(this, motion, webOverlay);
                Close();
            }
        }

        private void RenderSpider()
        {
            if (!IsHandleCreated) return;
            using (Bitmap bitmap = SpiderArt.Draw(SurfaceSize, motion.Rotation,
                !paused && motion.Moving && !motion.CuriosityHolding, clock.Elapsed.TotalSeconds, scale))
            {
                Native.SetBitmap(Handle, bitmap, (int)Math.Round(motion.Position.X) - SurfaceSize / 2,
                    (int)Math.Round(motion.Position.Y) - SurfaceSize / 2);
            }
        }

        private void ClearHoverGlow(string reason = "DISABLED")
        {
            if (hoverGlow != null) hoverGlow.Clear();
            if (hoverProbe != null) hoverProbe.CancelPending();
            curiosity.Reset(reason);
        }

        private HoverTarget PrepareCuriosity(double elapsed, double now, PointF pointer, double idle)
        {
            if (hoverProbe == null || hoverGlow == null) return null;
            PointF point = motion.Position;
            bool threat = EcologyMotion.Distance(point, pointer) < 162 * scale || motion.State == "ESCAPE";
            bool canExplore = shown && !paused && glowItem.Checked && motion.CurrentWeb == null &&
                idle >= motion.WanderThreshold && idle < motion.WeaveThreshold && motion.State == "WANDER";
            IntPtr root = HoverProbe.FindExternalRootAtPoint(point, ownProcessId);
            if (canExplore && !threat) hoverProbe.Request(point, root);
            else hoverProbe.CancelPending();
            HoverTarget target = hoverProbe.Latest;
            if (target != null && (!target.IsFresh(point, root, 300) || !hoverProbe.IsApprovedWindow(root, target.ProcessId))) target = null;
            curiosity.Update(elapsed, now, point, target == null ? null : new GhostCandidate(target.Bounds, target.Kind), canExplore, threat);
            return target;
        }

        private void RenderCuriosity(HoverTarget target)
        {
            if (!curiosity.WantsGlow || !motion.CuriosityHolding || target == null ||
                !target.IsFresh(motion.Position, HoverProbe.FindExternalRootAtPoint(motion.Position, ownProcessId), 300) ||
                !hoverProbe.IsApprovedWindow(target.RootWindow, target.ProcessId))
            {
                hoverGlow.Clear();
                return;
            }
            hoverGlow.UpdateTarget(curiosity.GlowBounds);
            if (hoverGlow.Visible) curiosity.ConfirmRendered();
        }

        private void ApproveForegroundWindow()
        {
            IntPtr root = Native.GetForegroundWindow();
            bool approved = hoverProbe != null && hoverProbe.ApproveWindow(root, HoverProbe.WindowProcessId(root));
            ClearHoverGlow("CONSENT_REVOKED");
            tray.ShowBalloonTip(4000, "Ghost Pointer",
                approved ? "Only this selected window is approved for passive Button/MenuItem geometry observation. No input is dispatched." :
                "That window cannot be approved. Select a non-sensitive ordinary app window and try again.", ToolTipIcon.Info);
        }

        private static double ReadIdleSeconds()
        {
            Native.LASTINPUTINFO input = new Native.LASTINPUTINFO();
            input.Size = (uint)Marshal.SizeOf(typeof(Native.LASTINPUTINFO));
            if (!Native.GetLastInputInfo(ref input)) return 0;
            return unchecked((uint)Environment.TickCount - input.Time) / 1000.0;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0084) { m.Result = new IntPtr(-1); return; } // HTTRANSPARENT
            if (m.Msg == 0x0021) { m.Result = new IntPtr(3); return; } // MA_NOACTIVATE
            if (m.Msg == Native.QuitMessage) { Close(); return; }
            if (m.Msg == Native.LedgerMessage)
            {
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "session-ledger.json"),
                    new JavaScriptSerializer().Serialize(motion.Ledger));
                return;
            }
            if (m.Msg == Native.StatusMessage)
            {
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "running-status.txt"),
                    "Version: Halloweb 0.3.0 (LIVE-SPIDER-12)\r\nRunning: " + Native.IsWindowVisible(Handle) + "\r\nPaused: " + paused +
                    "\r\nState: " + motion.State + "\r\nMotion status: " + motion.MotionStatus +
                    "\r\nDistance moved this tick: " + motion.DistanceMoved.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
                    "\r\nRoute status: " + motion.RouteStatus + "\r\nRejected routes remembered: " + motion.RejectedRouteCount +
                    "\r\nWeb progress: " + (motion.CurrentWeb == null ? "none" : motion.CurrentWeb.Progress.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)) +
                    "\r\nReceipts: " + string.Join(", ", motion.Receipts.ToArray()) +
                    "\r\nRoute ID: " + (motion.CurrentRouteId ?? "none") +
                    "\r\nDecision ID: " + (motion.CurrentDecisionId ?? "none") +
                    "\r\nWeaving authorized: " + motion.WeaveAuthorized +
                    "\r\nSession ledger entries: " + motion.Ledger.Count +
                    "\r\nHover glow enabled: " + glowItem.Checked +
                    "\r\nHover glow visible: " + (hoverGlow != null && hoverGlow.Visible) +
                    "\r\nCuriosity state: " + curiosity.State + "\r\nCuriosity holding wander: " + motion.CuriosityHolding +
                    "\r\nGhost receipt count (RAM only): " + curiosity.Ledger.Count +
                    "\r\nObservation outcome: " + (hoverProbe == null ? "DISABLED" : hoverProbe.LatestOutcome) +
                    "\r\nUIA point lookups this session: " + (hoverProbe == null ? 0 : hoverProbe.UiaLookupCount) +
                    "\r\nApproval shortcut Ctrl+Alt+G: " + approvalHotkey +
                    "\r\nEngine tick: " + motion.Tick + "\r\nPointer polling available: " + PointerPollingAvailable() +
                    "\r\nPause shortcut Ctrl+Alt+S: " + pauseHotkey +
                    "\r\nQuit shortcut Ctrl+Alt+Q: " + quitHotkey + "\r\n");
                return;
            }
            if (m.Msg == 0x0312)
            {
                if (m.WParam.ToInt32() == 1) TogglePause();
                if (m.WParam.ToInt32() == 2) Close();
                if (m.WParam.ToInt32() == 3) ApproveForegroundWindow();
                return;
            }
            if (m.Msg == 0x007E && motion != null) { RefreshBounds(); RenderSpider(); }
            base.WndProc(ref m);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            timer.Stop();
            timer.Dispose();
            Native.UnregisterHotKey(Handle, 1);
            Native.UnregisterHotKey(Handle, 2);
            Native.UnregisterHotKey(Handle, 3);
            tray.Visible = false;
            if (tray.ContextMenuStrip != null) tray.ContextMenuStrip.Dispose();
            tray.Dispose();
            if (hoverProbe != null) hoverProbe.Dispose();
            if (hoverGlow != null) { hoverGlow.Close(); hoverGlow.Dispose(); }
            if (webOverlay != null) { webOverlay.Close(); webOverlay.Dispose(); }
            if (trayIcon != null) trayIcon.Dispose();
            base.OnFormClosed(e);
        }

        internal static bool PointerPollingAvailable()
        {
            Native.POINT cursor;
            return Native.GetCursorPos(out cursor);
        }
    }

    internal static class SpiderArt
    {
        private static void Leg(Graphics g, float x1, float y1, float cx, float cy, float x2, float y2, float width)
        {
            using (GraphicsPath p = new GraphicsPath())
            {
                p.AddBezier(x1, y1, x1 + (cx - x1) * 2 / 3, y1 + (cy - y1) * 2 / 3,
                    x2 + (cx - x2) * 2 / 3, y2 + (cy - y2) * 2 / 3, x2, y2);
                using (Pen edge = new Pen(Color.FromArgb(195, 100, 82, 73), width + 1.2f))
                using (Pen core = new Pen(Color.FromArgb(20, 18, 18), width))
                {
                    edge.StartCap = edge.EndCap = core.StartCap = core.EndCap = LineCap.Round;
                    g.DrawPath(edge, p);
                    g.DrawPath(core, p);
                }
            }
        }

        public static Bitmap Draw(int size, double angle, bool moving, double time, double scale)
        {
            Bitmap bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                g.TranslateTransform(size / 2f, size / 2f);
                g.RotateTransform((float)angle);
                float pulse = moving ? (float)Math.Sin(time * 66) : 0;
                g.ScaleTransform((float)(0.8 * scale) * (1 - pulse * 0.055f),
                    (float)(0.8 * scale) * (1 + pulse * 0.055f));
                g.TranslateTransform(-50, -55);
                float step = pulse * 3.8f;
                Leg(g, 45, 40, 10, 10, 5, 35 + step, 4.5f);
                Leg(g, 45, 45, 5, 40, 5, 55 - step, 4.5f);
                Leg(g, 45, 50, 5, 70, 15, 85 + step, 4.5f);
                Leg(g, 48, 55, 30, 95, 25, 105 - step, 4.5f);
                Leg(g, 55, 40, 90, 10, 95, 35 - step, 4.5f);
                Leg(g, 55, 45, 95, 40, 95, 55 + step, 4.5f);
                Leg(g, 55, 50, 95, 70, 85, 85 - step, 4.5f);
                Leg(g, 52, 55, 70, 95, 75, 105 + step, 4.5f);
                Leg(g, 45, 32, 40, 20, 38, 18, 3);
                Leg(g, 55, 32, 60, 20, 62, 18, 3);
                using (SolidBrush shadow = new SolidBrush(Color.FromArgb(55, 0, 0, 0)))
                    g.FillEllipse(shadow, 33, 45, 34, 48);
                using (LinearGradientBrush body = new LinearGradientBrush(new Point(36, 43), new Point(64, 87),
                    Color.FromArgb(54, 47, 45), Color.FromArgb(5, 5, 5)))
                using (Pen edge = new Pen(Color.FromArgb(98, 76, 68), 1.3f))
                {
                    g.FillEllipse(body, 36, 43, 28, 44);
                    g.DrawEllipse(edge, 36, 43, 28, 44);
                }
                using (SolidBrush red = new SolidBrush(Color.FromArgb(255, 30, 30)))
                    g.FillPolygon(red, new PointF[] { new PointF(46, 55), new PointF(54, 55),
                        new PointF(48, 62), new PointF(53, 67), new PointF(47, 67), new PointF(52, 62) });
                using (LinearGradientBrush head = new LinearGradientBrush(new Point(40, 30), new Point(60, 50),
                    Color.FromArgb(49, 41, 39), Color.FromArgb(13, 12, 12)))
                using (Pen edge = new Pen(Color.FromArgb(94, 74, 65), 1))
                {
                    g.FillEllipse(head, 40, 30, 20, 20);
                    g.DrawEllipse(edge, 40, 30, 20, 20);
                }
                using (SolidBrush red = new SolidBrush(Color.FromArgb(255, 35, 35)))
                {
                    g.FillEllipse(red, 44.5f, 34.5f, 3, 3);
                    g.FillEllipse(red, 52.5f, 34.5f, 3, 3);
                    g.FillEllipse(red, 41, 37, 2, 2);
                    g.FillEllipse(red, 57, 37, 2, 2);
                    g.FillEllipse(red, 47.2f, 33.2f, 1.6f, 1.6f);
                    g.FillEllipse(red, 51.2f, 33.2f, 1.6f, 1.6f);
                }
            }
            return bitmap;
        }
    }

    internal static class Checks
    {
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Check failed: " + message);
        }

        public static void Run()
        {
            int scenarios = 0;
            RectangleF[] monitors = { new RectangleF(64, 64, 1792, 912),
                new RectangleF(-1856, 64, 1792, 912), new RectangleF(264, -1036, 1152, 912) };
            foreach (RectangleF bounds in monitors)
            {
                PointF[] positions = { new PointF(bounds.Left, bounds.Top), new PointF(bounds.Right, bounds.Top),
                    new PointF(bounds.Left, bounds.Bottom), new PointF(bounds.Right, bounds.Bottom),
                    new PointF(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2) };
                foreach (PointF position in positions)
                for (int direction = 0; direction < 9; direction++)
                {
                    EscapeMotion m = new EscapeMotion(position, bounds, scenarios + 1);
                    double angle = direction * Math.PI / 4;
                    PointF threat = direction == 8 ? position : new PointF(position.X + (float)Math.Cos(angle) * 65,
                        position.Y + (float)Math.Sin(angle) * 65);
                    float before = EscapeMotion.Distance(position, threat);
                    m.Think(threat, 1, 1);
                    Require(EscapeMotion.Distance(position, m.Target) > 20, "wall/corner escape");
                    Require(EscapeMotion.Distance(m.Target, threat) > before, "moves away from pointer");
                    for (int frame = 0; frame < 120; frame++)
                    {
                        m.Step(1.0 / 60, 1);
                        Require(!float.IsNaN(m.Position.X) && !float.IsNaN(m.Position.Y), "finite coordinates");
                        Require(m.Position.X >= bounds.Left && m.Position.X <= bounds.Right &&
                            m.Position.Y >= bounds.Top && m.Position.Y <= bounds.Bottom, "within real monitor");
                    }
                    Require(!m.Moving, "arrives at destination");
                    m.SetBounds(new RectangleF(64, 64, 800, 600));
                    Require(m.Position.X >= 64 && m.Position.X <= 864, "monitor removal clamp");
                    scenarios++;
                }
            }
            foreach (double artScale in new double[] { 0.75, 1, 1.35 })
            for (int angle = 0; angle < 360; angle += 5)
            for (int phase = 0; phase < 2; phase++)
            using (Bitmap bitmap = SpiderArt.Draw(160, angle, true, (Math.PI / 2 + phase * Math.PI) / 66, artScale))
            {
                Require(bitmap.GetPixel(0, 0).A == 0, "transparent background");
                for (int i = 0; i < bitmap.Width; i++)
                    Require(bitmap.GetPixel(i, 0).A == 0 && bitmap.GetPixel(i, bitmap.Height - 1).A == 0 &&
                        bitmap.GetPixel(0, i).A == 0 && bitmap.GetPixel(bitmap.Width - 1, i).A == 0, "unclipped art");
                if (artScale == 1 && angle == 0 && phase == 0)
                    bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "spider-preview.png"), ImageFormat.Png);
            }
            using (Bitmap emptyWeb = WebArt.Draw(192, 0, 1))
            using (Bitmap growingWeb = WebArt.Draw(192, 4, 1))
            using (Bitmap finishedWeb = WebArt.Draw(192, 24, 1))
            {
                int emptyPixels = 0, growingPixels = 0, finishedPixels = 0;
                for (int x = 0; x < 192; x++)
                for (int y = 0; y < 192; y++)
                {
                    if (emptyWeb.GetPixel(x, y).A > 0) emptyPixels++;
                    if (growingWeb.GetPixel(x, y).A > 0) growingPixels++;
                    if (finishedWeb.GetPixel(x, y).A > 0) finishedPixels++;
                }
                Require(emptyPixels == 0 && growingPixels > 0 && finishedPixels > growingPixels, "silk grows from actual path lengths");
                Require(finishedWeb.GetPixel(0, 0).A == 0, "silk background is transparent");
                finishedWeb.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "web-preview.png"), ImageFormat.Png);
                using (Bitmap scene = new Bitmap(460, 300))
                using (Graphics graphics = Graphics.FromImage(scene))
                using (Bitmap spider = SpiderArt.Draw(160, 0, false, 0, 1))
                {
                    graphics.Clear(Color.FromArgb(24, 31, 43));
                    graphics.DrawImage(finishedWeb, 134, 54);
                    graphics.DrawImage(spider, 150, 70);
                    scene.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ecology-preview.png"), ImageFormat.Png);
                }
            }
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "self-test-results.txt"),
                "PASS: " + scenarios + " escape scenarios; corners, exact overlap, negative screen coordinates, " +
                "arrival, monitor removal, transparent background, and 432 artwork rotation/size/animation checks.\r\n" +
                EcologyChecks.Run() + "\r\n" + EcologyV11Checks.Run() + "\r\n" +
                "PASS: " + HoverGlowChecks.Run() + " hover glow artwork/geometry assertions; " +
                HoverIntegrationChecks.PureChecks() + " hover target validation/freshness assertions.\r\n" +
                "PASS: " + GhostCuriosityChecks.Run() + " pure curiosity assertions; " + GhostMovementChecks.Run() + " movement priority assertions.\r\n");
            // A deterministic audit example: travel, measured arrival, deferred
            // permission, authorized construction, then revocation by a threat.
            EcologyMotion example = new EcologyMotion(new PointF(140, 100), new RectangleF(20, 20, 1880, 1040), 11);
            example.CurrentWeb = new EcologyWeb { Anchor = new PointF(100, 100), Progress = 10, Interrupted = true };
            example.Update(new PointF(-1000, -1000), 0.1, 40, 1, 0);
            example.Update(new PointF(-1000, -1000), 0.1, 40, 1, 0.1);
            example.Update(new PointF(-1000, -1000), 0.1, 40, 1, 0.2);
            example.Update(new PointF(-1000, -1000), 0.1, 300, 1, 1);
            example.Update(new PointF(100, 100), 0.1, 300, 1, 1.1);
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "native-receipt-example.json"),
                new JavaScriptSerializer().Serialize(example.Ledger));
        }

        public static void CheckWindow(SpiderForm form, EcologyMotion motion, WebOverlay silk)
        {
            int style = Native.GetWindowLong(form.Handle, -20);
            int expected = Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW;
            Require((style & expected) == expected, "click-through/no-activation/layered/tool window styles");
            Require(Native.IsWindowVisible(form.Handle), "overlay is visible");
            Require(Native.GetForegroundWindow() != form.Handle, "does not take keyboard focus");
            Native.RECT rect;
            Native.GetWindowRect(form.Handle, out rect);
            Require(Math.Abs((rect.Left + rect.Right) / 2.0 - motion.Position.X) < 2, "screen pixel X alignment");
            Require(Math.Abs((rect.Top + rect.Bottom) / 2.0 - motion.Position.Y) < 2, "screen pixel Y alignment");
            Require(silk != null && silk.Visible && motion.CurrentWeb != null, "growing web overlay is visible");
            Require((Native.GetWindowLong(silk.Handle, -20) & expected) == expected, "silk is layered/click-through/nonactivating");
            Require(Native.GetForegroundWindow() != silk.Handle, "silk does not take focus");
            Native.GetWindowRect(silk.Handle, out rect);
            Require(Math.Abs((rect.Left + rect.Right) / 2.0 - motion.CurrentWeb.Anchor.X) < 2 &&
                Math.Abs((rect.Top + rect.Bottom) / 2.0 - motion.CurrentWeb.Anchor.Y) < 2, "silk stays at its own anchor");
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "smoke-test-results.txt"),
                "PASS: native spider and silk overlays launched; layered, click-through, tool window and no-activation styles verified; " +
                "keyboard focus retained; physical screen placement, web anchor, and pause/resume verified.\r\n" +
                "Pointer polling available during smoke test: " + SpiderForm.PointerPollingAvailable() + "\r\n");
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "smoke-ledger.json"),
                new JavaScriptSerializer().Serialize(motion.Ledger));
        }
    }

    internal static class Native
    {
        public const int WS_EX_LAYERED = 0x80000, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x8000000;
        public const int QuitMessage = 0x8000 + 73, StatusMessage = 0x8000 + 74, LedgerMessage = 0x8000 + 75;
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }
        [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int Width, Height; public SIZE(int w, int h) { Width = w; Height = h; } }
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] public struct LASTINPUTINFO { public uint Size, Time; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] public struct BLENDFUNCTION
        { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
        [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT point);
        [DllImport("user32.dll")] public static extern bool GetLastInputInfo(ref LASTINPUTINFO input);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, System.Text.StringBuilder name, int count);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int show);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int index);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT rect);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr order, int x, int y, int w, int height, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateLayeredWindow(IntPtr h, IntPtr destDC,
            ref POINT destination, ref SIZE size, IntPtr sourceDC, ref POINT source, int color, ref BLENDFUNCTION blend, int flags);

        public static void SetBitmap(IntPtr handle, Bitmap bitmap, int x, int y)
        {
            IntPtr screenDC = GetDC(IntPtr.Zero), memoryDC = CreateCompatibleDC(screenDC);
            IntPtr dib = IntPtr.Zero, previous = IntPtr.Zero;
            try
            {
                dib = bitmap.GetHbitmap(Color.FromArgb(0));
                previous = SelectObject(memoryDC, dib);
                POINT destination = new POINT(x, y), source = new POINT(0, 0);
                SIZE size = new SIZE(bitmap.Width, bitmap.Height);
                BLENDFUNCTION blend = new BLENDFUNCTION { SourceConstantAlpha = 255, AlphaFormat = 1 };
                if (!UpdateLayeredWindow(handle, screenDC, ref destination, ref size, memoryDC, ref source, 0, ref blend, 2))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }
            finally
            {
                if (previous != IntPtr.Zero) SelectObject(memoryDC, previous);
                if (dib != IntPtr.Zero) DeleteObject(dib);
                DeleteDC(memoryDC);
                ReleaseDC(IntPtr.Zero, screenDC);
            }
        }
    }
}
