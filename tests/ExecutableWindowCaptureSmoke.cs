using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Linq;
using FlaUI.Core.Input;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using FlaButton = FlaUI.Core.AutomationElements.Button;
using FlaListBox = FlaUI.Core.AutomationElements.ListBox;
using FlaTextBox = FlaUI.Core.AutomationElements.TextBox;

internal static class ExecutableWindowCaptureSmoke
{
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkR = 0x52;
    private const int VkP = 0x50;
    private const int VkF9 = 0x78;
    private const int VkF10 = 0x79;
    private const uint KeyUp = 0x0002;
    private const int GwChild = 5;
    private const int GwNext = 2;
    private const int LbGetText = 0x0189;
    private const int LbGetTextLength = 0x018A;
    private const int LbGetCount = 0x018B;
    private const int LbSetCurSel = 0x0186;
    private const int BmClick = 0x00F5;
    private const int WmClose = 0x0010;
    private const int SwMinimize = 6;
    private const int SwRestore = 9;

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);
    [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint code, uint mapType);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr FindWindow(string className, string windowName);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, int command);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr window, StringBuilder className, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int maxCount);

    [DllImport("user32.dll", EntryPoint = "SendMessage", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessageInt(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "SendMessage", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessageText(IntPtr window, int message, IntPtr wParam, StringBuilder text);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowDisplayAffinity(IntPtr window, out uint affinity);

    [DllImport("user32.dll")]
    private static extern bool MoveWindow(IntPtr window, int x, int y, int width, int height, bool repaint);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr window);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--installed-toggle")
            return RunInstalledToggleSmoke(Path.GetFullPath(args[1]));
        if (args.Length != 4)
        {
            Console.Error.WriteLine("Usage: ExecutableWindowCaptureSmoke <RBG - RScreenRec.exe> <output-directory> <width> <height>");
            return 2;
        }

        string appPath = Path.GetFullPath(args[0]);
        string outputDirectory = Path.GetFullPath(args[1]);
        int width = int.Parse(args[2]);
        int height = int.Parse(args[3]);
        Process targetProcess = null;
        Process appProcess = null;
        Scenario scenario = null;
        bool shiftDown = false;
        int result = 1;

        try
        {
            string tempRoot = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!outputDirectory.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The executable smoke output must stay inside its dedicated harness folder.");
            Directory.CreateDirectory(outputDirectory);
            if (Directory.GetFiles(outputDirectory, "*.mp4").Length != 0)
                throw new InvalidOperationException("The executable smoke output folder must be empty.");

            try
            {
                using (Mutex.OpenExisting(@"Local\RScreenRec_SingleInstance"))
                    throw new InvalidOperationException("RScreenRec is already running; the smoke test will not signal or stop the user's recording.");
            }
            catch (WaitHandleCannotBeOpenedException)
            {
            }

            try { SetProcessDpiAwarenessContext(new IntPtr(-4)); }
            catch (EntryPointNotFoundException) { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string targetPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SyntheticCaptureTarget.exe");
            if (!File.Exists(targetPath))
                throw new FileNotFoundException("The synthetic capture target was not compiled.", targetPath);
            targetProcess = Process.Start(new ProcessStartInfo(targetPath, width + " " + height) { UseShellExecute = false });
            if (targetProcess == null || !targetProcess.WaitForInputIdle(10000))
                throw new TimeoutException("The synthetic target did not become idle.");

            IntPtr targetWindow = IntPtr.Zero;
            Stopwatch targetDeadline = Stopwatch.StartNew();
            while (targetDeadline.Elapsed < TimeSpan.FromSeconds(10) && !targetProcess.HasExited)
            {
                targetProcess.Refresh();
                targetWindow = targetProcess.MainWindowHandle;
                if (targetWindow != IntPtr.Zero)
                    break;
                System.Threading.Thread.Sleep(50);
            }
            if (targetWindow == IntPtr.Zero)
                throw new TimeoutException("The synthetic target window handle was unavailable.");

            keybd_event(VkShift, 0x2A, 0, UIntPtr.Zero);
            shiftDown = true;
            Stopwatch shiftDeadline = Stopwatch.StartNew();
            while ((GetAsyncKeyState(VkShift) & 0x8000) == 0 && shiftDeadline.Elapsed < TimeSpan.FromSeconds(1))
                System.Threading.Thread.Sleep(10);
            if ((GetAsyncKeyState(VkShift) & 0x8000) == 0)
                throw new InvalidOperationException("Windows did not report the injected Shift key as held.");

            var startInfo = new ProcessStartInfo(appPath)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(appPath)
            };
            startInfo.EnvironmentVariables["RSCREENREC_BENCHMARK_OUTPUT_DIRECTORY"] = outputDirectory;
            appProcess = Process.Start(startInfo);
            if (appProcess == null)
                throw new InvalidOperationException("The actual RScreenRec executable did not start.");

            Console.WriteLine("actual_exe_started=true");
            scenario = new Scenario(appPath, outputDirectory, targetWindow, appProcess);
            result = scenario.Run();
            if (result == 0)
                RunFullScreenOverlaySmoke(appPath, outputDirectory);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            result = 1;
        }
        finally
        {
            if (shiftDown)
                keybd_event(VkShift, 0x2A, KeyUp, UIntPtr.Zero);

            if (scenario != null)
                scenario.Dispose();

            StopAppSafely(appPath, appProcess);
            CloseTarget(targetProcess);

            if (result != 0 && Directory.Exists(outputDirectory))
            {
                foreach (string video in Directory.GetFiles(outputDirectory, "*.mp4"))
                {
                    try { File.Delete(video); }
                    catch (Exception ex) { Console.Error.WriteLine("Could not remove failed smoke output: " + ex.Message); }
                }
            }
        }

        return result;
    }

    private static int RunInstalledToggleSmoke(string appPath)
    {
        string outputDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "installed-toggle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDirectory);
        Process app = null;
        Form surface = null;
        Form secondSurface = null;
        string video = null;
        DateTime started = DateTime.Now;
        try
        {
            try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch (EntryPointNotFoundException) { }
            Application.EnableVisualStyles();
            try
            {
                using (Mutex.OpenExisting(@"Local\RScreenRec_SingleInstance"))
                    throw new InvalidOperationException("A recorder is already active; installed-hotkey acceptance will not touch it.");
            }
            catch (WaitHandleCannotBeOpenedException) { }
            string linkPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                "RBG Utilities", "RBG RScreenRec - Screen recording.lnk");
            object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            object link = null;
            try
            {
                link = ((dynamic)shell).CreateShortcut(linkPath);
                dynamic shortcut = link;
                string key = (string)shortcut.Hotkey;
                if (!string.Equals(Path.GetFullPath((string)shortcut.TargetPath), appPath, StringComparison.OrdinalIgnoreCase) ||
                    (string)shortcut.Arguments != "--record-screen" ||
                    key.Replace(" ", "").ToUpperInvariant() != "ALT+CTRL+R")
                    throw new InvalidOperationException("The installed Ctrl+Alt+R shortcut does not target this candidate.");
            }
            finally
            {
                if (link != null) Marshal.ReleaseComObject(link);
                Marshal.ReleaseComObject(shell);
            }
            Screen screen = Screen.FromPoint(Cursor.Position);
            surface = new Form
            {
                FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual,
                Bounds = new Rectangle(screen.Bounds.Left, screen.Bounds.Top, screen.Bounds.Width / 2 + 1, screen.Bounds.Height),
                BackColor = Color.Cyan, TopMost = true, ShowInTaskbar = false
            };
            secondSurface = new Form
            {
                FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual,
                Bounds = new Rectangle(screen.Bounds.Left + screen.Bounds.Width / 2, screen.Bounds.Top,
                    screen.Bounds.Width - screen.Bounds.Width / 2, screen.Bounds.Height),
                BackColor = Color.Cyan, TopMost = true, ShowInTaskbar = false
            };
            surface.Show();
            secondSurface.Show();
            surface.BringToFront();
            Application.DoEvents();
            Thread.Sleep(200);
            for (int cycle = 1; cycle <= 2; cycle++)
            {
                started = DateTime.Now;
                long offset = GetLogLength();
                PressGlobalHotkey(VkR);
                DateTime deadline = DateTime.UtcNow.AddSeconds(15);
                while (app == null && DateTime.UtcNow < deadline)
                {
                    foreach (Process candidate in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(appPath)))
                    {
                        if (app == null && candidate.StartTime >= started.AddSeconds(-1))
                            app = candidate;
                        else candidate.Dispose();
                    }
                    Application.DoEvents();
                    if (app == null) Thread.Sleep(50);
                }
                if (app == null) throw new TimeoutException("Ctrl+Alt+R did not start the app from idle through the installed shortcut.");
                if (!string.Equals(app.MainModule.FileName, appPath, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Explorer launched a different executable: " + app.MainModule.FileName);
                if (!WaitForLogMessage("Active recording hotkeys ready.", offset, TimeSpan.FromSeconds(10)))
                    throw new TimeoutException("The keyboard-launched recorder did not become ready.");
                string startup = ReadLogTail(offset).Split('\n').LastOrDefault(line =>
                    line.Contains("Startup ready in ") && line.Contains("Output="));
                if (startup == null) throw new InvalidOperationException("No output path was logged for the keyboard-launched recorder.");
                video = startup.Substring(startup.IndexOf("Output=", StringComparison.Ordinal) + 7).Trim();
                Console.WriteLine("idle_ctrl_alt_r=started; cycle=" + cycle);
                if (cycle == 1)
                {
                    long pauseOffset = GetLogLength();
                    PressGlobalHotkey(VkP);
                    if (!WaitForLogMessage("Recording paused.", pauseOffset, TimeSpan.FromSeconds(5)))
                        throw new TimeoutException("Ctrl+Alt+P did not pause the keyboard-launched recorder.");
                    VerifyPauseFeedback(app, outputDirectory, true);
                    pauseOffset = GetLogLength();
                    PressGlobalHotkey(VkP);
                    if (!WaitForLogMessage("Recording resumed.", pauseOffset, TimeSpan.FromSeconds(5)))
                        throw new TimeoutException("Ctrl+Alt+P did not resume the keyboard-launched recorder.");
                    VerifyPauseFeedback(app, outputDirectory, false);
                }
                Thread.Sleep(700);
                StopRecording(appPath, app);
                app.Dispose();
                app = null;
                MoveInstalledSmokeVideo(video, outputDirectory, cycle, started);
                video = null;
                Thread.Sleep(300);
            }
            Console.WriteLine("installed_toggle=PASS; idle start, pause/resume, stop, restart, stop");
            Console.WriteLine("installed_toggle_artifacts=" + outputDirectory);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            StopAppSafely(appPath, app);
            if (video != null)
            {
                try { MoveInstalledSmokeVideo(video, outputDirectory, 0, started); }
                catch (Exception ex) { Console.Error.WriteLine("Test video retained at " + video + ": " + ex.Message); }
            }
            if (surface != null) surface.Dispose();
            if (secondSurface != null) secondSurface.Dispose();
        }
    }

    private static string ReadLogTail(long offset)
    {
        string path = Path.Combine(Path.GetTempPath(), "screenrec.log");
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            stream.Position = Math.Min(offset, stream.Length);
            using (var reader = new StreamReader(stream)) return reader.ReadToEnd();
        }
    }

    private static void MoveInstalledSmokeVideo(string path, string outputDirectory, int cycle, DateTime started)
    {
        string source = Path.GetFullPath(path);
        string captures = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Captures")) + Path.DirectorySeparatorChar;
        if (!source.StartsWith(captures, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(source).StartsWith("rec_", StringComparison.OrdinalIgnoreCase) ||
            !source.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing to move an output outside the recorder's capture directory.");
        FileInfo file = new FileInfo(source);
        if (!file.Exists || file.CreationTime < started.AddSeconds(-1))
            throw new InvalidOperationException("Refusing to move an output that was not created by this test.");
        File.Move(source, Path.Combine(outputDirectory, "cycle-" + cycle + ".mp4"));
    }

    private static void RunFullScreenOverlaySmoke(string appPath, string parentOutputDirectory)
    {
        string outputDirectory = Path.Combine(parentOutputDirectory, "full-screen-overlay");
        Directory.CreateDirectory(outputDirectory);
        Screen screen = Screen.FromPoint(Cursor.Position);
        string positionPath = Path.Combine(outputDirectory, "overlay-position.txt");
        Form surface = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            Bounds = screen.Bounds,
            TopMost = true,
            ShowInTaskbar = false,
            BackColor = Color.Cyan
        };
        Process firstRecording = null;
        Process secondRecording = null;
        try
        {
            surface.Show();
            surface.BringToFront();
            Application.DoEvents();
            Thread.Sleep(250);

            WriteShortcutProfile(appPath, outputDirectory, "Ctrl+Alt+R", "Ctrl+Alt+P");
            firstRecording = StartFullScreenRecording(appPath, outputDirectory);
            bool fullscreenWindow = false;
            EnumWindows((window, parameter) =>
            {
                uint owner;
                NativeRect rect;
                GetWindowThreadProcessId(window, out owner);
                if (owner == (uint)firstRecording.Id && IsWindowVisible(window) && GetWindowRect(window, out rect) &&
                    rect.Left <= screen.Bounds.Left && rect.Top <= screen.Bounds.Top &&
                    rect.Right >= screen.Bounds.Right && rect.Bottom >= screen.Bounds.Bottom)
                {
                    fullscreenWindow = true;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            if (fullscreenWindow)
                throw new InvalidOperationException("The recorder still has a fullscreen top-level window that can suppress Explorer shortcut keys.");
            Console.WriteLine("recorder_fullscreen_window=absent");
            string firstVideo = WaitForVideo(outputDirectory, firstRecording, TimeSpan.FromSeconds(10));
            long pauseLogOffset = GetLogLength();
            PressGlobalHotkey(VkP);
            if (!WaitForLogMessage("Recording paused.", pauseLogOffset, TimeSpan.FromSeconds(5)))
                throw new TimeoutException("Ctrl+Alt+P did not pause a full-screen recording.");
            VerifyPauseFeedback(firstRecording, outputDirectory, true);
            pauseLogOffset = GetLogLength();
            PressGlobalHotkey(VkP);
            if (!WaitForLogMessage("Recording resumed.", pauseLogOffset, TimeSpan.FromSeconds(5)))
                throw new TimeoutException("Ctrl+Alt+P did not resume a full-screen recording.");
            VerifyPauseFeedback(firstRecording, outputDirectory, false);
            Console.WriteLine("keyboard_ctrl_alt_p=paused_and_resumed");

            NativeRect initialBounds;
            IntPtr overlay = WaitForRecordingOverlay(firstRecording, out initialBounds, TimeSpan.FromSeconds(10));
            if (overlay == IntPtr.Zero)
                throw new TimeoutException("The actual full-screen recording did not show the REC overlay.");

            uint affinity;
            bool exclusionEnabled = GetWindowDisplayAffinity(overlay, out affinity) && affinity == 0x11;
            Console.WriteLine("full_screen_overlay=visible");
            Console.WriteLine("overlay_exclusion=" + (exclusionEnabled ? "enabled" : "unavailable"));

            Rectangle workingArea = screen.WorkingArea;
            int deltaX = -Math.Min(180, Math.Max(40, initialBounds.Left - workingArea.Left));
            int deltaY = -Math.Min(120, Math.Max(40, initialBounds.Top - workingArea.Top));
            Point start = new Point(initialBounds.Left + (initialBounds.Right - initialBounds.Left) / 2,
                initialBounds.Top + (initialBounds.Bottom - initialBounds.Top) / 2);
            Point destination = new Point(start.X + deltaX, start.Y + deltaY);
            Mouse.MoveTo(start);
            Thread.Sleep(100);
            Mouse.Down(MouseButton.Left);
            Mouse.MoveTo(destination);
            Thread.Sleep(120);
            Mouse.Up(MouseButton.Left);
            Thread.Sleep(250);

            NativeRect movedBounds;
            overlay = FindRecordingOverlay(firstRecording.Id, out movedBounds);
            if (overlay == IntPtr.Zero || movedBounds.Left == initialBounds.Left && movedBounds.Top == initialBounds.Top)
                throw new InvalidOperationException("FlaUI could not drag the actual REC overlay.");
            if (!File.Exists(positionPath))
                throw new InvalidOperationException("Dragging the REC overlay did not persist its isolated position.");

            string[] saved = File.ReadAllText(positionPath).Split(',');
            int savedX, savedY;
            if (saved.Length != 2 || !int.TryParse(saved[0], out savedX) || !int.TryParse(saved[1], out savedY) ||
                savedX != movedBounds.Left || savedY != movedBounds.Top)
                throw new InvalidOperationException("The persisted REC overlay position did not match its dragged position.");

            WriteCaptureProbe(outputDirectory, screen.Bounds, movedBounds, exclusionEnabled);
            Thread.Sleep(1700);
            StopRecording(appPath, firstRecording);
            firstRecording.Dispose();
            firstRecording = null;
            if (!File.Exists(firstVideo) || new FileInfo(firstVideo).Length == 0)
                throw new InvalidOperationException("The actual full-screen recording did not finalize a video.");

            WriteShortcutProfile(appPath, outputDirectory, "Ctrl+Alt+F9", "Ctrl+Alt+F10");
            secondRecording = StartFullScreenRecording(appPath, outputDirectory);
            WaitForVideo(outputDirectory, secondRecording, TimeSpan.FromSeconds(10));
            NativeRect reopenedBounds;
            overlay = WaitForRecordingOverlay(secondRecording, out reopenedBounds, TimeSpan.FromSeconds(10));
            if (overlay == IntPtr.Zero || reopenedBounds.Left != savedX || reopenedBounds.Top != savedY)
                throw new InvalidOperationException("The REC overlay did not reload its position on the next app launch.");
            pauseLogOffset = GetLogLength();
            PressGlobalHotkey(VkF10);
            if (!WaitForLogMessage("Recording paused.", pauseLogOffset, TimeSpan.FromSeconds(5)))
                throw new TimeoutException("The customized pause shortcut was not honored.");
            pauseLogOffset = GetLogLength();
            PressGlobalHotkey(VkF10);
            if (!WaitForLogMessage("Recording resumed.", pauseLogOffset, TimeSpan.FromSeconds(5)))
                throw new TimeoutException("The customized resume shortcut was not honored.");
            StopRecording(appPath, secondRecording, VkF9);
            Console.WriteLine("keyboard_custom=paused_resumed_stopped");
            secondRecording.Dispose();
            secondRecording = null;
            Console.WriteLine("overlay_drag=persisted");
            Console.WriteLine("overlay_position_reloaded=true");
            Console.WriteLine("screen_recording_pause_resume=ok");
            Console.WriteLine("full_screen_recording=finalized");
            Console.WriteLine("full_screen_output=" + firstVideo);
        }
        finally
        {
            StopRecordingSafely(appPath, firstRecording);
            StopRecordingSafely(appPath, secondRecording);
            surface.Dispose();
            string profile = Path.Combine(outputDirectory, "shortcuts");
            if (Directory.Exists(profile))
                foreach (string link in Directory.GetFiles(profile, "*.lnk", SearchOption.AllDirectories))
                    File.Move(link, link + ".test-artifact");
        }
    }

    private static Process StartFullScreenRecording(string appPath, string outputDirectory)
    {
        long logOffset = GetLogLength();
        var start = new ProcessStartInfo(appPath, "--record-screen")
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(appPath)
        };
        start.EnvironmentVariables["RSCREENREC_BENCHMARK_OUTPUT_DIRECTORY"] = outputDirectory;
        start.EnvironmentVariables["RBG_VISUAL_SETTINGS_TEST_ROOT"] = outputDirectory;
        start.EnvironmentVariables["RBG_OVERLAY_POSITION_TEST_ROOT"] = outputDirectory;
        start.EnvironmentVariables["RBG_SHORTCUT_TEST_ROOT"] = Path.Combine(outputDirectory, "shortcuts");
        Process app = Process.Start(start);
        if (!WaitForLogMessage("Active recording hotkeys ready.", logOffset, TimeSpan.FromSeconds(5)))
        {
            StopRecordingSafely(appPath, app);
            throw new TimeoutException("The active recording hotkey listener did not start.");
        }
        return app;
    }

    private static void WriteShortcutProfile(string appPath, string outputDirectory, string stopHotkey, string pauseHotkey)
    {
        string folder = Path.Combine(outputDirectory, "shortcuts", "RBG Utilities");
        Directory.CreateDirectory(folder);
        object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
        try
        {
            string[] modes = { "Screen recording", "Window recording", "Pause - resume" };
            string[] hotkeys = { stopHotkey, "Ctrl+Alt+W", pauseHotkey };
            for (int i = 0; i < modes.Length; i++)
            {
                object shortcut = ((dynamic)shell).CreateShortcut(Path.Combine(folder, "RBG RScreenRec - " + modes[i] + ".lnk"));
                try
                {
                    dynamic link = shortcut;
                    link.TargetPath = appPath;
                    link.Hotkey = hotkeys[i];
                    link.Save();
                }
                finally { Marshal.ReleaseComObject(shortcut); }
            }
        }
        finally { Marshal.ReleaseComObject(shell); }
    }

    private static void PressGlobalHotkey(int key)
    {
        keybd_event(VkControl, 0x1D, 0, UIntPtr.Zero);
        keybd_event(VkMenu, 0x38, 0, UIntPtr.Zero);
        byte scanCode = (byte)MapVirtualKey((uint)key, 0);
        keybd_event((byte)key, scanCode, 0, UIntPtr.Zero);
        Thread.Sleep(100);
        keybd_event((byte)key, scanCode, KeyUp, UIntPtr.Zero);
        keybd_event(VkMenu, 0x38, KeyUp, UIntPtr.Zero);
        keybd_event(VkControl, 0x1D, KeyUp, UIntPtr.Zero);
        Thread.Sleep(150);
    }

    private static void VerifyPickerLayout(IntPtr picker, AutomationElement dialog, FlaListBox list, string outputDirectory)
    {
        IntPtr listHandle = list.Properties.NativeWindowHandle.Value;
        IntPtr listFont = SendMessageInt(listHandle, 0x31, IntPtr.Zero, IntPtr.Zero);
        using (Font font = Font.FromHfont(listFont))
        using (Graphics graphics = Graphics.FromHwnd(listHandle))
        {
            int row = SendMessageInt(listHandle, 0x1A1, IntPtr.Zero, IntPtr.Zero).ToInt32();
            if (row < Math.Ceiling(font.GetHeight(graphics)))
                throw new InvalidOperationException("Picker rows are shorter than the rendered font; text would overlap.");
        }
        NativeRect listRect, pickerRect;
        GetWindowRect(listHandle, out listRect);
        GetWindowRect(picker, out pickerRect);
        foreach (AutomationElement button in dialog.FindAllDescendants(cf => cf.ByControlType(ControlType.Button)))
        {
            if (button.Name != "Select" && button.Name != "Cancel" && button.Name != "Click a window...") continue;
            Rectangle bounds = button.BoundingRectangle;
            using (Font font = Font.FromHfont(listFont))
            using (Graphics graphics = Graphics.FromHwnd(listHandle))
            {
                Size text = TextRenderer.MeasureText(graphics, button.Name, font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                if (text.Height > bounds.Height || text.Width > bounds.Width || bounds.Top < listRect.Bottom || bounds.Bottom > pickerRect.Bottom)
                    throw new InvalidOperationException("Picker button is clipped or overlaps the window list: " + button.Name);
            }
        }
        using (var bitmap = new Bitmap(pickerRect.Right - pickerRect.Left, pickerRect.Bottom - pickerRect.Top))
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            IntPtr dc = graphics.GetHdc();
            try { if (!PrintWindow(picker, dc, 0)) throw new InvalidOperationException("Could not capture the picker layout."); }
            finally { graphics.ReleaseHdc(dc); }
            bitmap.Save(Path.Combine(outputDirectory, "picker-layout.png"));
        }
        Console.WriteLine("picker_layout=readable");
    }

    private static void VerifyPauseFeedback(Process app, string outputDirectory, bool paused)
    {
        string expected = paused ? "Recording paused" : "Recording";
        DateTime deadline = DateTime.UtcNow.AddSeconds(3);
        using (var automation = new UIA3Automation())
        {
            do
            {
                var badge = automation.GetDesktop().FindFirstChild(cf => cf.ByProcessId(app.Id).And(cf.ByName(expected)));
                if (badge != null && !badge.Properties.IsOffscreen.Value)
                {
                    File.WriteAllText(Path.Combine(outputDirectory, paused ? "badge-paused.txt" : "badge-resumed.txt"),
                        expected + "; bounds=" + badge.BoundingRectangle);
                    Console.WriteLine(paused ? "pause_feedback=paused_label" : "resume_feedback=recording_label");
                    return;
                }
                Thread.Sleep(50);
            } while (DateTime.UtcNow < deadline);
        }
        throw new InvalidOperationException("The visible REC badge did not expose its expected pause/resume state.");
    }

    private static long GetLogLength()
    {
        string path = Path.Combine(Path.GetTempPath(), "screenrec.log");
        return File.Exists(path) ? new FileInfo(path).Length : 0;
    }

    private static bool WaitForLogMessage(string message, long offset, TimeSpan timeout)
    {
        string path = Path.Combine(Path.GetTempPath(), "screenrec.log");
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(path))
            {
                try
                {
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete))
                    {
                        if (stream.Length < offset) offset = 0;
                        stream.Position = offset;
                        using (var reader = new StreamReader(stream))
                            if (reader.ReadToEnd().IndexOf(message, StringComparison.OrdinalIgnoreCase) >= 0)
                                return true;
                    }
                }
                catch (IOException) { }
            }
            Thread.Sleep(50);
        }
        return false;
    }

    private static string WaitForVideo(string outputDirectory, Process app, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline && !app.HasExited)
        {
            string[] videos = Directory.GetFiles(outputDirectory, "rec_*.mp4");
            if (videos.Length > 0)
            {
                Array.Sort(videos, StringComparer.OrdinalIgnoreCase);
                string latest = videos[videos.Length - 1];
                if (new FileInfo(latest).Length > 0)
                    return latest;
            }
            Application.DoEvents();
            Thread.Sleep(50);
        }
        throw new TimeoutException("The actual full-screen recording did not create an MP4.");
    }

    private static IntPtr WaitForRecordingOverlay(Process app, out NativeRect bounds, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        NativeRect lastBounds = new NativeRect();
        while (DateTime.UtcNow < deadline && !app.HasExited)
        {
            IntPtr overlay = FindRecordingOverlay(app.Id, out lastBounds);
            if (overlay != IntPtr.Zero)
            {
                bounds = lastBounds;
                return overlay;
            }
            Application.DoEvents();
            Thread.Sleep(50);
        }
        bounds = lastBounds;
        return IntPtr.Zero;
    }

    private static IntPtr FindRecordingOverlay(int processId, out NativeRect bounds)
    {
        IntPtr found = IntPtr.Zero;
        NativeRect foundBounds = new NativeRect();
        EnumWindows((window, parameter) =>
        {
            uint ownerProcessId;
            if (GetWindowThreadProcessId(window, out ownerProcessId) != 0 &&
                ownerProcessId == (uint)processId && IsWindowVisible(window))
            {
                NativeRect candidate;
                if (GetWindowRect(window, out candidate))
                {
                    int width = candidate.Right - candidate.Left;
                    int height = candidate.Bottom - candidate.Top;
                    if (width >= 50 && width <= 300 && height >= 15 && height <= 100)
                    {
                        found = window;
                        foundBounds = candidate;
                        return false;
                    }
                }
            }
            return true;
        }, IntPtr.Zero);
        bounds = foundBounds;
        return found;
    }

    private static void WriteCaptureProbe(string outputDirectory, Rectangle screen, NativeRect overlay, bool exclusionEnabled)
    {
        File.WriteAllText(Path.Combine(outputDirectory, "overlay-capture-probe.txt"), string.Join(",", new[]
        {
            screen.Left.ToString(), screen.Top.ToString(),
            overlay.Left.ToString(), overlay.Top.ToString(),
            (overlay.Right - overlay.Left).ToString(), (overlay.Bottom - overlay.Top).ToString(),
            exclusionEnabled ? "1" : "0"
        }));
    }

    private static void StopRecording(string appPath, Process app, int key = VkR)
    {
        if (app == null || app.HasExited) return;
        long logOffset = GetLogLength();
        PressGlobalHotkey(key);
        if (!WaitForLogMessage("Stop request detected; closing overlays immediately.", logOffset, TimeSpan.FromSeconds(5)))
            throw new TimeoutException("Ctrl+Alt+R did not stop the active screen recording.");
        if (!app.WaitForExit(10000))
            throw new TimeoutException("The full-screen recording did not finalize after Ctrl+Alt+R.");
        if (key == VkR) Console.WriteLine("keyboard_ctrl_alt_r=stopped");
    }

    private static void StopRecordingSafely(string appPath, Process app)
    {
        StopAppSafely(appPath, app);
    }

    private static void StopAppSafely(string appPath, Process appProcess)
    {
        if (appProcess == null)
            return;

        try
        {
            if (!appProcess.HasExited)
            {
                IntPtr picker = FindWindow(null, "Select window - RScreenRec");
                if (picker != IntPtr.Zero)
                {
                    uint owner;
                    GetWindowThreadProcessId(picker, out owner);
                    if (owner == (uint)appProcess.Id)
                        SendMessageInt(picker, WmClose, IntPtr.Zero, IntPtr.Zero);
                }
                if (!appProcess.HasExited)
                {
                    try { using (var stop = EventWaitHandle.OpenExisting(@"Local\RScreenRec_StopEvent")) stop.Set(); }
                    catch (WaitHandleCannotBeOpenedException) { }
                }

                if (!appProcess.WaitForExit(5000))
                {
                    appProcess.Kill();
                    appProcess.WaitForExit(3000);
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Recorder cleanup: " + ex.Message);
            try
            {
                if (!appProcess.HasExited)
                {
                    appProcess.Kill();
                    appProcess.WaitForExit(3000);
                }
            }
            catch { }
        }
        finally
        {
            appProcess.Dispose();
        }
    }

    private static void CloseTarget(Process targetProcess)
    {
        if (targetProcess == null)
            return;
        try
        {
            if (!targetProcess.HasExited)
            {
                targetProcess.CloseMainWindow();
                if (!targetProcess.WaitForExit(3000))
                {
                    targetProcess.Kill();
                    targetProcess.WaitForExit(3000);
                }
            }
        }
        catch { }
        finally
        {
            targetProcess.Dispose();
        }
    }

    private sealed class Scenario : IDisposable
    {
        private readonly string appPath;
        private readonly string outputDirectory;
        private readonly IntPtr targetWindow;
        private readonly Process appProcess;
        private readonly System.Windows.Forms.Timer timer;
        private readonly Stopwatch total = Stopwatch.StartNew();
        private Stopwatch recording;
        private Form occluder;
        private string videoPath;
        private string failure;
        private bool pickerSeen;
        private bool targetSelected;
        private bool occlusionShown;
        private bool occlusionHidden;
        private bool resized;
        private bool minimized;
        private bool restored;
        private bool recordingOverlaySeen;
        private bool pauseRequested;
        private bool resumeRequested;
        private bool stopRequested;
        private DateTime pickerSeenAt;
        private DateTime selectionAt;
        private int result = 1;

        internal Scenario(string appPath, string outputDirectory, IntPtr targetWindow, Process appProcess)
        {
            this.appPath = appPath;
            this.outputDirectory = outputDirectory;
            this.targetWindow = targetWindow;
            this.appProcess = appProcess;
            timer = new System.Windows.Forms.Timer { Interval = 50 };
            timer.Tick += OnTick;
        }

        internal int Run()
        {
            timer.Start();
            Application.Run(new ApplicationContext());
            timer.Stop();
            if (failure != null)
                Console.Error.WriteLine(failure);
            return result;
        }

        private void OnTick(object sender, EventArgs args)
        {
            try
            {
                if (appProcess.HasExited && !stopRequested)
                    throw new InvalidOperationException("The actual RScreenRec process exited before the smoke test requested stop.");

                IntPtr compatibilityDialog = FindWindow(null, "RScreenRec - Compatibility capture");
                if (compatibilityDialog != IntPtr.Zero)
                {
                    SendMessageInt(compatibilityDialog, WmClose, IntPtr.Zero, IntPtr.Zero);
                    throw new InvalidOperationException("The actual executable selected compatibility capture instead of Windows Graphics Capture.");
                }

                IntPtr picker = FindWindow(null, "Select window - RScreenRec");
                if (picker != IntPtr.Zero)
                {
                    uint pickerProcessId;
                    GetWindowThreadProcessId(picker, out pickerProcessId);
                    if (pickerProcessId == (uint)appProcess.Id)
                    {
                        if (!pickerSeen)
                        {
                            if (GetForegroundWindow() != picker)
                                throw new InvalidOperationException("The actual executable's window picker was not brought to the foreground.");
                            NativeRect pickerBounds;
                            if (!GetWindowRect(picker, out pickerBounds))
                                throw new InvalidOperationException("Could not read the actual executable's window-picker bounds.");
                            Rectangle pickerWorkArea = Screen.FromHandle(picker).WorkingArea;
                            if (pickerBounds.Left < pickerWorkArea.Left || pickerBounds.Top < pickerWorkArea.Top ||
                                pickerBounds.Right > pickerWorkArea.Right || pickerBounds.Bottom > pickerWorkArea.Bottom)
                                throw new InvalidOperationException("The actual executable's window picker extends beyond the monitor's visible work area.");
                            pickerSeen = true;
                            pickerSeenAt = DateTime.UtcNow;
                            keybd_event(VkShift, 0x2A, KeyUp, UIntPtr.Zero);
                            Console.WriteLine("shift_picker=seen_with_shift_held");
                        }
                        SelectSyntheticTarget(picker);
                    }
                }

                if (!pickerSeen && total.Elapsed > TimeSpan.FromSeconds(5))
                    throw new TimeoutException("Holding Shift did not open the actual executable's window picker within five seconds.");
                if (pickerSeen && !targetSelected && DateTime.UtcNow - pickerSeenAt > TimeSpan.FromSeconds(10))
                    throw new TimeoutException("The synthetic target did not appear in the actual executable's window picker.");
                if (targetSelected && recording == null)
                {
                    string[] videos = Directory.GetFiles(outputDirectory, "rec_*.mp4");
                    if (videos.Length > 0)
                    {
                        Array.Sort(videos, StringComparer.OrdinalIgnoreCase);
                        for (int i = videos.Length - 1; i >= 0; i--)
                        {
                            var info = new FileInfo(videos[i]);
                            if (info.Length > 0)
                            {
                                videoPath = videos[i];
                                recording = Stopwatch.StartNew();
                                Console.WriteLine("recording_file_created=true");
                                break;
                            }
                        }
                    }
                    if (recording == null && DateTime.UtcNow - selectionAt > TimeSpan.FromSeconds(8))
                        throw new TimeoutException("The actual executable did not create an MP4 after selecting the synthetic window.");
                }

                if (recording != null)
                    AdvanceCaptureScenario();

                if (stopRequested)
                {
                    if (appProcess.HasExited)
                    {
                        var info = new FileInfo(videoPath);
                        if (!info.Exists || info.Length == 0)
                            throw new InvalidOperationException("The actual executable exited without finalizing a non-empty MP4.");
                        Console.WriteLine("actual_exe_stop=ok");
                        Console.WriteLine("occlusion_and_resume=ok");
                        Console.WriteLine("output=" + videoPath);
                        Console.WriteLine("recorded_seconds=" + recording.Elapsed.TotalSeconds.ToString("F2"));
                        result = 0;
                        timer.Stop();
                        Application.ExitThread();
                    }
                    else if (total.Elapsed > TimeSpan.FromSeconds(22))
                    {
                        throw new TimeoutException("The actual executable did not exit after receiving its second-launch stop request.");
                    }
                }
            }
            catch (Exception ex)
            {
                failure = ex.ToString();
                timer.Stop();
                Application.ExitThread();
            }
        }

        private void SelectSyntheticTarget(IntPtr picker)
        {
            using (var automation = new UIA3Automation())
            {
                var dialog = automation.GetDesktop().FindFirstChild(cf => cf
                    .ByName("Select window - RScreenRec")
                    .And(cf.ByControlType(ControlType.Window)));
                if (dialog == null)
                    return;

                FlaListBox list = dialog.FindFirstDescendant(cf => cf
                    .ByControlType(ControlType.List))?.AsListBox();
                FlaTextBox search = dialog.FindFirstDescendant(cf => cf
                    .ByControlType(ControlType.Edit))?.AsTextBox();
                if (list == null || search == null)
                    throw new InvalidOperationException("FlaUI could not find the window picker list or search box.");

                search.Text = "synthetic capture target";
                Thread.Sleep(200);
                VerifyPickerLayout(picker, dialog, list, outputDirectory);
                var item = list.Items.FirstOrDefault(candidate =>
                    candidate.Name.IndexOf("RScreenRec synthetic capture target", StringComparison.OrdinalIgnoreCase) >= 0);
                if (item == null)
                    throw new InvalidOperationException("FlaUI could not find the synthetic target in the filtered picker list.");
                item.Select();
                Thread.Sleep(100);
                var selected = list.Items.FirstOrDefault(candidate => candidate.IsSelected);
                if (selected == null || !selected.Name.Equals(item.Name, StringComparison.Ordinal))
                    throw new InvalidOperationException("FlaUI did not select the synthetic target in the actual executable picker.");

                FlaButton selectButton = dialog.FindFirstDescendant(cf => cf
                    .ByName("Select")
                    .And(cf.ByControlType(ControlType.Button)))?.AsButton();
                if (selectButton == null)
                    throw new InvalidOperationException("FlaUI could not find the Select button in the actual executable picker.");
                selectButton.Invoke();
                if (!targetSelected)
                {
                    targetSelected = true;
                    selectionAt = DateTime.UtcNow;
                    Console.WriteLine("picker_selection=synthetic_target");
                }
            }
        }

        private void AdvanceCaptureScenario()
        {
            double seconds = recording.Elapsed.TotalSeconds;
            if (!recordingOverlaySeen)
            {
                NativeRect overlayBounds;
                IntPtr overlay = FindRecordingOverlay(out overlayBounds);
                if (overlay != IntPtr.Zero)
                {
                    recordingOverlaySeen = true;
                    Console.WriteLine("recording_overlay=visible; size=" +
                        (overlayBounds.Right - overlayBounds.Left) + "x" + (overlayBounds.Bottom - overlayBounds.Top));
                }
                else if (seconds >= 3.0)
                {
                    throw new TimeoutException("The actual executable did not show its REC recording overlay.");
                }
            }

            if (!occlusionShown && seconds >= 0.8)
            {
                NativeRect bounds;
                if (!GetWindowRect(targetWindow, out bounds))
                    throw new InvalidOperationException("Could not read the synthetic target's bounds for occlusion.");
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
                occluder.BringToFront();
                BringWindowToTop(occluder.Handle);
                occlusionShown = true;
                Console.WriteLine("occluder=shown_at=" + seconds.ToString("F2"));
            }
            if (occlusionShown && !occlusionHidden && seconds >= 2.2)
            {
                occluder.Hide();
                occlusionHidden = true;
                Console.WriteLine("occluder=hidden_at=" + seconds.ToString("F2"));
            }
            if (occlusionHidden && !resized && seconds >= 2.5)
            {
                if (!MoveWindow(targetWindow, 260, 160, 771, 437, true))
                    throw new InvalidOperationException("Could not resize and move the synthetic target after occlusion.");
                resized = true;
            }
            if (resized && !minimized && seconds >= 3.2)
            {
                ShowWindow(targetWindow, SwMinimize);
                minimized = true;
            }
            if (!pauseRequested && seconds >= 2.8)
            {
                TogglePause();
                pauseRequested = true;
                Console.WriteLine("pause_request=sent");
            }
            if (pauseRequested && !resumeRequested && seconds >= 3.8)
            {
                TogglePause();
                resumeRequested = true;
                Console.WriteLine("resume_request=sent");
            }
            if (minimized && !restored && seconds >= 4.0)
            {
                ShowWindow(targetWindow, SwRestore);
                restored = true;
                Console.WriteLine("target=restored_after_minimize");
            }
            if (restored && !stopRequested && seconds >= 9.0)
            {
                if (!occlusionHidden || !resized || !minimized || !recordingOverlaySeen || !pauseRequested || !resumeRequested)
                    throw new InvalidOperationException("The overlay, pause/resume, occlusion, resize, minimize, and restore events did not all run.");
                keybd_event(VkControl, 0x1D, 0, UIntPtr.Zero);
                keybd_event(VkShift, 0x2A, 0, UIntPtr.Zero);
                try
                {
                    using (Process stopSignal = Process.Start(new ProcessStartInfo(appPath) { UseShellExecute = false }))
                        if (stopSignal == null || !stopSignal.WaitForExit(5000) || stopSignal.ExitCode != 0)
                            throw new InvalidOperationException("The actual executable's Ctrl+Shift second-launch stop request failed.");
                }
                finally
                {
                    keybd_event(VkShift, 0x2A, KeyUp, UIntPtr.Zero);
                    keybd_event(VkControl, 0x1D, KeyUp, UIntPtr.Zero);
                }
                stopRequested = true;
                Console.WriteLine("stop_request=second_launch");
                Console.WriteLine("pause_resume=ok");
            }
        }

        private IntPtr FindRecordingOverlay(out NativeRect bounds)
        {
            IntPtr found = IntPtr.Zero;
            NativeRect foundBounds = new NativeRect();
            EnumWindows((window, parameter) =>
            {
                uint processId;
                if (GetWindowThreadProcessId(window, out processId) != 0 &&
                    processId == (uint)appProcess.Id && IsWindowVisible(window))
                {
                    NativeRect candidate;
                    if (GetWindowRect(window, out candidate))
                    {
                        int width = candidate.Right - candidate.Left;
                        int height = candidate.Bottom - candidate.Top;
                        if (width >= 50 && width <= 300 && height >= 15 && height <= 100)
                        {
                            found = window;
                            foundBounds = candidate;
                            return false;
                        }
                    }
                }
                return true;
            }, IntPtr.Zero);
            bounds = foundBounds;
            return found;
        }

        private void TogglePause()
        {
            var start = new ProcessStartInfo(appPath, "--toggle-pause")
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(appPath)
            };
            using (Process pauseSignal = Process.Start(start))
            {
                if (pauseSignal == null || !pauseSignal.WaitForExit(5000) || pauseSignal.ExitCode != 0)
                    throw new InvalidOperationException("The actual executable's pause/resume shortcut request failed.");
            }
        }

        public void Dispose()
        {
            timer.Dispose();
            if (occluder != null)
                occluder.Dispose();
        }
    }
}
