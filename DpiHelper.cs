using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RScreenRec
{
    public static class DpiHelper
    {
        [DllImport("user32.dll")]
        private static extern int GetDpiForWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType,
            out uint dpiX, out uint dpiY);

        private const int DEFAULT_DPI = 96;

        public static float GetSystemDpiScale()
        {
            try
            {
                IntPtr desktop = GetDesktopWindow();
                int dpi = GetDpiForWindow(desktop);
                return (float)dpi / DEFAULT_DPI;
            }
            catch
            {
                // Fallback using Graphics
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                {
                    return g.DpiX / DEFAULT_DPI;
                }
            }
        }

        public static float GetScreenDpiScale(Screen screen)
        {
            if (screen == null)
                return GetSystemDpiScale();
            try
            {
                var bounds = screen.Bounds;
                var center = new NativePoint
                {
                    X = bounds.Left + bounds.Width / 2,
                    Y = bounds.Top + bounds.Height / 2
                };
                IntPtr monitor = MonitorFromPoint(center, 2); // MONITOR_DEFAULTTONEAREST
                uint x, y;
                if (monitor != IntPtr.Zero && GetDpiForMonitor(monitor, 0, out x, out y) == 0 &&
                    x >= DEFAULT_DPI / 2 && x <= DEFAULT_DPI * 6)
                    return x / (float)DEFAULT_DPI;
            }
            catch (EntryPointNotFoundException) { }
            catch (DllNotFoundException) { }
            catch (Exception) { }
            return GetSystemDpiScale();
        }

        public static int ScaleValue(int value, float scale)
        {
            return (int)Math.Round(value * scale);
        }

        public static Size ScaleSize(Size size, float scale)
        {
            return new Size(
                ScaleValue(size.Width, scale),
                ScaleValue(size.Height, scale)
            );
        }

        public static Point ScalePoint(Point point, float scale)
        {
            return new Point(
                ScaleValue(point.X, scale),
                ScaleValue(point.Y, scale)
            );
        }

        public static Rectangle ScaleRectangle(Rectangle rect, float scale)
        {
            return new Rectangle(
                ScaleValue(rect.X, scale),
                ScaleValue(rect.Y, scale),
                ScaleValue(rect.Width, scale),
                ScaleValue(rect.Height, scale)
            );
        }
    }
}