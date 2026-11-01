using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using System.Web.Script.Serialization;

namespace HallowebNativeHoverLab
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            try
            {
                try { LabNative.SetProcessDpiAwarenessContext(new IntPtr(-4)); }
                catch (EntryPointNotFoundException) { LabNative.SetProcessDPIAware(); }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
                using (HoverLab lab = new HoverLab(Array.IndexOf(args, "--verify") >= 0)) Application.Run(lab);
            }
            catch (Exception error)
            {
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "native-hover-lab-error.txt"), error.ToString());
                Environment.ExitCode = 1;
            }
        }
    }

    internal sealed class ObservedButton : Button
    {
        public int Moves, Leaves, Hovers, Downs, Ups, Clicks;
        public ObservedButton()
        {
            FlatStyle = FlatStyle.System; TabStop = false;
            Click += delegate { Clicks++; };
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == LabNative.WM_MOUSEMOVE) Moves++;
            if (m.Msg == LabNative.WM_MOUSELEAVE) Leaves++;
            if (m.Msg == 0x02A1) Hovers++;
            if (m.Msg == 0x0201) Downs++;
            if (m.Msg == 0x0202) Ups++;
            // Observe the unmodified native control. Do not suppress leave or
            // force a paint state to manufacture sustained pointer hover.
            base.WndProc(ref m);
        }
    }

    internal sealed class HoverLab : Form
    {
        private readonly ObservedButton button = new ObservedButton { Text = "Standard Windows button", Bounds = new Rectangle(54, 135, 300, 52) };
        private readonly ToolBar toolbar = new ToolBar { Bounds = new Rectangle(54, 260, 300, 58), Dock = DockStyle.None,
            AutoSize = false, Appearance = ToolBarAppearance.Flat, ShowToolTips = false, Wrappable = false,
            ButtonSize = new Size(260, 48), TextAlign = ToolBarTextAlign.Right, TabStop = false };
        private readonly Label status = new Label { Bounds = new Rectangle(390, 115, 330, 260), AutoSize = false };
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 25 };
        private readonly Stopwatch clock = new Stopwatch();
        private readonly LabSpider spider = new LabSpider();
        private readonly bool verify;
        private readonly Dictionary<string, object> results = new Dictionary<string, object>();
        private LabNative.POINT initialCursor;
        private IntPtr initialForeground;
        private int phase, toolbarClicks, sends;
        private bool pointerUnchanged = true, foregroundUnchanged = true;
        private bool captured25, captured100, captured300;
        private double enteredAt;

        public HoverLab(bool verifyMode)
        {
            verify = verifyMode;
            Text = "Halloweb Native Hover Lab - isolated test";
            ClientSize = new Size(750, 440); FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; AutoScaleMode = AutoScaleMode.None; StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(246, 247, 249);
            Font = new Font("Segoe UI", 10);
            Controls.Add(new Label { Text = "Can the spider produce native hover without moving your pointer?", Bounds = new Rectangle(28, 22, 700, 40), Font = new Font("Segoe UI", 13, FontStyle.Bold) });
            Controls.Add(new Label { Text = "Only this lab's two controls can receive messages. Your desktop spider is unchanged.", Bounds = new Rectangle(28, 65, 700, 35) });
            Controls.Add(new Label { Text = "1. Standard button: one mouse-move message", Bounds = new Rectangle(28, 105, 340, 26) });
            Controls.Add(new Label { Text = "2. Native toolbar: explicit hot-item state", Bounds = new Rectangle(28, 225, 340, 26) });
            Controls.Add(button); toolbar.Buttons.Add(new ToolBarButton("Native toolbar action")); Controls.Add(toolbar); Controls.Add(status);
            toolbar.ButtonClick += delegate { toolbarClicks++; };
            Button replay = new Button { Text = "Replay experiment", Bounds = new Rectangle(28, 382, 170, 34), TabStop = false };
            replay.Click += delegate { BeginExperiment(); }; Controls.Add(replay);
            Controls.Add(new Label { Text = "Keep the real pointer outside the test controls during verification.", Bounds = new Rectangle(220, 390, 500, 28) });
            timer.Tick += delegate { Step(); };
            LabNative.POINT cursor;
            if (!LabNative.GetCursorPos(out cursor)) throw new InvalidOperationException("The lab needs normal desktop pointer polling.");
            Rectangle area = Screen.FromPoint(new Point(cursor.X, cursor.Y)).WorkingArea;
            Location = new Point(cursor.X < area.Left + area.Width / 2 ? area.Right - Width - 24 : area.Left + 24,
                cursor.Y < area.Top + area.Height / 2 ? area.Bottom - Height - 24 : area.Top + 24);
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { CreateParams cp = base.CreateParams; cp.ExStyle |= 0x08000000; return cp; }
        }
        protected override void OnShown(EventArgs e) { base.OnShown(e); BeginExperiment(); }
        private void BeginExperiment()
        {
            timer.Stop();
            ClearTargets();
            results.Clear(); phase = 0; sends = 0;
            button.Moves = button.Leaves = button.Hovers = button.Downs = button.Ups = button.Clicks = toolbarClicks = 0;
            captured25 = captured100 = captured300 = false;
            pointerUnchanged = foregroundUnchanged = true;
            if (!LabNative.GetCursorPos(out initialCursor)) throw new InvalidOperationException("Pointer polling unavailable.");
            initialForeground = LabNative.GetForegroundWindow();
            string buttonClass = LabNative.ClassName(button.Handle), toolbarClass = LabNative.ClassName(toolbar.Handle);
            if (buttonClass.IndexOf("BUTTON", StringComparison.OrdinalIgnoreCase) < 0 ||
                toolbarClass.IndexOf("ToolbarWindow32", StringComparison.OrdinalIgnoreCase) < 0)
                throw new InvalidOperationException("Expected real Windows BUTTON and ToolbarWindow32 controls.");
            results["buttonClass"] = buttonClass; results["toolbarClass"] = toolbarClass;
            results["buttonInitiallyHot"] = ButtonHot();
            clock.Restart(); timer.Start();
        }
        private IntPtr OwnMessage(Control target, int message, int value, int coordinates)
        {
            bool permitted = ReferenceEquals(target, button) && (message == LabNative.WM_MOUSEMOVE || message == LabNative.WM_MOUSELEAVE || message == LabNative.BM_GETSTATE) ||
                ReferenceEquals(target, toolbar) && (message == LabNative.TB_SETHOTITEM || message == LabNative.TB_GETHOTITEM);
            uint pid;
            if (!permitted || !target.IsHandleCreated || target.IsDisposed || !LabNative.IsChild(Handle, target.Handle) ||
                LabNative.GetWindowThreadProcessId(target.Handle, out pid) == 0 || pid != (uint)Process.GetCurrentProcess().Id)
                throw new InvalidOperationException("Refused a message outside the exact owned test controls.");
            if (message == LabNative.WM_MOUSEMOVE && value != 0 ||
                message == LabNative.TB_SETHOTITEM && value != 0 && value != -1)
                throw new InvalidOperationException("Refused unexpected input flags or toolbar index.");
            sends++;
            return LabNative.SendMessage(target.Handle, message, new IntPtr(value), new IntPtr(coordinates));
        }
        private bool ButtonHot() { return (OwnMessage(button, LabNative.BM_GETSTATE, 0, 0).ToInt64() & 0x0200) != 0; }
        private int ToolbarHot() { return OwnMessage(toolbar, LabNative.TB_GETHOTITEM, 0, 0).ToInt32(); }
        private void ClearTargets()
        {
            if (button.IsHandleCreated && !button.IsDisposed) OwnMessage(button, LabNative.WM_MOUSELEAVE, 0, 0);
            if (toolbar.IsHandleCreated && !toolbar.IsDisposed) OwnMessage(toolbar, LabNative.TB_SETHOTITEM, -1, 0);
        }
        private Point Center(Control control) { Rectangle r = control.RectangleToScreen(control.ClientRectangle); return new Point(r.X + r.Width / 2, r.Y + r.Height / 2); }
        private void Step()
        {
            try
            {
                LabNative.POINT cursor;
                pointerUnchanged &= LabNative.GetCursorPos(out cursor) && cursor.X == initialCursor.X && cursor.Y == initialCursor.Y;
                foregroundUnchanged &= LabNative.GetForegroundWindow() == initialForeground;
                double time = clock.Elapsed.TotalSeconds;
                Point first = Center(button), second = Center(toolbar);
                if (time < 1) spider.MoveTo(new Point(first.X - 150 + (int)(150 * time), first.Y));
                if (phase == 0 && time >= 1)
                {
                    spider.MoveTo(first);
                    int x = button.ClientSize.Width / 2, y = button.ClientSize.Height / 2;
                    OwnMessage(button, LabNative.WM_MOUSEMOVE, 0, (y << 16) | (x & 0xFFFF));
                    results["buttonHotImmediately"] = ButtonHot(); enteredAt = time; phase = 1;
                }
                if (phase == 1)
                {
                    double age = time - enteredAt;
                    if (!captured25 && age >= 0.025) { results["buttonHotAfter25ms"] = ButtonHot(); captured25 = true; }
                    if (!captured100 && age >= 0.100) { results["buttonHotAfter100ms"] = ButtonHot(); captured100 = true; }
                    if (!captured300 && age >= 0.300) { results["buttonHotAfter300ms"] = ButtonHot(); captured300 = true; }
                    if (age > 1.4)
                    {
                        OwnMessage(button, LabNative.WM_MOUSELEAVE, 0, 0);
                        results["buttonHotAfterClear"] = ButtonHot(); results["buttonLeaveMessages"] = button.Leaves; phase = 2;
                    }
                }
                if (phase == 2)
                {
                    double fraction = Math.Max(0, Math.Min(1, (time - 2.4) / 0.8));
                    spider.MoveTo(new Point(first.X, first.Y + (int)((second.Y - first.Y) * fraction)));
                    if (fraction >= 1)
                    {
                        OwnMessage(toolbar, LabNative.TB_SETHOTITEM, 0, 0);
                        results["toolbarHotImmediately"] = ToolbarHot(); enteredAt = time; phase = 3;
                    }
                }
                if (phase == 3 && time - enteredAt >= 0.5)
                {
                    results["toolbarHotAfter500ms"] = ToolbarHot(); phase = 4;
                }
                if (phase == 4 && time - enteredAt >= 1.8)
                {
                    OwnMessage(toolbar, LabNative.TB_SETHOTITEM, -1, 0);
                    results["toolbarHotAfterClear"] = ToolbarHot(); phase = 5;
                    spider.MoveTo(new Point(second.X - 150, second.Y));
                    Finish();
                }
                status.Text = phase == 5 ? "Observed on this Windows desktop\r\n\r\n" +
                    "Standard button hot:\r\n" + results["buttonHotImmediately"] + " immediately, " + results["buttonHotAfter300ms"] + " after 300 ms\r\n\r\n" +
                    "Toolbar held its native highlight.\r\n\r\n" +
                    "Clicks / mouse down / mouse up: " + (button.Clicks + toolbarClicks) + " / " + button.Downs + " / " + button.Ups +
                    "\r\nPointer unchanged: " + pointerUnchanged + "\r\nForeground unchanged: " + foregroundUnchanged +
                    "\r\n\r\nToolbar preview held until replay/close." : "Native hover experiment\r\n\r\n" +
                    "Button hot now: " + ButtonHot() + "\r\nToolbar hot item: " + ToolbarHot() +
                    "\r\n\r\nButton clicks: " + button.Clicks + "\r\nToolbar clicks: " + toolbarClicks +
                    "\r\nMouse down/up: " + button.Downs + "/" + button.Ups +
                    "\r\n\r\nReal pointer unchanged: " + pointerUnchanged + "\r\nForeground unchanged: " + foregroundUnchanged;
                if (phase == 5 && verify) Close();
            }
            catch (Exception error)
            {
                timer.Stop(); File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "native-hover-lab-error.txt"), error.ToString());
                Environment.ExitCode = 1; Close();
            }
        }
        private void Finish()
        {
            timer.Stop();
            results["buttonClicks"] = button.Clicks; results["toolbarClicks"] = toolbarClicks;
            results["mouseDowns"] = button.Downs; results["mouseUps"] = button.Ups;
            results["buttonMouseMoves"] = button.Moves; results["buttonMouseHovers"] = button.Hovers;
            results["pointerUnchanged"] = pointerUnchanged; results["foregroundUnchanged"] = foregroundUnchanged;
            results["ownedMessagesIncludingQueries"] = sends;
            bool pass = pointerUnchanged && foregroundUnchanged && button.Clicks == 0 && toolbarClicks == 0 && button.Downs == 0 && button.Ups == 0 &&
                !(bool)results["buttonHotAfterClear"] && (int)results["toolbarHotImmediately"] == 0 &&
                (int)results["toolbarHotAfter500ms"] == 0 && (int)results["toolbarHotAfterClear"] == -1;
            results["isolationChecksPassed"] = pass;
            string folder = AppDomain.CurrentDomain.BaseDirectory;
            File.WriteAllText(Path.Combine(folder, "native-hover-lab-results.json"), new JavaScriptSerializer().Serialize(results));
            File.WriteAllText(Path.Combine(folder, "native-hover-lab-results.txt"), (pass ? "PASS: " : "FAIL: ") +
                "Owned-controls-only native hover experiment. Button hot immediately/after300ms: " + results["buttonHotImmediately"] + "/" + results["buttonHotAfter300ms"] +
                ". Toolbar hot item immediately/after500ms/cleared: " + results["toolbarHotImmediately"] + "/" + results["toolbarHotAfter500ms"] + "/" + results["toolbarHotAfterClear"] +
                ". Clicks and mouse-down/up: " + button.Clicks + "/" + toolbarClicks + "/" + button.Downs + "/" + button.Ups +
                ". Pointer unchanged: " + pointerUnchanged + "; foreground unchanged: " + foregroundUnchanged + ".\r\n");
            if (!pass && verify) Environment.ExitCode = 1;
            if (!verify)
            {
                // After recording the successful clear, hold a separate owned
                // toolbar preview for the human to inspect. This is not a claim
                // that the standard button sustained native pointer hover.
                OwnMessage(toolbar, LabNative.TB_SETHOTITEM, 0, 0);
                spider.MoveTo(Center(toolbar));
            }
        }
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            timer.Stop(); timer.Dispose(); ClearTargets(); spider.Dispose(); base.OnFormClosed(e);
        }
    }

    internal static class LabNative
    {
        public const int WM_MOUSEMOVE = 0x0200, WM_MOUSELEAVE = 0x02A3, BM_GETSTATE = 0x00F2,
            TB_GETHOTITEM = 0x0400 + 71, TB_SETHOTITEM = 0x0400 + 72;
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT point);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
        [DllImport("user32.dll")] public static extern bool IsChild(IntPtr parent, IntPtr child);
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int count);
        public static string ClassName(IntPtr window) { StringBuilder name = new StringBuilder(256); GetClassName(window, name, name.Capacity); return name.ToString(); }
    }
}
