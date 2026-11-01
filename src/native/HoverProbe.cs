using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;

namespace SpookyDesktopSpider
{
    internal enum HoverControlKind { Button, MenuItem }

    // No live automation object, text, title, name, or provider is published.
    internal sealed class HoverTarget
    {
        public readonly RectangleF Bounds;
        public readonly PointF ProbePoint;
        public readonly IntPtr RootWindow;
        public readonly int RootProcessId;
        public readonly HoverControlKind Kind;
        public readonly long CapturedAtTimestamp, RequestVersion, ApprovalGeneration;
        public int ProcessId { get { return RootProcessId; } }
        public long ObservedAtTimestamp { get { return CapturedAtTimestamp; } }

        internal HoverTarget(RectangleF bounds, PointF point, IntPtr root, int pid, HoverControlKind kind, long timestamp, long version)
            : this(bounds, point, root, pid, kind, timestamp, version, 0) { }

        internal HoverTarget(RectangleF bounds, PointF point, IntPtr root, int pid, HoverControlKind kind,
            long timestamp, long version, long approvalGeneration)
        {
            Bounds = bounds; ProbePoint = point; RootWindow = root; RootProcessId = pid;
            Kind = kind; CapturedAtTimestamp = timestamp; RequestVersion = version; ApprovalGeneration = approvalGeneration;
        }

        public double AgeMilliseconds { get { return HoverProbe.ElapsedMilliseconds(CapturedAtTimestamp, Stopwatch.GetTimestamp()); } }
        public bool IsFresh(PointF point, IntPtr root) { return IsFresh(point, root, HoverProbe.MaximumAgeMilliseconds); }
        public bool IsFresh(PointF point, IntPtr root, double maximumAgeMilliseconds)
        {
            return root != IntPtr.Zero && root == RootWindow && HoverProbe.Contains(Bounds, point) &&
                HoverProbe.IsFreshTimestamp(CapturedAtTimestamp, Stopwatch.GetTimestamp(), maximumAgeMilliseconds);
        }
    }

    // LIVE-SPIDER-12: one explicitly approved window, one direct hit, and no
    // traversal or fallback. Unsupported/obscured hits decline conservatively.
    internal sealed class HoverProbe : IDisposable
    {
        public const double MaximumAgeMilliseconds = 300;
        public const double MinimumProbeIntervalMilliseconds = 150;
        public const double MaximumTargetWidth = 600, MaximumTargetHeight = 200, MaximumTargetArea = 150000;
        private const double MaximumCoordinate = 1000000;
        private readonly int ownProcessId;
        private readonly object requestLock = new object();
        private readonly AutoResetEvent requestReady = new AutoResetEvent(false);
        private readonly Thread worker;
        private volatile bool disposed, busy;
        private bool pending;
        private RequestData latestRequest;
        private Approval approval;
        private long requestVersion, approvalGeneration, completedProbes, uiaLookups;
        private HoverTarget latest;
        private string latestOutcome = "DECLINED_UNAPPROVED_WINDOW";

        private sealed class Approval
        {
            public readonly IntPtr Root;
            public readonly int ProcessId;
            public readonly long Generation;
            public Approval(IntPtr root, int pid, long generation) { Root = root; ProcessId = pid; Generation = generation; }
        }

        private struct RequestData
        {
            public PointF Point;
            public IntPtr ExpectedRoot;
            public long Version, Timestamp, ApprovalGeneration;
        }

        private struct ElementData
        {
            public int ProcessId, TypeId;
            public bool Enabled, Offscreen, Password;
            public System.Windows.Rect Rectangle;
        }

        private sealed class ProbeResult
        {
            public readonly HoverTarget Target;
            public readonly string Outcome;
            public ProbeResult(HoverTarget target, string outcome) { Target = target; Outcome = outcome; }
        }

