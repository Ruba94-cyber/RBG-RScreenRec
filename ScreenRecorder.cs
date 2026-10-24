using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace RScreenRec
{
    public class ScreenRecorder
    {
        private const int FramesPerSecond = 30;
        private const int MaxCatchUpFramesPerCycle = FramesPerSecond * 2;
        private const int ScreenRecordingQuality = 60;
        private readonly object recordingLock = new object();
        private Thread recordingThread;
        private Stopwatch recordingStopwatch;
        private ManualResetEventSlim firstFrameWritten;
        private Rectangle bounds;
        private IntPtr targetWindow;
        private IFrameSource initialWindowSource;
        private string windowCaptureBackend;
        private byte[] frameBuffer;
        private bool isRecording;
        private bool isPaused;
        private long pausedAtTicks;
        private long pausedTotalTicks;
        private string outputPath;
        private int encodedWidth;
        private int encodedHeight;
        private int encodedPaddingRight;
        private int encodedPaddingBottom;
        private int framesWritten;
        private int updatedSourceFrames;
        private int repeatedSourceFrames;
        private Exception recordingError;

        public void StartRecording(Rectangle screenBounds, string outputPath)
        {
            StartRecordingCore(screenBounds, outputPath, IntPtr.Zero, null, "Desktop");
        }

        public void StartWindowRecording(IntPtr window, string outputPath)
        {
            string backend;
            IFrameSource source = WindowFrameSourceFactory.Create(window, out backend);
            try
            {
                StartRecordingCore(new Rectangle(Point.Empty, source.FrameSize), outputPath, window, source, backend);
            }
            catch
            {
                bool workerOwnsSource;
                lock (recordingLock)
                    workerOwnsSource = recordingThread != null;
                if (!workerOwnsSource)
                    source.Dispose();
                throw;
            }
        }

        public string WindowCaptureBackend
        {
            get
            {
                lock (recordingLock)
                    return windowCaptureBackend;
            }
        }

        private void StartRecordingCore(Rectangle screenBounds, string outputPath, IntPtr window,
            IFrameSource windowSource, string captureBackend)
        {
            if (screenBounds.Width <= 0 || screenBounds.Height <= 0)
                throw new ArgumentException("Screen bounds must have a positive size.", "screenBounds");
            if (string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentException("Output path is required.", "outputPath");

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

            Stopwatch startupStopwatch = Stopwatch.StartNew();
            lock (recordingLock)
            {
                if (isRecording || recordingThread != null)
                    throw new InvalidOperationException("A recording session is already in progress or has not been finalized.");

                bounds = screenBounds;
                targetWindow = window;
                initialWindowSource = windowSource;
                windowCaptureBackend = captureBackend;
                this.outputPath = outputPath;
                encodedWidth = MakeH264CompatibleDimension(screenBounds.Width);
                encodedHeight = MakeH264CompatibleDimension(screenBounds.Height);
                encodedPaddingRight = encodedWidth - screenBounds.Width;
                encodedPaddingBottom = encodedHeight - screenBounds.Height;
                frameBuffer = new byte[GetFrameBufferSize(encodedWidth, encodedHeight)];
                firstFrameWritten = new ManualResetEventSlim(false);
                framesWritten = 0;
                updatedSourceFrames = 0;
                repeatedSourceFrames = 0;
                recordingError = null;
                recordingStopwatch = Stopwatch.StartNew();
                isPaused = false;
                pausedAtTicks = 0;
                pausedTotalTicks = 0;
                isRecording = true;
            }

            Thread worker = new Thread(RecordLoop);
            worker.IsBackground = true;
            worker.Name = "ScreenCaptureMediaFoundation";
            worker.Priority = ThreadPriority.AboveNormal;
            lock (recordingLock)
                recordingThread = worker;
            try
            {
                worker.Start();
            }
            catch
            {
                lock (recordingLock)
                {
                    isRecording = false;
                    recordingThread = null;
                    recordingStopwatch = null;
                    initialWindowSource = null;
                    frameBuffer = null;
                    firstFrameWritten.Dispose();
                    firstFrameWritten = null;
                }
                throw;
            }

            // Wait briefly for the worker to write its first frame. Do not report
            // startup as successful if Media Foundation failed immediately.
            ManualResetEventSlim startupEvent;
            lock (recordingLock)
                startupEvent = firstFrameWritten;
            bool firstFrameReady = startupEvent.Wait(3000);
            Exception startupError;
            lock (recordingLock)
                startupError = recordingError;
            if (startupError != null || !IsRecording)
            {
                // StopRecording joins the worker and propagates encoder errors.
                // If it returns normally, the worker ended without an error
                // before recording was established.
                StopRecording();
                throw new InvalidOperationException("Recording stopped before its first frame.");
            }
            if (!firstFrameReady)
            {
                // Do not claim a successful start when the encoder has not
                // accepted a frame. Shut down and surface the failure.
                try
                {
                    StopRecording();
                }
                catch (Exception ex)
                {
                    throw new TimeoutException("The recorder could not write its first frame within 3 seconds.", ex);
                }
                throw new TimeoutException("The recorder could not write its first frame within 3 seconds.");
            }
            else if (!IsRecording)
            {
                // The first frame may have been written just before an error.
                // Do not report startup success for a stopped worker.
                StopRecording();
                throw new InvalidOperationException("Recording stopped during startup.");
            }

            Logger.Log(string.Format("Recording dispatched in {0} ms. Output={1}, CaptureBounds={2}x{3}@({4},{5}), EncodedSize={6}x{7}, PaddingRight={8}, PaddingBottom={9}, FPS={10}, H264Quality={11}, Backend={12}",
                startupStopwatch.ElapsedMilliseconds,
                outputPath,
                screenBounds.Width,
                screenBounds.Height,
                screenBounds.X,
                screenBounds.Y,
                encodedWidth,
                encodedHeight,
                encodedPaddingRight,
                encodedPaddingBottom,
                FramesPerSecond,
                ScreenRecordingQuality,
                captureBackend));
        }

        public void StopRecording()
        {
            Thread captureThread;
            Stopwatch stopwatch;
            ManualResetEventSlim firstFrameEvent;
            string path;

            lock (recordingLock)
            {
                captureThread = recordingThread;
                stopwatch = recordingStopwatch;
                firstFrameEvent = firstFrameWritten;
                path = outputPath;
                // Request shutdown before waiting, but retain worker-owned resources
                // until the thread has actually terminated.
                isRecording = false;
            }

            if (captureThread != null && captureThread.IsAlive && !captureThread.Join(90000))
            {
                Logger.Log("Capture thread did not finalize within timeout; keeping its resources alive.");
                throw new TimeoutException("Capture thread did not stop within 90 seconds.");
            }

            TimeSpan requestedDuration = stopwatch != null ? stopwatch.Elapsed : TimeSpan.Zero;
            if (stopwatch != null)
                stopwatch.Stop();

            Exception error;
            int written;
            int updated;
            int repeated;
            string backend;
            lock (recordingLock)
            {
                error = recordingError;
                written = framesWritten;
                updated = updatedSourceFrames;
                repeated = repeatedSourceFrames;
                backend = windowCaptureBackend;
                recordingThread = null;
                recordingStopwatch = null;
                firstFrameWritten = null;
                initialWindowSource = null;
                frameBuffer = null;
            }

            if (firstFrameEvent != null)
                firstFrameEvent.Dispose();

            long size = !string.IsNullOrEmpty(path) && File.Exists(path) ? new FileInfo(path).Length : 0;
            Logger.Log(string.Format("Recording stopped. RequestedDuration={0}, Frames={1}, SourceFramesUpdated={2}, SourceFramesRepeated={3}, Backend={4}, Size={5}, Output={6}",
                requestedDuration, written, updated, repeated, backend, size, path));

            if (error != null)
                throw new InvalidOperationException("Recording failed while finalizing.", error);
        }

        /// <summary>
        /// Pauses encoding without introducing a gap in the MP4 timeline.
        /// The capture worker remains alive; no additional process is required.
        /// Returns the new pause state.
        /// </summary>
        public bool TogglePause()
        {
            lock (recordingLock)
            {
                if (!isRecording || recordingStopwatch == null)
                    return false;
                long now = recordingStopwatch.ElapsedTicks;
                if (isPaused)
                {
                    pausedTotalTicks += Math.Max(0, now - pausedAtTicks);
                    isPaused = false;
                }
                else
                {
                    pausedAtTicks = now;
                    isPaused = true;
                }
                Logger.Log(isPaused ? "Recording paused." : "Recording resumed.");
                return isPaused;
            }
        }

        public bool IsPaused
        {
            get { lock (recordingLock) return isPaused; }
        }

        private long ActiveElapsedTicks(Stopwatch stopwatch)
        {
            lock (recordingLock)
            {
                long end = isPaused ? pausedAtTicks : stopwatch.ElapsedTicks;
                return Math.Max(0, end - pausedTotalTicks);
            }
        }

        public bool IsRecording
        {
            get
            {
                lock (recordingLock)
                {
                    return isRecording;
                }
            }
        }

        private void RecordLoop()
        {
            Stopwatch stopwatch;
            Rectangle captureBounds;
            string path;
            int outputWidth;
            int outputHeight;
            IntPtr window;
            IFrameSource suppliedWindowSource;

            lock (recordingLock)
            {
                stopwatch = recordingStopwatch;
                captureBounds = bounds;
                path = outputPath;
                outputWidth = encodedWidth;
                outputHeight = encodedHeight;
                window = targetWindow;
                suppliedWindowSource = initialWindowSource;
                initialWindowSource = null;
            }

            if (stopwatch == null)
                return;

            long frameIntervalTicks = Math.Max(1, (long)Math.Round(Stopwatch.Frequency / (double)FramesPerSecond));
            long frameIndex = 0;
            float dpiScale = DpiHelper.GetSystemDpiScale();
            bool firstFrameSignaled = false;
            Exception frameSourceFailure = null;

            try
            {
                IFrameSource selectedSource = window == IntPtr.Zero
                    ? (IFrameSource)new DesktopFrameSource(captureBounds)
                    : suppliedWindowSource;
                if (selectedSource == null)
                    throw new InvalidOperationException("The selected window capture source was not initialized.");

                using (IFrameSource source = selectedSource)
                using (var writer = new MediaFoundationMp4Writer(path, outputWidth, outputHeight, FramesPerSecond, ScreenRecordingQuality))
                using (Bitmap bitmap = new Bitmap(source.FrameSize.Width, source.FrameSize.Height, PixelFormat.Format32bppArgb))
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    while (IsRecording)
                    {
                        SleepUntilActive(stopwatch, frameIndex * frameIntervalTicks);
                        if (!IsRecording)
                            break;
                        if (IsPaused)
                            continue;

                        FrameCaptureStatus captureStatus;
                        try
                        {
                            captureStatus = source.CaptureFrame(graphics);
                        }
                        catch (Exception ex)
                        {
                            frameSourceFailure = ex;
                            Logger.Log("Frame source capture failed.", ex);
                            throw;
                        }
                        if (captureStatus == FrameCaptureStatus.Updated)
                            Interlocked.Increment(ref updatedSourceFrames);
                        else
                            Interlocked.Increment(ref repeatedSourceFrames);
                        if (window == IntPtr.Zero)
                        {
                            DrawMousePointer(graphics, captureBounds, dpiScale);
                            DrawTouchPointers(graphics, captureBounds, dpiScale);
                        }

                        // Window frames are unchanged when minimized or when WGC has
                        // not delivered a new frame. Reuse the already-converted
                        // RGB32 buffer rather than copying millions of pixels again.
                        // Desktop mode always converts because cursor/touch overlays can change.
                        byte[] frame = window != IntPtr.Zero &&
                            captureStatus == FrameCaptureStatus.Repeated && frameIndex > 0
                            ? frameBuffer
                            : BitmapToRgb32TopDownPadded(bitmap, outputWidth, outputHeight);
                        WriteFrame(writer, frame, frame.Length, frameIndex, ref firstFrameSignaled);
                        frameIndex++;

                        long expectedFrameCount = Math.Max(frameIndex, ActiveElapsedTicks(stopwatch) / frameIntervalTicks);
                        int catchUpFrames = 0;
                        while (frameIndex < expectedFrameCount &&
                               catchUpFrames < MaxCatchUpFramesPerCycle &&
                               IsRecording)
                        {
                            WriteFrame(writer, frame, frame.Length, frameIndex, ref firstFrameSignaled);
                            frameIndex++;
                            catchUpFrames++;
                        }

                        if (frameIndex < expectedFrameCount)
                            frameIndex = expectedFrameCount;
                    }
                }
            }
            catch (Exception ex)
            {
                Exception reportedError = frameSourceFailure ?? ex;
                lock (recordingLock)
                {
                    recordingError = reportedError;
                    isRecording = false;
                }
                if (frameSourceFailure != null && !ReferenceEquals(ex, frameSourceFailure))
                    Logger.Log("MP4 finalization also failed after the frame source error.", ex);
                Logger.Log("Capture loop stopped by error.", reportedError);
            }
            finally
            {
                lock (recordingLock)
                {
                    isRecording = false;
                }
                Logger.Log("Capture loop stopped.");
            }
        }

        private void WriteFrame(MediaFoundationMp4Writer writer, byte[] frame, int length, long frameIndex, ref bool firstFrameSignaled)
        {
            writer.WriteFrame(frame, length, frameIndex);
            lock (recordingLock)
            {
                framesWritten++;
            }

            if (!firstFrameSignaled)
            {
                firstFrameSignaled = true;
                ManualResetEventSlim firstFrameEvent;
                lock (recordingLock)
                {
                    firstFrameEvent = firstFrameWritten;
                }

                if (firstFrameEvent != null)
                    firstFrameEvent.Set();
            }
        }

        private void SleepUntilActive(Stopwatch stopwatch, long targetTicks)
        {
            while (IsRecording)
            {
                if (IsPaused)
                {
                    Thread.Sleep(15);
                    continue;
                }
                long remainingTicks = targetTicks - ActiveElapsedTicks(stopwatch);
                if (remainingTicks <= 0)
                    return;

                int sleepMs = (int)(remainingTicks * 1000 / Stopwatch.Frequency);
                if (sleepMs > 1)
                    Thread.Sleep(sleepMs - 1);
                else
                    Thread.SpinWait(80);
            }
        }

        private static readonly SolidBrush MousePointerBrush = new SolidBrush(Color.Red);

        private static void DrawMousePointer(Graphics graphics, Rectangle captureBounds, float dpiScale)
        {
            Point cursorPos = Cursor.Position;
            int localX = cursorPos.X - captureBounds.X;
            int localY = cursorPos.Y - captureBounds.Y;

            if (localX >= 0 && localX < captureBounds.Width && localY >= 0 && localY < captureBounds.Height)
            {
                int scaledSize = DpiHelper.ScaleValue(10, dpiScale);
                int scaledOffset = scaledSize / 2;
                graphics.FillEllipse(
                    MousePointerBrush,
                    localX - scaledOffset,
                    localY - scaledOffset,
                    scaledSize,
                    scaledSize);
            }
        }

        private static void DrawTouchPointers(Graphics graphics, Rectangle captureBounds, float dpiScale)
        {
            Point[] touchPoints = TouchOverlayForm.GetActiveTouchPointsSnapshot();
            if (touchPoints.Length == 0)
                return;

            int radius = DpiHelper.ScaleValue(20, dpiScale);
            int diameter = radius * 2;
            int borderWidth = Math.Max(1, DpiHelper.ScaleValue(3, dpiScale));
            foreach (Point point in touchPoints)
            {
                int localX = point.X - captureBounds.X;
                int localY = point.Y - captureBounds.Y;
                if (localX < 0 || localY < 0 || localX >= captureBounds.Width || localY >= captureBounds.Height)
                    continue;
                Rectangle rect = new Rectangle(localX - radius, localY - radius, diameter, diameter);
                using (Pen border = new Pen(Color.White, borderWidth))
                {
                    graphics.FillEllipse(MousePointerBrush, rect);
                    graphics.DrawEllipse(border, rect);
                }
            }
        }

        private unsafe byte[] BitmapToRgb32TopDownPadded(Bitmap bitmap, int outputWidth, int outputHeight)
        {
            Rectangle rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            BitmapData data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int outputStride = outputWidth * 4;
                int bufferSize = GetFrameBufferSize(outputWidth, outputHeight);
                if (frameBuffer == null || frameBuffer.Length < bufferSize)
                    frameBuffer = new byte[bufferSize];

                if (outputWidth != bitmap.Width || outputHeight != bitmap.Height)
                    Array.Clear(frameBuffer, 0, bufferSize);

                int inputRowBytes = bitmap.Width * 4;
                int inputStride = data.Stride;
                int absStride = Math.Abs(inputStride);
                byte* srcBase = (byte*)data.Scan0.ToPointer();
                byte* src = inputStride < 0 ? srcBase + (bitmap.Height - 1) * absStride : srcBase;

                fixed (byte* destBase = frameBuffer)
                {
                    byte* dest = destBase;
                    for (int y = 0; y < bitmap.Height; y++)
                    {
                        Buffer.MemoryCopy(src, dest, outputStride, inputRowBytes);
                        dest += outputStride;
                        src += inputStride < 0 ? -absStride : absStride;
                    }
                }

                return frameBuffer;
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        private static int MakeH264CompatibleDimension(int value)
        {
            return (value & 1) == 0 ? value : value + 1;
        }

        private static int GetFrameBufferSize(int width, int height)
        {
            long bytes = width * (long)height * 4L;
            if (bytes > int.MaxValue)
                throw new InvalidOperationException("Screen resolution is too large for a single RGB32 frame buffer.");

            return (int)bytes;
        }

    }
}
