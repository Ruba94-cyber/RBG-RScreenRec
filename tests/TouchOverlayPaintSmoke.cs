using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using RScreenRec;

internal static class TouchOverlayPaintSmoke
{
    [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr context);

    [STAThread]
    private static int Main()
    {
        try
        {
            try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch (EntryPointNotFoundException) { }
            using (var left = new TouchOverlayForm(new Rectangle(0, 0, 100, 100)))
            using (var right = new TouchOverlayForm(new Rectangle(100, 0, 100, 100)))
            {
                IntPtr leftHandle = left.Handle;
                IntPtr rightHandle = right.Handle;
                left.Show();
                right.Show();
                Application.DoEvents();
                int leftPaints = 0, rightPaints = 0;
                left.Paint += (sender, args) => leftPaints++;
                right.Paint += (sender, args) => rightPaints++;
                MethodInfo add = typeof(TouchOverlayForm).GetMethod("AddActiveTouchPoint", BindingFlags.NonPublic | BindingFlags.Static);
                add.Invoke(null, new object[] { new Point(100, 50) });
                DateTime repaintDeadline = DateTime.UtcNow.AddMilliseconds(300);
                while ((leftPaints == 0 || rightPaints == 0) && DateTime.UtcNow < repaintDeadline)
                {
                    Application.DoEvents();
                    Thread.Sleep(5);
                }
                if (leftPaints == 0 || rightPaints == 0)
                    throw new InvalidOperationException("A shared touch event did not repaint both overlay tiles.");
                if (!IsRed(PaintPixel(left, 99, 50)) || !IsRed(PaintPixel(right, 0, 50)))
                    throw new InvalidOperationException("A touch marker is clipped at the tile seam.");
                if (TouchOverlayForm.GetActiveTouchPointsSnapshot().Length != 1)
                    throw new InvalidOperationException("The shared touch marker was not available to recording.");
                Thread.Sleep(750);
                if (IsRed(PaintPixel(left, 99, 50)) || IsRed(PaintPixel(right, 0, 50)) ||
                    TouchOverlayForm.GetActiveTouchPointsSnapshot().Length != 0)
                    throw new InvalidOperationException("The touch marker did not expire in both tiles.");
            }
            Console.WriteLine("touch_overlay_seam=continuous; expiry=ok");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static Color PaintPixel(TouchOverlayForm form, int x, int y)
    {
        using (var bitmap = new Bitmap(form.Width, form.Height))
        {
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            return bitmap.GetPixel(x, y);
        }
    }

    private static bool IsRed(Color color) { return color.R > 150 && color.G < 100 && color.B < 100; }
}
