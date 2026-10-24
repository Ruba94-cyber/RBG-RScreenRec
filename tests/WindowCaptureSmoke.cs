using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using RScreenRec;
using FlaButton = FlaUI.Core.AutomationElements.Button;
using FlaListBox = FlaUI.Core.AutomationElements.ListBox;
using FlaTextBox = FlaUI.Core.AutomationElements.TextBox;

internal static class WindowCaptureSmoke
{
    [DllImport("user32.dll", EntryPoint = "SetProcessDpiAwarenessContext")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string windowName);
    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder className, int maxCount);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int maxCount);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessageInt(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageText(IntPtr window, uint message, IntPtr wParam, StringBuilder lParam);
    [DllImport("user32.dll")]
    private static extern bool MoveWindow(IntPtr window, int x, int y, int width, int height, bool repaint);
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private static IntPtr SelectTestWindow(IntPtr expectedWindow)
    {
        Type pickerType = typeof(ScreenRecorder).Assembly.GetType("RScreenRec.WindowPicker", true);
        MethodInfo pickerMethod = pickerType.GetMethod("SelectWindow", BindingFlags.Static | BindingFlags.NonPublic);
        if (pickerMethod == null)
            throw new MissingMethodException("RScreenRec.WindowPicker.SelectWindow was not found.");

        Exception automationFailure = null;
        Window picker = null;
        var automationThread = new Thread(() =>
        {
            try
            {
                using (var automation = new UIA3Automation())
                {
                    var desktop = automation.GetDesktop();
                    DateTime deadline = DateTime.UtcNow.AddSeconds(15);
                    while (DateTime.UtcNow < deadline)
                    {
                        picker = desktop.FindFirstChild(cf => cf
                            .ByName("Select window - RScreenRec")
                            .And(cf.ByControlType(ControlType.Window)))?.AsWindow();
                        if (picker != null) break;
                        Thread.Sleep(100);
                    }
                    if (picker == null)
                        throw new TimeoutException("FlaUI could not find the RScreenRec window picker.");

                    FlaListBox list = picker.FindFirstDescendant(cf => cf
                        .ByControlType(ControlType.List))?.AsListBox();
                    if (list == null)
                        throw new InvalidOperationException("FlaUI could not find the picker window list.");

                    FlaTextBox search = picker.FindFirstDescendant(cf => cf
                        .ByControlType(ControlType.Edit))?.AsTextBox();
                    if (search == null)
                        throw new InvalidOperationException("FlaUI could not find the picker search box.");
                    search.Text = "synthetic capture target";
                    Thread.Sleep(200);

                    var item = list.Items.FirstOrDefault(candidate =>
                        candidate.Name.IndexOf("RScreenRec synthetic capture target", StringComparison.OrdinalIgnoreCase) >= 0);
                    if (item == null)
                    {
                        string[] itemNames = list.Items.Select(candidate => candidate.Name).ToArray();
                        throw new InvalidOperationException("The synthetic target was not listed by the picker. Items: " +
                            string.Join(" | ", itemNames));
                    }
                    item.Select();
                    Thread.Sleep(100);
                    var selectedItem = list.Items.FirstOrDefault(candidate => candidate.IsSelected);
                    if (selectedItem == null || !selectedItem.Name.Equals(item.Name, StringComparison.Ordinal))
                        throw new InvalidOperationException("FlaUI did not select the filtered synthetic target. Selected item: " +
                            (selectedItem == null ? "<none>" : selectedItem.Name));

                    FlaButton selectButton = picker.FindFirstDescendant(cf => cf
                        .ByName("Select")
                        .And(cf.ByControlType(ControlType.Button)))?.AsButton();
                    if (selectButton == null)
                        throw new InvalidOperationException("FlaUI could not find the picker Select button.");
                    selectButton.Invoke();
                }
            }
            catch (Exception ex)
            {
                automationFailure = ex;
                try
                {
                    FlaButton cancelButton = picker?.FindFirstDescendant(cf => cf
                        .ByName("Cancel")
                        .And(cf.ByControlType(ControlType.Button)))?.AsButton();
                    if (cancelButton != null) cancelButton.Invoke();
                }
                catch { }
            }
        });
        automationThread.IsBackground = true;
        automationThread.SetApartmentState(ApartmentState.STA);
        automationThread.Start();

        IntPtr selectedWindow;
        try
        {
            selectedWindow = (IntPtr)pickerMethod.Invoke(null, null);
        }
        catch (TargetInvocationException ex)
        {
            throw ex.InnerException ?? ex;
        }
        if (!automationThread.Join(TimeSpan.FromSeconds(15)))
            throw new TimeoutException("FlaUI did not complete window selection within 15 seconds.");

        if (automationFailure != null)
            throw automationFailure;
        if (selectedWindow != expectedWindow)
            throw new InvalidOperationException("The picker returned " + selectedWindow + ", expected " + expectedWindow + ".");
        Console.WriteLine("picker_selection=synthetic target");
        return selectedWindow;
    }

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length < 4 || args.Length > 5 ||
            (args.Length == 5 &&
                !string.Equals(args[4], "close", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(args[4], "pause", StringComparison.OrdinalIgnoreCase)))
        {
            Console.Error.WriteLine("Usage: WindowCaptureSmoke <output.mp4> <width> <height> <duration-seconds> [close|pause]");
            return 2;
        }

        string path = Path.GetFullPath(args[0]);
        int width = int.Parse(args[1]);
        int height = int.Parse(args[2]);
        int durationSeconds = int.Parse(args[3]);
        bool closeTargetScenario = args.Length == 5 && string.Equals(args[4], "close", StringComparison.OrdinalIgnoreCase);
        bool pauseScenario = args.Length == 5 && string.Equals(args[4], "pause", StringComparison.OrdinalIgnoreCase);
        Directory.CreateDirectory(Path.GetDirectoryName(path));

        try { SetProcessDpiAwarenessContext(new IntPtr(-4)); }
        catch (EntryPointNotFoundException) { }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.ThreadException += (sender, eventArgs) =>
            Console.Error.WriteLine("ui-thread-exception=" + eventArgs.Exception);

        Process targetProcess = null;
        Form occluder = null;
        try
        {
            string targetPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SyntheticCaptureTarget.exe");
            if (!File.Exists(targetPath))
                throw new FileNotFoundException("The separate synthetic window process was not compiled.", targetPath);
            targetProcess = Process.Start(new ProcessStartInfo(targetPath, width + " " + height)
            {
                UseShellExecute = false
            });
            if (targetProcess == null || !targetProcess.WaitForInputIdle(10000))
                throw new TimeoutException("The synthetic target process did not become idle.");

            IntPtr targetWindow = IntPtr.Zero;
            DateTime targetDeadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < targetDeadline && !targetProcess.HasExited)
            {
                targetProcess.Refresh();
                targetWindow = targetProcess.MainWindowHandle;
                if (targetWindow != IntPtr.Zero)
                    break;
                System.Threading.Thread.Sleep(50);
            }
            if (targetWindow == IntPtr.Zero)
                throw new TimeoutException("The synthetic target window handle was not available.");

            var recorder = new ScreenRecorder();
            int ticks = 0;
            bool recordingStarted = false;
            bool recordingStopped = false;
            bool targetCloseRequested = false;
            bool sourceCloseReported = false;
            bool pausedOnce = false;
            bool resumedOnce = false;
            Exception failure = null;
            Stopwatch duration = new Stopwatch();
            IntPtr selectedWindow = SelectTestWindow(targetWindow);
            recorder.StartWindowRecording(selectedWindow, path);
            recordingStarted = true;
            Console.WriteLine("backend=" + recorder.WindowCaptureBackend);
            duration.Start();

            using (var timer = new System.Windows.Forms.Timer { Interval = 100 })
            {
                timer.Tick += (sender, eventArgs) =>
                {
                    try
                    {
                        ticks++;
                        if (pauseScenario && !pausedOnce && duration.Elapsed >= TimeSpan.FromSeconds(1))
                        {
                            if (!recorder.TogglePause() || !recorder.IsPaused)
                                throw new InvalidOperationException("Pause did not become active.");
                            pausedOnce = true;
                        }
                        if (pauseScenario && pausedOnce && !resumedOnce &&
                            duration.Elapsed >= TimeSpan.FromSeconds(2))
                        {
                            if (recorder.TogglePause() || recorder.IsPaused)
                                throw new InvalidOperationException("Resume did not clear the pause state.");
                            resumedOnce = true;
                        }
                        if (ticks == 8)
                        {
                            NativeRect bounds;
                            if (!GetWindowRect(targetWindow, out bounds))
                                throw new InvalidOperationException("Could not read the synthetic target bounds.");
                            occluder = new Form
                            {
                                FormBorderStyle = FormBorderStyle.None,
                                StartPosition = FormStartPosition.Manual,
                                Bounds = Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom),
                                TopMost = true,
                                ShowInTaskbar = false,
                                BackColor = Color.Magenta
                            };
                            occluder.Show();
                        }
                        if (ticks == 18 && occluder != null)
                            occluder.Hide();
                        if (ticks == 19 && !MoveWindow(targetWindow, 260, 160, width + 100, height + 60, true))
                            throw new InvalidOperationException("Could not resize and move the synthetic target.");
                        if (ticks == 24)
                            ShowWindow(targetWindow, 6); // SW_MINIMIZE
                        if (ticks == 34)
                            ShowWindow(targetWindow, 9); // SW_RESTORE

                        if (closeTargetScenario && !targetCloseRequested &&
                            duration.Elapsed >= TimeSpan.FromSeconds(durationSeconds))
                        {
                            targetCloseRequested = true;
                            if (!targetProcess.CloseMainWindow())
                                throw new InvalidOperationException("Could not close the synthetic target window.");
                            if (!targetProcess.WaitForExit(3000))
                                throw new TimeoutException("The synthetic target process did not exit after close.");
                        }

                        if (closeTargetScenario && targetCloseRequested && !recorder.IsRecording)
                        {
                            timer.Stop();
                            try
                            {
                                recorder.StopRecording();
                                throw new InvalidOperationException("The capture source closed without reporting an error.");
                            }
                            catch (InvalidOperationException ex)
                            {
                                if (ex.InnerException == null ||
                                    ex.InnerException.Message.IndexOf("selected window has been closed", StringComparison.OrdinalIgnoreCase) < 0)
                                    throw;
                                sourceCloseReported = true;
                                recordingStopped = true;
                                Console.WriteLine("source_close=expected; output_finalized=true");
                            }
                            Application.ExitThread();
                        }

                        if (!closeTargetScenario && duration.Elapsed >= TimeSpan.FromSeconds(durationSeconds))
                        {
                            timer.Stop();
                            recorder.StopRecording();
                            recordingStopped = true;
                            Console.WriteLine("stop=ok timerTicks=" + ticks);
                            Application.ExitThread();
                        }
                    }
                    catch (Exception ex)
                    {
                        failure = ex;
                        Console.Error.WriteLine(ex);
                        timer.Stop();
                        Application.ExitThread();
                    }
                };
                timer.Start();
                Application.Run();
            }

            if (recordingStarted && !recordingStopped)
            {
                try { recorder.StopRecording(); recordingStopped = true; }
                catch (Exception ex) { failure = failure ?? ex; Console.Error.WriteLine(ex); }
            }
            if (failure != null)
                return 3;
            if (closeTargetScenario && !sourceCloseReported)
                return 6;
            if (pauseScenario && !(pausedOnce && resumedOnce))
                return 7;
            return recordingStarted && recordingStopped && File.Exists(path) && new FileInfo(path).Length > 0 ? 0 : 4;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 5;
        }
        finally
        {
            if (occluder != null)
                occluder.Dispose();
            if (targetProcess != null)
            {
                try
                {
                    if (!targetProcess.HasExited)
                    {
                        targetProcess.CloseMainWindow();
                        if (!targetProcess.WaitForExit(3000))
                            targetProcess.Kill();
                    }
                }
                catch { }
                targetProcess.Dispose();
            }
        }
    }
}
