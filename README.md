# RBG - RScreenRec - Advanced Screen Recording Tool

A C# desktop application for advanced screen recording with visual overlays and touch input support.

Author: Paolo Rubagotti.

Windows release packages are unsigned because no Authenticode certificate is available. The executable metadata identifies Paolo Rubagotti and `paolo.ruba.1913@gmail.com`; Windows may show an unknown publisher notice.

## Features

### 🎥 Screen Recording
- **Multi-monitor aware**: Automatically records the monitor under the mouse cursor.
- **Screen-format safe**: Supports standard, ultrawide, portrait, 16:10, 4:3, and odd pixel dimensions without cropping.
- **Compact H.264 encoding**: 30 FPS with a constant-quality profile tuned for readable screen text and controlled file size.
- **Variable file size**: Output depends on resolution and screen motion. A 3-second, 1920x1200 synthetic office-UI clip encoded at quality 60 was 224,742 bytes; a simple linear scale would be about 270 MB/hour for that test content. This is an illustration, not a prediction or a file-size limit; changing video and higher resolutions can be substantially larger.
- **DPI aware**: Handles high-resolution displays and mixed-DPI coordinate spaces.
- **Portable single executable**: No FFmpeg, codecs, or helper binaries are shipped beside the app.
- **Automatic file management**: Timestamped filenames avoid slow startup scans.

### 🎯 Visual Overlays
- **Recording indicator**: Blinking dot to show active recording.
- **Mouse cursor overlay**: Highlights the cursor in recordings with a red dot.
- **Touch overlay**: Displays touch input using red, outlined circles.

### 🔧 Technical Capabilities
- **File locking**: Prevents multiple instances from running simultaneously.
- **Thread-safe design**: Safe coordination of overlay and recording threads.
- **Robust error handling**: Comprehensive exception management.
- **Performance tuned**: Minimal allocations and efficient resource usage.

## System Requirements

- **Operating System**: Windows 7 or later
- **.NET Framework**: 4.8
- **Hardware**: DirectX-compatible graphics adapter for screen capture
- **Window capture**: Windows Graphics Capture is the preferred backend on Windows 10 version 1803 or later. Older or unsupported configurations may use the PrintWindow compatibility backend, which can return incomplete frames.
- **Touch**: Optional Windows Touch support for the touch overlay

## Usage

### Quick Start
```bash
"RBG - RScreenRec.exe"
```

