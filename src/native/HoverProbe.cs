using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;

namespace SpookyDesktopSpider
{
    internal enum HoverControlKind
    {
        Button, Hyperlink, CheckBox, RadioButton, TabItem, MenuItem
    }

    // A snapshot contains primitive geometry and identity only. No automation
    // element, provider, control pattern, label, or user content leaves the worker.
    internal sealed class HoverTarget
    {
        public readonly RectangleF Bounds;
        public readonly PointF ProbePoint;
        public readonly IntPtr RootWindow;
        public readonly int RootProcessId;
        public readonly HoverControlKind Kind;
        public readonly long CapturedAtTimestamp;
        public readonly long RequestVersion;
        public int ProcessId { get { return RootProcessId; } }
        public long ObservedAtTimestamp { get { return CapturedAtTimestamp; } }

        internal HoverTarget(RectangleF bounds, PointF point, IntPtr root, int processId,
            HoverControlKind kind, long timestamp, long version)
        {
            Bounds = bounds;
            ProbePoint = point;
            RootWindow = root;
            RootProcessId = processId;
            Kind = kind;
            CapturedAtTimestamp = timestamp;
            RequestVersion = version;
        }

        public double AgeMilliseconds
        {
            get { return HoverProbe.ElapsedMilliseconds(CapturedAtTimestamp, Stopwatch.GetTimestamp()); }
        }

        public bool IsFresh(PointF point, IntPtr currentRoot)
        {
            return IsFresh(point, currentRoot, HoverProbe.MaximumAgeMilliseconds);
        }

        public bool IsFresh(PointF point, IntPtr currentRoot, double maximumAgeMilliseconds)
        {
            return HoverProbe.IsFinitePoint(point) && HoverProbe.Contains(Bounds, point) &&
                currentRoot != IntPtr.Zero && currentRoot == RootWindow &&
                HoverProbe.IsFreshTimestamp(CapturedAtTimestamp, Stopwatch.GetTimestamp(), maximumAgeMilliseconds);
        }
    }

    // This detector performs read-only geometry queries. It never delivers input,
    // invokes a control, changes focus, or asks another application to hover.
    internal sealed class HoverProbe : IDisposable
    {
        public const double MaximumAgeMilliseconds = 300;
        public const double MinimumProbeIntervalMilliseconds = 100;
        public const int MaximumAncestorHops = 6;
        public const int MaximumFallbackNodes = 96;
        public const int MaximumFallbackDepth = 6;
        public const double MaximumTargetWidth = 600;
        public const double MaximumTargetHeight = 200;
        public const double MaximumTargetArea = 150000;
        private const double MaximumCoordinate = 1000000;
        private const double FallbackBudgetMilliseconds = 80;
        private readonly int ownProcessId;
        private readonly object requestLock = new object();
        private readonly AutoResetEvent requestReady = new AutoResetEvent(false);
        private readonly Thread worker;
        private volatile bool disposed;
        private volatile bool busy;
        private bool pending;
        private RequestData latestRequest;
        private long requestVersion, completedProbes;
        private HoverTarget latest;

        private struct RequestData
        {
            public PointF Point;
            public IntPtr ExpectedRoot;
            public long Version, Timestamp;
        }

        private struct ElementData
        {
            public int ProcessId, TypeId;
            public bool Enabled, Offscreen;
            public System.Windows.Rect Rectangle;
        }

        public HoverProbe(int ownPid)
        {
            if (ownPid <= 0) throw new ArgumentOutOfRangeException("ownPid");
            ownProcessId = ownPid;
            worker = new Thread(WorkerLoop);
            worker.Name = "Halloweb read-only hover geometry";
            worker.IsBackground = true;
            worker.SetApartmentState(ApartmentState.MTA);
            worker.Start();
        }

        public HoverTarget Latest
        {
            get
            {
                if (disposed) return null;
                HoverTarget target = Interlocked.CompareExchange(ref latest, null, null);
                return target != null && IsFreshTimestamp(target.CapturedAtTimestamp,
                    Stopwatch.GetTimestamp(), MaximumAgeMilliseconds) ? target : null;
            }
        }

        public bool IsBusy { get { return busy; } }
        public long CompletedProbes { get { return Interlocked.Read(ref completedProbes); } }