        public HoverProbe(int ownPid)
        {
            if (ownPid <= 0) throw new ArgumentOutOfRangeException("ownPid");
            ownProcessId = ownPid;
            worker = new Thread(WorkerLoop);
            worker.Name = "Halloweb approved-window geometry";
            worker.IsBackground = true;
            worker.SetApartmentState(ApartmentState.MTA);
            worker.Start();
        }

        public bool IsBusy { get { return busy; } }
        public long CompletedProbes { get { return Interlocked.Read(ref completedProbes); } }
        public long UiaLookupCount { get { return Interlocked.Read(ref uiaLookups); } }
        public string LatestOutcome
        {
            get
            {
                if (disposed) return "DECLINED_STOPPED";
                HoverTarget target = Interlocked.CompareExchange(ref latest, null, null);
                if (target != null && !IsFreshTimestamp(target.CapturedAtTimestamp, Stopwatch.GetTimestamp(), MaximumAgeMilliseconds))
                    return "DECLINED_STALE";
                return Interlocked.CompareExchange(ref latestOutcome, null, null);
            }
        }

        public HoverTarget Latest
        {
            get
            {
                if (disposed) return null;
                HoverTarget target = Interlocked.CompareExchange(ref latest, null, null);
                Approval allowed;
                lock (requestLock) { allowed = approval; }
                return target != null && allowed != null && target.ApprovalGeneration == allowed.Generation &&
                    target.RootWindow == allowed.Root && target.ProcessId == allowed.ProcessId &&
                    IsFreshTimestamp(target.CapturedAtTimestamp, Stopwatch.GetTimestamp(), MaximumAgeMilliseconds) ? target : null;
            }
        }

        // The application calls this only after explicit user approval. This
        // method itself reads window identity/class metadata, never UIA content.
        public bool ApproveWindow(IntPtr root, int processId)
        {
            if (disposed) return false;
            if (!IsEligibleWindow(root, processId, ownProcessId))
            {
                RevokeApproval();
                Interlocked.Exchange(ref latestOutcome, "DECLINED_WINDOW_POLICY");
                return false;
            }
            lock (requestLock)
            {
                if (disposed) return false;
                approval = new Approval(root, processId, ++approvalGeneration);
                InvalidateLocked("APPROVED_WINDOW");
                return true;
            }
        }

        public void CancelPending()
        {
            lock (requestLock) { if (!disposed) InvalidateLocked("DECLINED_INACTIVE"); }
        }

        public void RevokeApproval()
        {
            lock (requestLock)
            {
                approval = null;
                approvalGeneration++;
                InvalidateLocked("DECLINED_UNAPPROVED_WINDOW");
            }
        }

        public bool IsApprovedWindow(IntPtr root, int processId)
        {
            Approval allowed;
            lock (requestLock) { allowed = approval; }
            if (disposed || allowed == null || root != allowed.Root) return false;
            if (processId == allowed.ProcessId && IsEligibleWindow(root, processId, ownProcessId)) return true;
            lock (requestLock)
            {
                if (approval == allowed)
                {
                    approval = null;
                    approvalGeneration++;
                    InvalidateLocked("DECLINED_WINDOW_CHANGED");
                }
            }
            return false;
        }

        public void Request(PointF point) { Request(point, IntPtr.Zero); }
        public void Request(PointF point, IntPtr expectedRootWindow)
        {
            if (disposed) return;
            lock (requestLock)
            {
                if (disposed) return;
                if (!IsFinitePoint(point)) { InvalidateLocked("DECLINED_INVALID_POINT"); return; }
                if (approval == null || (expectedRootWindow != IntPtr.Zero && expectedRootWindow != approval.Root))
                { InvalidateLocked("DECLINED_UNAPPROVED_WINDOW"); return; }
                latestRequest = new RequestData { Point = point, ExpectedRoot = expectedRootWindow,
                    Version = ++requestVersion, Timestamp = Stopwatch.GetTimestamp(), ApprovalGeneration = approval.Generation };
                pending = true;
                SignalRequest();
            }
        }