### How it works
1. **First launch**: Detects the current monitor and starts recording immediately.
2. **Second launch**: Stops the current recording if one is running.
3. **Output files**: Videos are saved to `%USERPROFILE%\Videos\Captures\`.

### Recording a window

1. Hold **Shift** while launching RBG - RScreenRec.
2. Select a visible window from the list.
3. Continue using other applications; window mode captures only the selected window and does not use desktop capture as a fallback.
4. Launch `RBG - RScreenRec.exe` again to stop and finalize the MP4.

### Configurable keyboard shortcuts (no background process)

Hold **Ctrl+Shift while launching the executable** to open Shortcut Settings.
Choose separate combinations for **screen recording** and **window recording**.
The suggested defaults are `Ctrl+Alt+R` (screen), `Ctrl+Alt+W` (window), and `Ctrl+Alt+P` (pause/resume). No hotkey is installed until you click **Save settings**.

- Click a shortcut box and press **Ctrl+Alt+letter** or **Ctrl+Alt+F1–F12** (optionally with Shift).
- Press **Delete** to clear a shortcut, then save to remove its Windows link.
- The application creates per-user Start Menu shortcuts in `RBG Utilities`. Windows Explorer handles hotkeys, so no RScreenRec process needs to remain running between recordings. The settings dialog checks collisions between both RBG utilities, but Windows and unrelated applications may still reserve combinations. Explorer must be running and shortcut registration can take a moment.
- If you move or rename the executable, reopen settings from its new location and save again.
- The original direct-executable behavior remains unchanged: normal launch starts/stops screen recording, Shift+launch requests window capture, and Ctrl+Shift+launch opens settings. The two hotkeys launch the respective modes; when a recording is already active, either launch stops it.

**REC badge:** A smaller blinking REC badge appears during screen and window recording. Drag the badge to move it; its position persists across launches. Adjust its size (70–130%) in settings. When paused, an amber PAUSE label replaces the blinking REC indicator. The app asks Windows to exclude the indicator from captures when supported, but exclusion is dependent on the Windows version and capture backend.

### Pause and resume

The optional pause shortcut (suggested `Ctrl+Alt+P`) toggles pause/resume during an active recording, using a lightweight Windows event. The MP4 timeline excludes the pause interval, so playback does not contain a long frozen pause. The recorder remains open during pauses and the existing second-launch behavior still stops and finalizes the video. The explicit `--toggle-pause` argument does nothing if no recording is running.

### Improved window picker

Type part of a window title to filter the list, or choose **Click a window...** and click the target directly. Restore minimized targets before starting capture. After recording begins, minimize/restore is handled according to the Windows Graphics Capture or PrintWindow limitations explained below.

### Release download

Download `RBG-RScreenRec-1.2.0-windows-x64.zip` from GitHub Releases, extract the archive, then run `RBG - RScreenRec.exe`.

Windows Graphics Capture is used when available. If it cannot be initialized, RScreenRec warns before using PrintWindow compatibility capture; some GPU-rendered or protected windows may then appear blank or incomplete. When a target is minimized, Windows may stop rendering it. RScreenRec keeps the last captured image in the video timeline and resumes with new frames after the window is restored; this is not live capture while minimized.

The H.264 profile preserves a 30 FPS output timeline and does not deliberately reduce the frame rate to shrink files. Repeated samples hold the last complete image when the capture source has no new frame. Quality-based encoding has no strict file-size cap, so rapidly changing full-screen content can produce much larger files than a mostly static desktop. In a 3-second, 1920x1200 synthetic office-UI comparison, quality 60 produced a 224,742-byte clip (56.9% smaller than quality 35 at 521,487 bytes). First-frame SSIM against the source image was 0.9913 at quality 60 and 0.9573 at quality 35. Quality 30 was 39.5% smaller than quality 60, but visibly lost fine lines and measured 0.9214 SSIM, so quality 60 is the current size/readability balance. These short synthetic tests do not predict every screen or encoder. A synthetic 3601-second, 30 FPS writer run is also checked for timestamps, finalization, and full decoding; this validates long MP4 timelines, not a 60-minute interactive capture session or its resulting file size.

### Windows smoke tests

Run `scripts/test-windows-smoke.ps1` in an interactive Windows session. It also validates a pause/resume case by checking the resulting MP4 duration and full decoding. It builds the Release app, automates the window picker against a separate synthetic process, exercises odd-landscape, portrait, and panoramic WGC capture, checks occlusion, resize, minimize/restore, selected-window closure, and validates every MP4 with FFmpeg. It also writes a synthetic timeline longer than one hour. The test runner requires the .NET Framework C# compiler plus `ffmpeg.exe` and `ffprobe.exe` on `PATH`; these are development tools and are not shipped with RScreenRec. Successful runs remove their temporary media, while failed runs retain the artifacts for diagnosis.

### File Naming
Files follow the pattern `rec_[timestamp].mp4`:
- **timestamp**: Uses the format `yyyy-MM-dd_HH-mm-ss-fff`.

Example: `rec_2026-06-04_16-22-40-234.mp4`

## Code Architecture

### Key Components

#### `Program.cs`
- Application entry point.
- Manages the lock file used to prevent multiple instances.
- Coordinates threads used for overlays.
- Controls the application lifecycle.

#### `ScreenRecorder.cs`
- Core engine responsible for screen capture.
- Generates MP4 files through the native Media Foundation writer.
- Thread-safe management of recording loops.
- Integrates mouse cursor overlay.

#### `MediaFoundationMp4Writer.cs`
- Thin COM/PInvoke wrapper around Windows Media Foundation.
- Writes H.264 video into an MP4 container.
- Uses a constant-quality H.264 profile and explicit timestamps and durations for normal playback speed and long recordings.

#### `FrameSources.cs`
- Uses Windows Graphics Capture for selected windows when available, with PrintWindow compatibility capture if WGC initialization fails.
- Keeps selected-window frames independent of other desktop windows; the compatibility backend may not render every GPU or protected surface correctly.

#### `RecordingOverlayForm.cs`
- Transparent overlay indicating active recording.
- Always-on-top window with custom blinking animation.
- Automatically positioned on the screen.

#### `TouchOverlayForm.cs`
- Captures and renders touch input events.
- Displays full-screen transparent overlay.
- Cleans up visual touch indicators automatically.

## Advanced Configuration

### Performance Tuning
- **FPS**: 30 FPS, configured in `ScreenRecorder.cs`.
- **Quality/size**: H.264 constant-quality profile 60; actual file size varies with scene motion, resolution, and the Windows encoder. The app does not impose a maximum file size or intentionally drop frames to reduce storage.
- **Buffer size**: Automatically determined from the selected capture dimensions.

### Overlay Customization
- **Indicator position**: Drag the REC badge; saved per-user and constrained to visible monitors.
- **Colors**: Modify the overlay classes to adjust colors.
- **Sizes**: REC badge size is adjustable in the built-in Ctrl+Shift settings dialog.

## Troubleshooting

### Common Issues

**Recording does not start**
- Verify permissions for the output folder.
- Check available disk space.
- Ensure no other instance is running.

**Low performance**
- Close unnecessary applications.
- Monitor CPU and memory usage.
- Reduce screen resolution if needed.

**Overlays not visible**
- Check Windows DPI settings.
- Review multi-monitor configuration.
- Restart with administrator privileges if required.

### Logging
Runtime logs are written to `%TEMP%\screenrec.log`.

## Development

### Build Requirements
- Visual Studio 2019 or later
- .NET Framework 4.8 SDK
- Windows SDK for touch functionality

### Project Structure
```
RScreenRec/
├── Program.cs              # Entry point
├── ScreenRecorder.cs       # Core recording engine
├── MediaFoundationMp4Writer.cs # Native MP4/H.264 writer
├── RecordingOverlayForm.cs # Recording indicator overlay
├── TouchOverlayForm.cs     # Touch input overlay
├── RScreenRec.csproj       # Project configuration
└── README.md               # Documentation
```

### Build Commands
```bash
# Debug build (produces `RBG - RScreenRec.exe` in `bin\\Debug`)
msbuild RScreenRec.csproj /p:Configuration=Debug

# Release build (produces `RBG - RScreenRec.exe` in `bin\\Release`)
msbuild RScreenRec.csproj /p:Configuration=Release
```

## License

This project is distributed under the MIT License. © 2025 Paolo Rubagotti. See the `LICENSE` file for details.

## Contributing

Contributions are welcome! To contribute:
1. Fork the repository.
2. Create a feature branch (`git checkout -b feature/new-feature`).
3. Commit your changes with clear messages.
4. Open a pull request describing your changes.

## Support

If RScreenRec helps you work faster, you can buy me a coffee by sending a PayPal donation through the [direct contribution page](https://www.paypal.com/donate?business=paolo.ruba.1913%40gmail.com&no_recurring=0&item_name=Support+RScreenRec&currency_code=EUR).
