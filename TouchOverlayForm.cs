using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RScreenRec
{
    public class TouchOverlayForm : Form
    {
        [DllImport("user32.dll")]
        static extern bool RegisterTouchWindow(IntPtr hWnd, uint ulFlags);

        [DllImport("user32.dll")]
        private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

        [DllImport("user32.dll")]
        static extern bool GetTouchInputInfo(IntPtr hTouchInput, int cInputs, [In, Out] TOUCHINPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        static extern void CloseTouchInputHandle(IntPtr lParam);

        private const int WM_TOUCH = 0x0240;
        private const int TOUCHEVENTF_DOWN = 0x0002;
        private const int WS_EX_TOOLWINDOW = 0x80;
        private const int WS_EX_TOPMOST = 0x8;
        private const int WS_EX_LAYERED = 0x80000;
        private const int WS_EX_TRANSPARENT = 0x20;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

        [StructLayout(LayoutKind.Sequential)]
        public struct TOUCHINPUT
        {
            public int x;
            public int y;
            public IntPtr hSource;
            public uint dwID;
            public uint dwFlags;
            public uint dwMask;
            public uint time;
            public IntPtr dwExtraInfo;
            public uint cxContact;
            public uint cyContact;
        }

        private static readonly object ActiveTouchSync = new object();
        private static readonly List<TouchMarker> ActiveTouchPoints = new List<TouchMarker>();
        private static readonly List<TouchMarker> VisualTouchPoints = new List<TouchMarker>();
        private static event Action VisualTouchChanged;
        private readonly Timer cleanupTimer;
        private bool markersPainted;

        private struct TouchMarker
        {
            public Point Point;
            public DateTime ExpiresAtUtc;
        }

        public TouchOverlayForm(Rectangle screenBounds)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = screenBounds;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;
            DoubleBuffered = true;
            Opacity = 0.5;

            Load += (s, e) =>
            {
                RegisterTouchWindow(Handle, 0);
                ExcludeFromCapture(Handle);
                BringToFront();
                VisualTouchChanged += OnVisualTouchChanged;
            };

            cleanupTimer = new Timer { Interval = 100 };
            cleanupTimer.Tick += (s, e) =>
            {
                if (markersPainted || GetVisualTouchPointsSnapshot().Length > 0) Invalidate();
            };
            cleanupTimer.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                VisualTouchChanged -= OnVisualTouchChanged;
                cleanupTimer.Stop();
                cleanupTimer.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_TOUCH)
            {
                int inputCount = (m.WParam.ToInt32() & 0xFFFF);
                TOUCHINPUT[] inputs = new TOUCHINPUT[inputCount];
                if (GetTouchInputInfo(m.LParam, inputCount, inputs, Marshal.SizeOf(typeof(TOUCHINPUT))))
                {
                    foreach (var ti in inputs)
                    {
                        if ((ti.dwFlags & TOUCHEVENTF_DOWN) != 0)
                        {
                            Point screenPoint = new Point(
                                (int)Math.Round(ti.x / 100.0),
                                (int)Math.Round(ti.y / 100.0));
                            // Recording consumes absolute desktop coordinates, not overlay client coordinates.
                            AddActiveTouchPoint(screenPoint);
                        }
                    }
                }
                CloseTouchInputHandle(m.LParam);
                Invalidate();
            }

            base.WndProc(ref m);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            float dpiScale = DpiHelper.GetSystemDpiScale();
            int scaledRadius = DpiHelper.ScaleValue(20, dpiScale);
            int scaledBorderWidth = DpiHelper.ScaleValue(3, dpiScale);

            Point[] visualPoints = GetVisualTouchPointsSnapshot();
            markersPainted = visualPoints.Length > 0;
            foreach (Point screenPoint in visualPoints)
            {
                Point pt = PointToClient(screenPoint);
                Rectangle r = new Rectangle(
                    pt.X - scaledRadius, pt.Y - scaledRadius,
                    scaledRadius * 2, scaledRadius * 2
                );
                if (!r.IntersectsWith(ClientRectangle)) continue;

                using (Pen border = new Pen(Color.White, scaledBorderWidth))
                using (Brush fill = new SolidBrush(Color.Red))
                {
                    e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    e.Graphics.FillEllipse(fill, r);
                    e.Graphics.DrawEllipse(border, r);
                }
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE;
                return cp;
            }
        }

        private void ExcludeFromCapture(IntPtr handle)
        {
            try
            {
                SetWindowDisplayAffinity(handle, WDA_EXCLUDEFROMCAPTURE);
            }
            catch { }
        }

        public static Point[] GetActiveTouchPointsSnapshot()
        {
            lock (ActiveTouchSync)
            {
                RemoveExpiredActiveTouchPoints();
                Point[] points = new Point[ActiveTouchPoints.Count];
                for (int i = 0; i < ActiveTouchPoints.Count; i++)
                    points[i] = ActiveTouchPoints[i].Point;
                return points;
            }
        }

        private static void AddActiveTouchPoint(Point point)
        {
            lock (ActiveTouchSync)
            {
                RemoveExpiredActiveTouchPoints();
                ActiveTouchPoints.Add(new TouchMarker
                {
                    Point = point,
                    ExpiresAtUtc = DateTime.UtcNow.AddMilliseconds(650)
                });
                VisualTouchPoints.Add(new TouchMarker
                {
                    Point = point,
                    ExpiresAtUtc = DateTime.UtcNow.AddMilliseconds(500)
                });
            }
            Action changed = VisualTouchChanged;
            if (changed != null) changed();
        }

        private void OnVisualTouchChanged()
        {
            try { if (IsHandleCreated && !IsDisposed) BeginInvoke(new Action(Invalidate)); }
            catch (InvalidOperationException) { }
        }

        private static Point[] GetVisualTouchPointsSnapshot()
        {
            lock (ActiveTouchSync)
            {
                DateTime now = DateTime.UtcNow;
                VisualTouchPoints.RemoveAll(marker => marker.ExpiresAtUtc <= now);
                return VisualTouchPoints.ConvertAll(marker => marker.Point).ToArray();
            }
        }

        private static void RemoveExpiredActiveTouchPoints()
        {
            DateTime now = DateTime.UtcNow;
            for (int i = ActiveTouchPoints.Count - 1; i >= 0; i--)
            {
                if (ActiveTouchPoints[i].ExpiresAtUtc <= now)
                    ActiveTouchPoints.RemoveAt(i);
            }
        }
    }
}