        public void Request(PointF point)
        {
            Request(point, IntPtr.Zero);
        }

        // The caller only replaces a one-slot request. All provider calls and
        // window selection happen on the existing MTA worker, never the UI thread.
        public void Request(PointF point, IntPtr expectedRootWindow)
        {
            if (disposed) return;
            lock (requestLock)
            {
                if (disposed) return;
                if (!IsFinitePoint(point))
                {
                    // Invalidate the one-slot request as well as its publication.
                    // An older in-flight result cannot republish after this epoch.
                    latestRequest = new RequestData { Point = point, Version = ++requestVersion,
                        Timestamp = Stopwatch.GetTimestamp() };
                    pending = false;
                    Interlocked.Exchange(ref latest, null);
                    return;
                }
                latestRequest = new RequestData { Point = point, ExpectedRoot = expectedRootWindow,
                    Version = ++requestVersion, Timestamp = Stopwatch.GetTimestamp() };
                pending = true;
                SignalRequest();
            }
        }

        public void Dispose()
        {
            lock (requestLock)
            {
                if (disposed) return;
                disposed = true;
                pending = false;
                Interlocked.Exchange(ref latest, null);
                SignalRequest();
            }
            // A provider may be hung. Do not join, abort, replace the worker, or
            // dispose its wait handle underneath an in-flight provider call.
            // The background worker owns that handle and releases it on exit.
        }