        private void InvalidateLocked(string outcome)
        {
            latestRequest = new RequestData { Point = new PointF(float.NaN, float.NaN), Version = ++requestVersion,
                Timestamp = Stopwatch.GetTimestamp(), ApprovalGeneration = approvalGeneration };
            pending = false;
            Interlocked.Exchange(ref latest, null);
            Interlocked.Exchange(ref latestOutcome, outcome);
        }

        public void Dispose()
        {
            lock (requestLock)
            {
                if (disposed) return;
                disposed = true;
                approval = null;
                approvalGeneration++;
                InvalidateLocked("DECLINED_STOPPED");
                SignalRequest();
            }
            // Never wait for, abort, or replace a hung provider. One background
            // MTA worker owns the wait handle and releases it when it exits.
        }

        private void SignalRequest()
        {
            try { requestReady.Set(); } catch (ObjectDisposedException) { }
        }

        private void WorkerLoop()
        {
            long lastStarted = 0;
            try
            {
                while (!disposed)
                {
                    requestReady.WaitOne();
                    while (!disposed)
                    {
                        int delay = lastStarted == 0 ? 0 : (int)Math.Ceiling(MinimumProbeIntervalMilliseconds -
                            ElapsedMilliseconds(lastStarted, Stopwatch.GetTimestamp()));
                        if (delay > 0) { requestReady.WaitOne(delay); continue; }
                        RequestData request;
                        lock (requestLock)
                        {
                            if (disposed || !pending) break;
                            request = latestRequest;
                            pending = false;
                        }
                        lastStarted = Stopwatch.GetTimestamp();
                        ProbeResult result;
                        busy = true;
                        try { result = Probe(request); }
                        catch (ElementNotAvailableException) { result = Decline("DECLINED_ELEMENT_UNAVAILABLE"); }
                        catch (InvalidOperationException) { result = Decline("DECLINED_CACHE_INVALID_OPERATION"); }
                        catch (COMException) { result = Decline("DECLINED_PROVIDER_COM"); }
                        catch (NotSupportedException) { result = Decline("DECLINED_PROVIDER_NOT_SUPPORTED"); }
                        catch (Exception) { result = Decline("DECLINED_UNAVAILABLE"); }
                        finally { busy = false; Interlocked.Increment(ref completedProbes); }
                        if (disposed) break;
                        RequestData current;
                        Approval allowed;
                        lock (requestLock) { current = latestRequest; allowed = approval; }
                        if (allowed == null || request.ApprovalGeneration != allowed.Generation || !IsFinitePoint(current.Point))
                            continue;
                        HoverTarget target = result.Target;
                        if (target != null && (!IsFreshTimestamp(target.CapturedAtTimestamp, Stopwatch.GetTimestamp(), MaximumAgeMilliseconds) ||
                            !Contains(target.Bounds, current.Point) || FindExternalRootAtPoint(current.Point, ownProcessId) != allowed.Root))
                            result = Decline("DECLINED_STALE_OR_OCCLUDED");
                        lock (requestLock)
                        {
                            if (disposed) break;
                            if (latestRequest.Version == current.Version && approval == allowed)
                            {
                                Interlocked.Exchange(ref latest, result.Target);
                                Interlocked.Exchange(ref latestOutcome, result.Outcome);
                            }
                            if (!pending) break;
                        }
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref latest, null);
                requestReady.Dispose();
            }
        }

        private static ProbeResult Decline(string outcome) { return new ProbeResult(null, outcome); }

        private ProbeResult Probe(RequestData request)
        {
            if (!IsFreshTimestamp(request.Timestamp, Stopwatch.GetTimestamp(), MaximumAgeMilliseconds)) return Decline("DECLINED_STALE");
            Approval allowed;
            lock (requestLock) { allowed = approval; }
            if (allowed == null || request.ApprovalGeneration != allowed.Generation) return Decline("DECLINED_UNAPPROVED_WINDOW");
            IntPtr root = FindExternalRootAtPoint(request.Point, ownProcessId);
            if (root != allowed.Root || (request.ExpectedRoot != IntPtr.Zero && root != request.ExpectedRoot))
                return Decline("DECLINED_UNAPPROVED_WINDOW");
            int pid = WindowProcessId(root);
            if (!IsApprovedWindow(root, pid)) return Decline("DECLINED_WINDOW_CHANGED");
            RectangleF rootBounds;
            if (!TryWindowBounds(root, out rootBounds) || !Contains(rootBounds, request.Point)) return Decline("DECLINED_GEOMETRY");
            // Approval is checked before the sole UIA lookup. Revocation cannot
            // cancel an in-flight provider call, but its results are discarded.
            lock (requestLock) { if (approval != allowed || disposed) return Decline("DECLINED_UNAPPROVED_WINDOW"); }
            CacheRequest cache = GeometryCache();
            AutomationElement hit;
            try
            {
                using (cache.Activate())
                {
                    Interlocked.Increment(ref uiaLookups);
                    hit = AutomationElement.FromPoint(new System.Windows.Point(request.Point.X, request.Point.Y));
                }
            }
            catch (ElementNotAvailableException) { return Decline("DECLINED_POINT_ELEMENT_UNAVAILABLE"); }
            catch (InvalidOperationException) { return Decline("DECLINED_POINT_INVALID_OPERATION"); }
            catch (COMException) { return Decline("DECLINED_POINT_PROVIDER_COM"); }
            catch (NotSupportedException) { return Decline("DECLINED_POINT_NOT_SUPPORTED"); }
            ElementData data;
            string metadataDecline;
            if (hit == null) return Decline("DECLINED_POINT_EMPTY");
            if (!TryRead(hit, out data, out metadataDecline)) return Decline(metadataDecline);
            if (data.ProcessId == ownProcessId) return Decline("DECLINED_OWN_OVERLAY");
            if (data.ProcessId != allowed.ProcessId) return Decline("DECLINED_PROCESS_MISMATCH");
            if (data.Password) return Decline("DECLINED_SENSITIVE_CONTROL");
            if (!data.Enabled) return Decline("DECLINED_DISABLED");
            if (data.Offscreen) return Decline("DECLINED_OFFSCREEN");
            HoverControlKind kind;
            if (!TryControlKind(data.TypeId, out kind)) return Decline("DECLINED_UNSUPPORTED");
            RectangleF bounds;
            if (!TryTargetBounds(data.Rectangle.X, data.Rectangle.Y, data.Rectangle.Width, data.Rectangle.Height, request.Point, out bounds))
                return Decline("DECLINED_GEOMETRY");
            bounds = RectangleF.Intersect(bounds, rootBounds);
            if (bounds.Width < 1 || bounds.Height < 1 || !Contains(bounds, request.Point)) return Decline("DECLINED_GEOMETRY");
            if (!IsApprovedWindow(root, WindowProcessId(root)) || FindExternalRootAtPoint(request.Point, ownProcessId) != root)
                return Decline("DECLINED_STALE_OR_OCCLUDED");
            return new ProbeResult(new HoverTarget(bounds, request.Point, root, pid, kind, request.Timestamp,
                request.Version, allowed.Generation), kind == HoverControlKind.Button ? "OBSERVED_BUTTON" : "OBSERVED_MENU_ITEM");
        }

        private static CacheRequest GeometryCache()
        {
            CacheRequest request = new CacheRequest();
            request.TreeScope = TreeScope.Element;
            request.AutomationElementMode = AutomationElementMode.None;
            request.Add(AutomationElement.ControlTypeProperty);
            request.Add(AutomationElement.BoundingRectangleProperty);
            request.Add(AutomationElement.IsEnabledProperty);
            request.Add(AutomationElement.IsOffscreenProperty);
            request.Add(AutomationElement.IsPasswordProperty);
            request.Add(AutomationElement.ProcessIdProperty);
            return request;
        }

        private static bool ReadCached(AutomationElement element, AutomationProperty property, bool ignoreDefault,
            string field, out object value, out string decline)
        {
            value = null; decline = null;
            try { value = element.GetCachedPropertyValue(property, ignoreDefault); return true; }
            catch (ElementNotAvailableException) { decline = "DECLINED_METADATA_" + field + "_ELEMENT_UNAVAILABLE"; }
            catch (InvalidOperationException) { decline = "DECLINED_METADATA_" + field + "_NOT_CACHED"; }
            catch (COMException) { decline = "DECLINED_METADATA_" + field + "_COM"; }
            return false;
        }

        private static bool TryRead(AutomationElement element, out ElementData data, out string decline)
        {
            data = new ElementData();
            decline = null;
            object type, bounds, enabled, offscreen, password, process;
            if (!ReadCached(element, AutomationElement.ControlTypeProperty, true, "ROLE", out type, out decline)) return false;
            ControlType control = type as ControlType;
            if (control == null) { decline = "DECLINED_METADATA_ROLE_UNAVAILABLE"; return false; }
            if (!ReadCached(element, AutomationElement.BoundingRectangleProperty, true, "BOUNDS", out bounds, out decline)) return false;
            if (!(bounds is System.Windows.Rect)) { decline = "DECLINED_METADATA_BOUNDS_UNAVAILABLE"; return false; }
            if (!ReadCached(element, AutomationElement.IsEnabledProperty, true, "ENABLED", out enabled, out decline)) return false;
            if (!(enabled is bool)) { decline = "DECLINED_METADATA_ENABLED_UNAVAILABLE"; return false; }
            if (!ReadCached(element, AutomationElement.IsOffscreenProperty, true, "OFFSCREEN", out offscreen, out decline)) return false;
            if (!(offscreen is bool)) { decline = "DECLINED_METADATA_OFFSCREEN_UNAVAILABLE"; return false; }
            // IsPassword is an optional flag on Button/MenuItem. UIA documents
            // false as its default; accepting that cached default does not read
            // a value/text field or claim to classify every sensitive window.
            if (!ReadCached(element, AutomationElement.IsPasswordProperty, false, "PASSWORD", out password, out decline)) return false;
            if (!(password is bool)) { decline = "DECLINED_METADATA_PASSWORD_UNAVAILABLE"; return false; }
            if (!ReadCached(element, AutomationElement.ProcessIdProperty, true, "PROCESS", out process, out decline)) return false;
            if (!(process is int)) { decline = "DECLINED_METADATA_PROCESS_UNAVAILABLE"; return false; }
            data.TypeId = control.Id; data.Rectangle = (System.Windows.Rect)bounds; data.Enabled = (bool)enabled;
            data.Offscreen = (bool)offscreen; data.Password = (bool)password; data.ProcessId = (int)process;
            return true;
        }

        public static bool TryControlKind(int typeId, out HoverControlKind kind)
        {
            kind = HoverControlKind.Button;
            if (typeId == ControlType.Button.Id) return true;
            if (typeId == ControlType.MenuItem.Id) { kind = HoverControlKind.MenuItem; return true; }
            return false;
        }

        public static bool IsDeniedWindowClass(string className)
        {
            if (string.IsNullOrEmpty(className)) return true;
            string[] denied = { "#32770", "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
                "NotifyIconOverflowWindow", "MultitaskingViewFrame", "DV2ControlHost", "Credential Dialog Xaml Host",
                "LockScreenBackstopFrame", "LockScreenControllerProxyWindow", "Windows.UI.Core.CoreWindow" };
            foreach (string name in denied) if (string.Equals(className, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static bool IsEligibleWindow(IntPtr root, int pid, int ownPid)
        {
            if (root == IntPtr.Zero || pid <= 0 || pid == ownPid || !IsWindow(root) || !IsWindowVisible(root) ||
                GetAncestor(root, 2) != root || WindowProcessId(root) != pid || IsCloaked(root)) return false;
            StringBuilder className = new StringBuilder(128);
            if (GetClassName(root, className, className.Capacity) <= 0) return false;
            return !IsDeniedWindowClass(className.ToString());
        }

        public static bool TryTargetBounds(double x, double y, double width, double height, PointF point, out RectangleF bounds)
        {
            bounds = RectangleF.Empty;
            if (!IsFinitePoint(point) || !Finite(x) || !Finite(y) || !Finite(width) || !Finite(height) || width < 1 || height < 1 ||
                width > MaximumTargetWidth || height > MaximumTargetHeight || width * height > MaximumTargetArea ||
                Math.Abs(x) > MaximumCoordinate || Math.Abs(y) > MaximumCoordinate || Math.Abs(x + width) > MaximumCoordinate ||
                Math.Abs(y + height) > MaximumCoordinate) return false;
            bounds = new RectangleF((float)x, (float)y, (float)width, (float)height);
            return Contains(bounds, point);
        }

        public static bool IsFinitePoint(PointF point) { return Finite(point.X) && Finite(point.Y); }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        public static bool Contains(RectangleF bounds, PointF point)
        {
            return IsFinitePoint(point) && Finite(bounds.Left) && Finite(bounds.Top) && Finite(bounds.Width) && Finite(bounds.Height) &&
                bounds.Width > 0 && bounds.Height > 0 && point.X >= bounds.Left && point.X < bounds.Right && point.Y >= bounds.Top && point.Y < bounds.Bottom;
        }
        public static double ElapsedMilliseconds(long start, long end) { return ((double)end - start) * 1000 / Stopwatch.Frequency; }
        public static bool IsFreshTimestamp(long captured, long now, double maximumAgeMilliseconds)
        {
            return captured >= 0 && now >= captured && Finite(maximumAgeMilliseconds) && maximumAgeMilliseconds >= 0 &&
                ElapsedMilliseconds(captured, now) <= maximumAgeMilliseconds;
        }

        public static IntPtr FindExternalRootAtPoint(PointF point, int ownPid)
        {
            if (!IsFinitePoint(point) || Math.Abs(point.X) > MaximumCoordinate || Math.Abs(point.Y) > MaximumCoordinate || ownPid <= 0) return IntPtr.Zero;
            IntPtr window = GetTopWindow(IntPtr.Zero);
            for (int checkedWindows = 0; window != IntPtr.Zero && checkedWindows < 512; checkedWindows++)
            {
                int pid = WindowProcessId(window);
                RectangleF bounds;
                if (pid > 0 && pid != ownPid && IsWindowVisible(window) && TryWindowBounds(window, out bounds) && Contains(bounds, point) && !IsCloaked(window))
                    return window;
                window = GetWindow(window, 2);
            }
            return IntPtr.Zero;
        }

        public static int WindowProcessId(IntPtr window)
        {
            uint pid;
            GetWindowThreadProcessId(window, out pid);
            return pid <= int.MaxValue ? (int)pid : 0;
        }
        private static bool TryWindowBounds(IntPtr window, out RectangleF bounds)
        {
            bounds = RectangleF.Empty;
            RECT rectangle;
            if (!GetWindowRect(window, out rectangle) || rectangle.Right <= rectangle.Left || rectangle.Bottom <= rectangle.Top) return false;
            double width = (double)rectangle.Right - rectangle.Left, height = (double)rectangle.Bottom - rectangle.Top;
            if (width > MaximumCoordinate || height > MaximumCoordinate) return false;
            bounds = new RectangleF(rectangle.Left, rectangle.Top, (float)width, (float)height);
            return true;
        }
        private static bool IsCloaked(IntPtr window)
        {
            try { int cloaked; return DwmGetWindowAttribute(window, 14, out cloaked, sizeof(int)) == 0 && cloaked != 0; }
            catch (DllNotFoundException) { return false; }
            catch (EntryPointNotFoundException) { return false; }
        }

        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] private static extern IntPtr GetTopWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out RECT rectangle);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder className, int capacity);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
    }
}
