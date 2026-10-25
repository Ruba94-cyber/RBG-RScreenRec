using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace RScreenRec
{
    static class Program
    {
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
        private const int VK_SHIFT = 0x10;
        private const int VK_CONTROL = 0x11;

        private static bool HasArgument(string[] args, string name)
        {
            return Array.Exists(args, arg => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase));
        }

        private const string InstanceMutexName = @"Local\RScreenRec_SingleInstance";
        private const string StopEventName = @"Local\RScreenRec_StopEvent";
        private const string PauseEventName = @"Local\RScreenRec_PauseEvent";
        private static readonly string StopRequestPath = Path.Combine(Path.GetTempPath(), "RScreenRec_" + Environment.UserName + "_" + Process.GetCurrentProcess().SessionId + ".stop");

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

        [DllImport("shcore.dll")]
        private static extern int SetProcessDpiAwareness(int value);

        private const int PROCESS_DPI_UNAWARE = 0;
        private const int PROCESS_SYSTEM_DPI_AWARE = 1;
        private const int PROCESS_PER_MONITOR_DPI_AWARE = 2;
        private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new IntPtr(-4);

        [STAThread]
        static void Main(string[] args)
        {
            Stopwatch startupStopwatch = Stopwatch.StartNew();
            ConfigureDpiAwareness();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (HasArgument(args, "--toggle-pause"))
            {
                SignalPause();
                return;
            }

            bool forceWindow = HasArgument(args, "--record-window");
            bool forceScreen = HasArgument(args, "--record-screen");
            bool shiftHeld = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
            bool controlHeld = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
            bool settingsRequested = HasArgument(args, "--settings") ||
                (!forceWindow && !forceScreen && shiftHeld && controlHeld);
            if (settingsRequested)
            {
                ShortcutSettings.Show("RBG RScreenRec", "Screen recording", "Window recording",
                    "--record-screen", "--record-window", "Ctrl+Alt+R", "Ctrl+Alt+W");
                return;
            }

            bool windowSelectionRequested = forceWindow || (!forceScreen && shiftHeld);
            bool isFirstInstance;
            using (var mutex = new Mutex(true, InstanceMutexName, out isFirstInstance))
            {
                if (!isFirstInstance)
                {
                    SignalStop();
                    return; // second launch only stops the running recorder
                }

                // Clear only stale fallback requests before publishing the stop event.
                // A second launch retries opening the event while this instance starts.
                TryDeleteStopRequest();
                bool createdNewStopEvent;
                using (var stopEvent = new EventWaitHandle(
                    false,
                    EventResetMode.ManualReset,
                    StopEventName,
                    out createdNewStopEvent))
                {
                    using (var pauseEvent = new EventWaitHandle(false, EventResetMode.AutoReset, PauseEventName))
                    {
                    IntPtr selectedWindow = IntPtr.Zero;
                    if (windowSelectionRequested)
                    {
                        selectedWindow = WindowPicker.SelectWindow();
                        if (selectedWindow == IntPtr.Zero) return;
                    }

                    // Get the monitor under the cursor
                    Screen activeScreen = Screen.FromPoint(Cursor.Position);
                    Rectangle bounds = activeScreen.Bounds;

                    string benchmarkFolder = Environment.GetEnvironmentVariable("RSCREENREC_BENCHMARK_OUTPUT_DIRECTORY");
                    string folder = string.IsNullOrWhiteSpace(benchmarkFolder)
                        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Captures")
                        : Path.GetFullPath(benchmarkFolder);
                    try
                    {
                        Directory.CreateDirectory(folder);
                    }
                    catch (Exception ex)
                    {
                        Logger.Log("Error creating output directory.", ex);
                        MessageBox.Show("Cannot create the recording folder: " + ex.Message,
                            "RScreenRec", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    string outputPath = CreateOutputPath(folder);

                    var recorder = new ScreenRecorder();
                    try
                    {
                        if (selectedWindow == IntPtr.Zero)
                            recorder.StartRecording(bounds, outputPath);
                        else
                            recorder.StartWindowRecording(selectedWindow, outputPath);
                        if (selectedWindow != IntPtr.Zero &&
                            recorder.WindowCaptureBackend != "Windows Graphics Capture")
                        {
                            MessageBox.Show(
                                "Windows Graphics Capture is unavailable for this window or system. " +
                                "RScreenRec is using PrintWindow compatibility capture; some GPU-rendered windows may be blank or incomplete. " +
                                "A minimized window holds its last captured frame. Other desktop content is never used as a fallback.",
                                "RScreenRec - Compatibility capture",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Warning);
                        }
                        Logger.Log(string.Format("Startup ready in {0} ms. Output={1}",
                            startupStopwatch.ElapsedMilliseconds,
                            outputPath));
                    }
                    catch (Exception ex)
                    {
                        Logger.Log("Failed to start recording.", ex);
                        MessageBox.Show("Recording could not start: " + ex.Message,
                            "RScreenRec", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    OverlayHost touchOverlay = selectedWindow == IntPtr.Zero
                        ? OverlayHost.Start("TouchOverlay", () => new TouchOverlayForm(bounds)) : null;
                    Rectangle overlayBounds = selectedWindow == IntPtr.Zero
                        ? bounds : Screen.FromHandle(selectedWindow).Bounds;
                    OverlayHost recordingOverlay = OverlayHost.Start("RecordingOverlay",
                        () => new RecordingOverlayForm(overlayBounds));

                    // Wait until stop is requested or recording ends
                    bool stopRequested = false;
                    while (recorder.IsRecording)
                    {
                        if (stopEvent.WaitOne(20) || File.Exists(StopRequestPath))
                        {
                            stopRequested = true;
                            break;
                        }
                        if (pauseEvent.WaitOne(0))
                        {
                            bool paused = recorder.TogglePause();
                            if (recordingOverlay != null) recordingOverlay.SetPaused(paused);
                        }
                    }

                    if (stopRequested)
                    {
                        Logger.Log("Stop request detected; closing overlays immediately.");
                        if (recordingOverlay != null) recordingOverlay.CloseNow();
                        if (touchOverlay != null) touchOverlay.CloseNow();
                    }

                    try
                    {
                        recorder.StopRecording();
                    }
                    catch (Exception ex)
                    {
                        Logger.Log("Failed to stop recording cleanly.", ex);
                        MessageBox.Show("Recording ended with an error: " + ex.Message,
                            "RScreenRec", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    TryDeleteStopRequest();
                    if (recordingOverlay != null) recordingOverlay.Dispose();
                    if (touchOverlay != null) touchOverlay.Dispose();
                    }
                }
            }
        }

        private static string CreateOutputPath(string folder)
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff");
            string basePath = Path.Combine(folder, "rec_" + timestamp);
            string outputPath = basePath + ".mp4";
            if (!File.Exists(outputPath))
                return outputPath;

            for (int i = 1; i < 1000; i++)
            {
                outputPath = string.Format("{0}_{1}.mp4", basePath, i);
                if (!File.Exists(outputPath))
                    return outputPath;
            }

            return Path.Combine(folder, "rec_" + timestamp + "_" + Guid.NewGuid().ToString("N") + ".mp4");
        }

        private static void SignalPause()
        {
            try
            {
                using (var pauseEvent = EventWaitHandle.OpenExisting(PauseEventName))
                    pauseEvent.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                // Pausing is meaningful only when an existing recording is active.
            }
            catch (Exception ex)
            {
                Logger.Log("Could not toggle recording pause state.", ex);
            }
        }

        private static void SignalStop()
        {
            bool signaled = false;
            const int maxAttempts = 20;
            for (int attempt = 0; attempt < maxAttempts && !signaled; attempt++)
            {
                try
                {
                    using (var stopEvent = EventWaitHandle.OpenExisting(StopEventName))
                    {
                        stopEvent.Set();
                        signaled = true;
                    }
                }
                catch (WaitHandleCannotBeOpenedException)
                {
                    // The first instance may own the mutex but not have published its event yet.
                    if (attempt + 1 < maxAttempts)
                        Thread.Sleep(25);
                }
                catch (Exception ex)
                {
                    Logger.Log("Could not signal stop event.", ex);
                    break;
                }
            }

            // The file is a fallback only. Writing it after a successful event
            // signal could leave a stale request that stops a later recording.
            if (!signaled)
            {
                try
                {
                    File.WriteAllText(StopRequestPath, DateTime.UtcNow.ToString("O"));
                    signaled = true;
                }
                catch (Exception ex)
                {
                    Logger.Log("Could not write stop request file.", ex);
                }
            }

            Logger.Log(string.Format("Stop signal requested. Success={0}", signaled));
        }

        private static void ConfigureDpiAwareness()
        {
            try
            {
                if (SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2))
                    return;
            }
            catch (EntryPointNotFoundException) { }
            catch (DllNotFoundException) { }

            try
            {
                int result = SetProcessDpiAwareness(PROCESS_PER_MONITOR_DPI_AWARE);
                if (result >= 0)
                    return;
            }
            catch (EntryPointNotFoundException) { }
            catch (DllNotFoundException) { }

            try { SetProcessDPIAware(); }
            catch (EntryPointNotFoundException) { }
            catch (DllNotFoundException) { }
        }

        private static bool HasOtherRunningInstance()
        {
            try
            {
                int currentId = Process.GetCurrentProcess().Id;
                string currentProcessName = Process.GetCurrentProcess().ProcessName;
                foreach (Process process in Process.GetProcessesByName(currentProcessName))
                {
                    using (process)
                    {
                        if (process.Id != currentId)
                            return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log("Could not inspect running RScreenRec processes.", ex);
            }

            return false;
        }

        private static void TryDeleteStopRequest()
        {
            try
            {
                if (File.Exists(StopRequestPath))
                    File.Delete(StopRequestPath);
            }
            catch (Exception ex)
            {
                Logger.Log("Could not delete stop request file.", ex);
            }
        }

        private sealed class OverlayHost : IDisposable
        {
            private readonly ManualResetEventSlim ready = new ManualResetEventSlim(false);
            private readonly Func<Form> formFactory;
            private readonly string name;
            private Thread thread;
            private Form form;
            private bool closeRequested;

            private OverlayHost(string name, Func<Form> formFactory)
            {
                this.name = name;
                this.formFactory = formFactory;
            }

            public static OverlayHost Start(string name, Func<Form> formFactory)
            {
                var host = new OverlayHost(name, formFactory);
                host.Start();
                return host;
            }

            public void SetPaused(bool paused)
            {
                Form current = form;
                if (current == null) return;
                try
                {
                    if (!current.IsDisposed && current.IsHandleCreated)
                        current.BeginInvoke(new Action(() =>
                        {
                            RecordingOverlayForm indicator = current as RecordingOverlayForm;
                            if (indicator != null) indicator.SetPaused(paused);
                        }));
                }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
            }

            public void CloseNow()
            {
                Form current = form;
                closeRequested = true;
                if (current == null)
                    return;

                try
                {
                    if (!current.IsDisposed && current.IsHandleCreated)
                    {
                        current.BeginInvoke(new Action(() =>
                        {
                            current.Hide();
                            current.Close();
                        }));
                    }
                }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
            }

            public void Dispose()
            {
                CloseNow();
                if (thread != null && thread.IsAlive)
                    thread.Join(2000);
                ready.Dispose();
            }

            private void Start()
            {
                thread = new Thread(Run);
                thread.SetApartmentState(ApartmentState.STA);
                thread.IsBackground = true;
                thread.Name = name;
                thread.Start();
                ready.Wait(250);
            }

            private void Run()
            {
                try
                {
                    form = formFactory();
                    if (closeRequested)
                    {
                        form.Dispose();
                        ready.Set();
                        return;
                    }

                    form.Shown += (s, e) => ready.Set();
                    form.FormClosed += (s, e) => ready.Set();
                    Application.Run(form);
                }
                catch (Exception ex)
                {
                    Logger.Log(string.Format("{0} failed.", name), ex);
                    ready.Set();
                }
            }
        }
    }
}