        private void SignalRequest()
        {
            try { requestReady.Set(); }
            catch (ObjectDisposedException) { /* A worker that already exited needs no signal. */ }
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
                        if (lastStarted != 0)
                        {
                            double age = ElapsedMilliseconds(lastStarted, Stopwatch.GetTimestamp());
                            int delay = (int)Math.Ceiling(MinimumProbeIntervalMilliseconds - age);
                            if (delay > 0) { requestReady.WaitOne(delay); continue; }
                        }
                        RequestData request;
                        lock (requestLock)
                        {
                            if (disposed || !pending) break;
                            request = latestRequest;
                            pending = false;
                        }
                        lastStarted = Stopwatch.GetTimestamp();
                        HoverTarget target = null;
                        busy = true;
                        try { target = Probe(request); }
                        catch (Exception) { target = null; } // Unavailable providers reveal no content or error text.
                        finally { busy = false; Interlocked.Increment(ref completedProbes); }
                        if (disposed) break;
                        RequestData current;
                        lock (requestLock) { current = latestRequest; }
                        if (target != null && (!IsFreshTimestamp(target.CapturedAtTimestamp,
                            Stopwatch.GetTimestamp(), MaximumAgeMilliseconds) || !Contains(target.Bounds, current.Point) ||
                            (current.ExpectedRoot != IntPtr.Zero && current.ExpectedRoot != target.RootWindow) ||
                            FindExternalRootAtPoint(current.Point, ownProcessId) != target.RootWindow))
                            target = null;
                        lock (requestLock)
                        {
                            if (disposed) break;
                            if (latestRequest.Version != current.Version) target = null;
                            Interlocked.Exchange(ref latest, target);
                        }
                        lock (requestLock) { if (!pending) break; }
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref latest, null);
                requestReady.Dispose();
            }
        }

        private HoverTarget Probe(RequestData request)
        {
            if (!IsFreshTimestamp(request.Timestamp, Stopwatch.GetTimestamp(), MaximumAgeMilliseconds)) return null;
            IntPtr root = FindExternalRootAtPoint(request.Point, ownProcessId);
            if (root == IntPtr.Zero || (request.ExpectedRoot != IntPtr.Zero && root != request.ExpectedRoot)) return null;
            int rootPid = WindowProcessId(root);
            if (rootPid <= 0 || rootPid == ownProcessId) return null;
            RectangleF rootBounds;
            if (!TryWindowBounds(root, out rootBounds) || !Contains(rootBounds, request.Point)) return null;

            CacheRequest cache = GeometryCache();
            {
                AutomationElement rootElement, hit = null;
                using (cache.Activate())
                {
                    rootElement = AutomationElement.FromHandle(root);
                    try { hit = AutomationElement.FromPoint(new System.Windows.Point(request.Point.X, request.Point.Y)); }
                    catch (ElementNotAvailableException) { hit = null; }
                    catch (InvalidOperationException) { hit = null; }
                    catch (COMException) { hit = null; }
                }
                ElementData rootData;
                if (rootElement == null || !TryRead(rootElement, out rootData) || rootData.ProcessId != rootPid || rootData.Offscreen)
                    return null;
                ElementData hitData = new ElementData();
                bool unavailable = hit == null || !TryRead(hit, out hitData);
                bool ownHit = !unavailable && hitData.ProcessId == ownProcessId;
                HoverTarget target = unavailable || ownHit ? null :
                    CandidateFromHit(hit, rootElement, request, root, rootPid, rootBounds, cache);
                if (target == null && (unavailable || ownHit))
                {
                    IntPtr nativeHit = DeepestNativeChild(root, request.Point);
                    AutomationElement fallbackRoot = rootElement;
                    if (nativeHit != root)
                    {
                        using (cache.Activate()) { fallbackRoot = AutomationElement.FromHandle(nativeHit); }
                        ElementData fallbackData;
                        if (fallbackRoot == null || !TryRead(fallbackRoot, out fallbackData) || fallbackData.ProcessId == ownProcessId)
                            return null;
                    }
                    target = CandidateFromRoot(fallbackRoot, request, root, rootPid, rootBounds, cache);
                }
                // A separate application may have covered or replaced the root
                // while a provider was answering. Such a result is discarded.
                if (target != null && (disposed || FindExternalRootAtPoint(request.Point, ownProcessId) != root)) return null;
                return target;
            }
        }

        private static CacheRequest GeometryCache()
        {
            CacheRequest request = new CacheRequest();
            request.TreeScope = TreeScope.Element;
            request.AutomationElementMode = AutomationElementMode.Full;
            request.Add(AutomationElement.ControlTypeProperty);
            request.Add(AutomationElement.BoundingRectangleProperty);
            request.Add(AutomationElement.IsEnabledProperty);
            request.Add(AutomationElement.IsOffscreenProperty);
            request.Add(AutomationElement.ProcessIdProperty);
            return request;
        }

        private static bool TryRead(AutomationElement element, out ElementData data)
        {
            data = new ElementData();
            if (element == null) return false;
            object type = element.GetCachedPropertyValue(AutomationElement.ControlTypeProperty, true);
            object bounds = element.GetCachedPropertyValue(AutomationElement.BoundingRectangleProperty, true);
            object enabled = element.GetCachedPropertyValue(AutomationElement.IsEnabledProperty, true);
            object offscreen = element.GetCachedPropertyValue(AutomationElement.IsOffscreenProperty, true);
            object process = element.GetCachedPropertyValue(AutomationElement.ProcessIdProperty, true);
            ControlType control = type as ControlType;
            if (control == null || !(bounds is System.Windows.Rect) || !(enabled is bool) || !(offscreen is bool) || !(process is int)) return false;
            data.TypeId = control.Id;
            data.Rectangle = (System.Windows.Rect)bounds;
            data.Enabled = (bool)enabled;
            data.Offscreen = (bool)offscreen;
            data.ProcessId = (int)process;
            return true;
        }

        private HoverTarget CandidateFromHit(AutomationElement element, AutomationElement rootElement, RequestData request,
            IntPtr root, int rootPid, RectangleF rootBounds, CacheRequest cache)
        {
            HoverTarget candidate = null;
            for (int hop = 0; element != null && hop <= MaximumAncestorHops; hop++)
            {
                ElementData data;
                if (!TryRead(element, out data) || data.ProcessId == ownProcessId) return null;
                // Identity comparison reads no application content. A candidate
                // must be proven to belong to the selected visible root; process
                // identity alone cannot distinguish overlapping sibling windows.
                if (Automation.Compare(element, rootElement)) return candidate;
                HoverTarget matched = MakeCandidate(data, request, root, rootPid, rootBounds);
                if (candidate == null && matched != null) candidate = matched;
                if (hop < MaximumAncestorHops) element = TreeWalker.ControlViewWalker.GetParent(element, cache);
            }
            return null;
        }

        private HoverTarget CandidateFromRoot(AutomationElement rootElement, RequestData request,
            IntPtr root, int rootPid, RectangleF rootBounds, CacheRequest cache)
        {
            // If FromPoint sees our transparent spider/silk/glow instead, query
            // only the selected external root's bounded control-view subtree.
            // We never hide overlays, transfer focus, or dispatch pointer input.
            int visited = 0;
            long started = Stopwatch.GetTimestamp();
            HoverTarget best = null;
            AutomationElement element = rootElement;
            int depth = 0;
            while (element != null && depth <= MaximumFallbackDepth && visited < MaximumFallbackNodes && !disposed &&
                ElapsedMilliseconds(started, Stopwatch.GetTimestamp()) <= FallbackBudgetMilliseconds)
            {
                visited++;
                ElementData data;
                if (!TryRead(element, out data) || data.ProcessId <= 0 || data.ProcessId == ownProcessId || data.Offscreen || !data.Enabled) return null;
                System.Windows.Rect rectangle = data.Rectangle;
                if (rectangle.IsEmpty || !Finite(rectangle.X) || !Finite(rectangle.Y) || !Finite(rectangle.Width) ||
                    !Finite(rectangle.Height) || rectangle.Width <= 0 || rectangle.Height <= 0 ||
                    request.Point.X < rectangle.Left || request.Point.X >= rectangle.Right ||
                    request.Point.Y < rectangle.Top || request.Point.Y >= rectangle.Bottom) return null;
                HoverTarget candidate = MakeCandidate(data, request, root, rootPid, rootBounds);
                if (candidate != null && (best == null || candidate.Bounds.Width * candidate.Bounds.Height < best.Bounds.Width * best.Bounds.Height))
                    best = candidate;
                if (depth >= MaximumFallbackDepth) return best;
                AutomationElement containingChild = null;
                AutomationElement child = TreeWalker.ControlViewWalker.GetFirstChild(element, cache);
                while (child != null && visited < MaximumFallbackNodes && !disposed &&
                    ElapsedMilliseconds(started, Stopwatch.GetTimestamp()) <= FallbackBudgetMilliseconds)
                {
                    visited++;
                    ElementData childData;
                    if (TryRead(child, out childData) && childData.ProcessId > 0 && childData.ProcessId != ownProcessId && !childData.Offscreen &&
                        !childData.Rectangle.IsEmpty && childData.Rectangle.Contains(new System.Windows.Point(request.Point.X, request.Point.Y)))
                    {
                        // UIA logical sibling order is not a visual z-order.
                        // Ambiguous overlapping branches cannot prove a target.
                        if (containingChild != null) return null;
                        containingChild = child;
                    }
                    child = TreeWalker.ControlViewWalker.GetNextSibling(child, cache);
                }
                if (child != null) return null; // Budget exhaustion cannot prove uniqueness.
                if (containingChild == null) return best;
                element = containingChild;
                depth++;
            }
            return null;
        }

        private HoverTarget MakeCandidate(ElementData data, RequestData request, IntPtr root, int rootPid, RectangleF rootBounds)
        {
            HoverControlKind kind;
            RectangleF bounds;
            if (!data.Enabled || data.Offscreen || data.ProcessId <= 0 || data.ProcessId == ownProcessId ||
                !TryControlKind(data.TypeId, out kind) || !TryTargetBounds(data.Rectangle.X, data.Rectangle.Y,
                    data.Rectangle.Width, data.Rectangle.Height, request.Point, out bounds)) return null;
            bounds = RectangleF.Intersect(bounds, rootBounds);
            if (!Contains(bounds, request.Point) || bounds.Width < 1 || bounds.Height < 1) return null;
            return new HoverTarget(bounds, request.Point, root, rootPid, kind, request.Timestamp, request.Version);
        }

        public static bool TryControlKind(int typeId, out HoverControlKind kind)
        {
            kind = HoverControlKind.Button;
            if (typeId == ControlType.Button.Id) return true;
            if (typeId == ControlType.Hyperlink.Id) { kind = HoverControlKind.Hyperlink; return true; }
            if (typeId == ControlType.CheckBox.Id) { kind = HoverControlKind.CheckBox; return true; }
            if (typeId == ControlType.RadioButton.Id) { kind = HoverControlKind.RadioButton; return true; }
            if (typeId == ControlType.TabItem.Id) { kind = HoverControlKind.TabItem; return true; }
            if (typeId == ControlType.MenuItem.Id) { kind = HoverControlKind.MenuItem; return true; }
            return false;
        }

        public static bool TryTargetBounds(double x, double y, double width, double height, PointF point, out RectangleF bounds)
        {
            bounds = RectangleF.Empty;
            if (!IsFinitePoint(point) || !Finite(x) || !Finite(y) || !Finite(width) || !Finite(height) ||
                width < 1 || height < 1 || width > MaximumTargetWidth || height > MaximumTargetHeight ||
                width * height > MaximumTargetArea || Math.Abs(x) > MaximumCoordinate || Math.Abs(y) > MaximumCoordinate ||
                Math.Abs(x + width) > MaximumCoordinate || Math.Abs(y + height) > MaximumCoordinate) return false;
            bounds = new RectangleF((float)x, (float)y, (float)width, (float)height);
            return Contains(bounds, point);
        }

        public static bool IsFinitePoint(PointF point) { return Finite(point.X) && Finite(point.Y); }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }

        public static bool Contains(RectangleF bounds, PointF point)
        {
            return IsFinitePoint(point) && Finite(bounds.Left) && Finite(bounds.Top) && Finite(bounds.Width) && Finite(bounds.Height) &&
                bounds.Width > 0 && bounds.Height > 0 && point.X >= bounds.Left && point.X < bounds.Right &&
                point.Y >= bounds.Top && point.Y < bounds.Bottom;
        }

        public static double ElapsedMilliseconds(long start, long end)
        {
            return ((double)end - start) * 1000 / Stopwatch.Frequency;
        }

        public static bool IsFreshTimestamp(long captured, long now, double maximumAgeMilliseconds)
        {
            if (captured < 0 || now < captured || !Finite(maximumAgeMilliseconds) || maximumAgeMilliseconds < 0) return false;
            return ElapsedMilliseconds(captured, now) <= maximumAgeMilliseconds;
        }

        // Read-only z-order geometry avoids considering an occluded control or
        // treating our own transparent overlay as the application underneath it.
        public static IntPtr FindExternalRootAtPoint(PointF point, int ownPid)
        {
            if (!IsFinitePoint(point) || Math.Abs(point.X) > MaximumCoordinate || Math.Abs(point.Y) > MaximumCoordinate || ownPid <= 0) return IntPtr.Zero;
            IntPtr window = GetTopWindow(IntPtr.Zero);
            for (int checkedWindows = 0; window != IntPtr.Zero && checkedWindows < 512; checkedWindows++)
            {
                int processId = WindowProcessId(window);
                RectangleF bounds;
                if (processId > 0 && processId != ownPid && IsWindowVisible(window) &&
                    TryWindowBounds(window, out bounds) && Contains(bounds, point) && !IsCloaked(window)) return window;
                window = GetWindow(window, 2); // GW_HWNDNEXT, z-order only.
            }
            return IntPtr.Zero;
        }

        private static IntPtr DeepestNativeChild(IntPtr root, PointF point)
        {
            IntPtr current = root;
            for (int hop = 0; hop < MaximumAncestorHops; hop++)
            {
                POINT clientPoint = new POINT { X = (int)Math.Round(point.X), Y = (int)Math.Round(point.Y) };
                if (!ScreenToClient(current, ref clientPoint)) break;
                IntPtr child = ChildWindowFromPointEx(current, clientPoint, 1); // CWP_SKIPINVISIBLE only.
                if (child == IntPtr.Zero || child == current || !IsWindowVisible(child)) break;
                current = child;
            }
            return current;
        }

        private static int WindowProcessId(IntPtr window)
        {
            uint processId;
            GetWindowThreadProcessId(window, out processId);
            return processId <= int.MaxValue ? (int)processId : 0;
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
            try
            {
                int cloaked;
                return DwmGetWindowAttribute(window, 14, out cloaked, sizeof(int)) == 0 && cloaked != 0;
            }
            catch (DllNotFoundException) { return false; }
            catch (EntryPointNotFoundException) { return false; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }
        [DllImport("user32.dll")] private static extern IntPtr GetTopWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out RECT rectangle);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr window, ref POINT point);
        [DllImport("user32.dll")] private static extern IntPtr ChildWindowFromPointEx(IntPtr parent, POINT point, uint flags);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
    }
}
