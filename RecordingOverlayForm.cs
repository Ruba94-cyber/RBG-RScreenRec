using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RScreenRec
{
    public class RecordingOverlayForm : Form
    {
        private readonly Timer blinkTimer;
        private bool blinkOn = true;
        private readonly float dpiScale;
        private bool dragging;
        private Point dragStartCursor;
        private Point dragStartWindow;

        public RecordingOverlayForm(Rectangle screenBounds)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;

            dpiScale = DpiHelper.GetSystemDpiScale();
            int scaledOffsetY = DpiHelper.ScaleValue(66, dpiScale);
            int scaledOffsetX = DpiHelper.ScaleValue(32, dpiScale);
            int scaledWidth = DpiHelper.ScaleValue(94, dpiScale);
            int scaledHeight = DpiHelper.ScaleValue(29, dpiScale);

            Size = new Size(scaledWidth, scaledHeight);
            Point defaultLocation = new Point(
                screenBounds.Right - scaledWidth - scaledOffsetX,
                screenBounds.Bottom - scaledHeight - scaledOffsetY
            );
            Point? savedLocation = ReadSavedPosition();
            Location = savedLocation.HasValue
                ? EnsureVisible(savedLocation.Value, Size, defaultLocation)
                : defaultLocation;
            TopMost = true;
            ShowInTaskbar = false;
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;
            DoubleBuffered = true;

            Load += (s, e) =>
            {
                TopMost = true;
                BringToFront();
                ForceOnTop(Handle);
                ExcludeFromCapture(Handle);
            };

            blinkTimer = new Timer { Interval = 520 };
            blinkTimer.Tick += (s, e) =>
            {
                // Keep the indicator draggable even while the dot blinks.
                blinkOn = !blinkOn;
                Invalidate();
            };
            Shown += (s, e) => blinkTimer.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            int padding = DpiHelper.ScaleValue(7, dpiScale);
            int cornerRadius = DpiHelper.ScaleValue(9, dpiScale);

            Rectangle pillRect = new Rectangle(0, 0, Width - 1, Height - 1);
            Rectangle shadowRect = pillRect;
            shadowRect.Offset(DpiHelper.ScaleValue(2, dpiScale), DpiHelper.ScaleValue(2, dpiScale));

            using (GraphicsPath shadowPath = CreateRoundedRectangle(shadowRect, cornerRadius))
            using (SolidBrush shadowBrush = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
            {
                e.Graphics.FillPath(shadowBrush, shadowPath);
            }

            using (GraphicsPath pillPath = CreateRoundedRectangle(pillRect, cornerRadius))
            using (LinearGradientBrush pillBrush = new LinearGradientBrush(
                pillRect,
                Color.FromArgb(230, 34, 34, 34),
                Color.FromArgb(230, 24, 24, 24),
                LinearGradientMode.Vertical))
            using (Pen borderPen = new Pen(Color.FromArgb(190, 255, 255, 255), 1f))
            {
                e.Graphics.FillPath(pillBrush, pillPath);
                e.Graphics.DrawPath(borderPen, pillPath);
            }

            int baseDotSize = DpiHelper.ScaleValue(10, dpiScale);
            int dotSize = baseDotSize;
            int dotX = padding;
            int dotY = (Height - dotSize) / 2;

            int glowSize = dotSize + DpiHelper.ScaleValue(6, dpiScale);
            Rectangle glowRect = new Rectangle(
                dotX - (glowSize - dotSize) / 2,
                (Height - glowSize) / 2,
                glowSize,
                glowSize);

            Rectangle dotRect = new Rectangle(dotX, dotY, dotSize, dotSize);
            using (SolidBrush glowBrush = new SolidBrush(Color.FromArgb(blinkOn ? 180 : 25, 255, 56, 56)))
            using (SolidBrush dotBrush = new SolidBrush(blinkOn
                ? Color.FromArgb(255, 255, 35, 35)
                : Color.FromArgb(130, 160, 60, 60)))
            {
                e.Graphics.FillEllipse(glowBrush, glowRect);
                e.Graphics.FillEllipse(dotBrush, dotRect);
            }

            int highlightSize = Math.Max(2, dotSize / 3);
            Rectangle highlightRect = new Rectangle(
                dotRect.Left + dotSize / 3,
                dotRect.Top + dotSize / 4,
                highlightSize,
                highlightSize);
            using (SolidBrush highlightBrush = new SolidBrush(Color.FromArgb(210, 255, 210, 210)))
            {
                e.Graphics.FillEllipse(highlightBrush, highlightRect);
            }

            string label = "REC";
            float fontSize = DpiHelper.ScaleValue(10, dpiScale);
            using (Font font = new Font("Segoe UI Semibold", fontSize, FontStyle.Bold, GraphicsUnit.Pixel))
            using (SolidBrush textBrush = new SolidBrush(Color.White))
            {
                SizeF textSize = e.Graphics.MeasureString(label, font);
                float textX = dotRect.Right + DpiHelper.ScaleValue(7, dpiScale);
                float textY = (Height - textSize.Height) / 2;
                e.Graphics.DrawString(label, font, textBrush, textX, textY);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
                return;

            dragging = true;
            dragStartCursor = Cursor.Position;
            dragStartWindow = Location;
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!dragging)
                return;

            Point current = Cursor.Position;
            Screen screen = Screen.FromPoint(current);
            Rectangle area = screen.WorkingArea;
            Point wanted = new Point(
                dragStartWindow.X + current.X - dragStartCursor.X,
                dragStartWindow.Y + current.Y - dragStartCursor.Y);
            Location = new Point(
                Math.Max(area.Left, Math.Min(wanted.X, area.Right - Width)),
                Math.Max(area.Top, Math.Min(wanted.Y, area.Bottom - Height)));
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left || !dragging)
                return;

            dragging = false;
            Capture = false;
            SavePosition(Location);
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (dragging)
            {
                dragging = false;
                SavePosition(Location);
            }
        }

        private static string PositionFile
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "RBG", "RScreenRec", "overlay-position.txt");
            }
        }

        private static Point? ReadSavedPosition()
        {
            try
            {
                if (!File.Exists(PositionFile))
                    return null;

                string[] coordinates = File.ReadAllText(PositionFile).Split(',');
                int x, y;
                if (coordinates.Length == 2 &&
                    int.TryParse(coordinates[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out x) &&
                    int.TryParse(coordinates[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out y))
                    return new Point(x, y);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return null;
        }

        private static void SavePosition(Point position)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PositionFile));
                File.WriteAllText(PositionFile,
                    position.X.ToString(CultureInfo.InvariantCulture) + "," +
                    position.Y.ToString(CultureInfo.InvariantCulture));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static Point EnsureVisible(Point position, Size size, Point fallback)
        {
            foreach (Screen screen in Screen.AllScreens)
            {
                Rectangle work = screen.WorkingArea;
                if (work.IntersectsWith(new Rectangle(position, size)))
                {
                    return new Point(
                        Math.Max(work.Left, Math.Min(position.X, work.Right - size.Width)),
                        Math.Max(work.Top, Math.Min(position.Y, work.Bottom - size.Height)));
                }
            }
            return fallback;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (blinkTimer != null)
                {
                    blinkTimer.Stop();
                    blinkTimer.Dispose();
                }
            }
            base.Dispose(disposing);
        }

        // Keep the window always on top
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const UInt32 SWP_NOMOVE = 0x0002;
        private const UInt32 SWP_NOSIZE = 0x0001;
        private const UInt32 SWP_NOACTIVATE = 0x0010;
        private const UInt32 SWP_SHOWWINDOW = 0x0040;
        private const UInt32 WDA_EXCLUDEFROMCAPTURE = 0x00000011;

        private void ForceOnTop(IntPtr handle)
        {
            SetWindowPos(handle, HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }

        private void ExcludeFromCapture(IntPtr handle)
        {
            try
            {
                if (!SetWindowDisplayAffinity(handle, WDA_EXCLUDEFROMCAPTURE))
                {
                    int error = Marshal.GetLastWin32Error();
                    Logger.Log("REC overlay exclusion could not be enabled. Win32 error: " + error);
                }
            }
            catch (Exception ex)
            {
                Logger.Log("REC overlay exclusion is unavailable.", ex);
            }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                // No activation, but allow mouse interaction for dragging.
                // WS_EX_TRANSPARENT would make the badge impossible to drag.
                cp.ExStyle |= 0x80 | 0x8 | 0x80000 | 0x08000000; // TOOLWINDOW | TOPMOST | LAYERED | NOACTIVATE
                return cp;
            }
        }

        private static GraphicsPath CreateRoundedRectangle(Rectangle rect, int radius)
        {
            int diameter = radius * 2;
            Size size = new Size(diameter, diameter);
            Rectangle arc = new Rectangle(rect.Location, size);
            GraphicsPath path = new GraphicsPath();

            path.AddArc(arc, 180, 90);
            arc.X = rect.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = rect.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = rect.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
