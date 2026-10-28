# RBG - RScreenRec

**Simple screen and window recording for Windows.** Choose a monitor in a multi-display setup or record a specific application window, then save directly to MP4.

## Download

[Download the latest release](https://github.com/Ruba94-cyber/RBG-RScreenRec/releases/latest), extract the ZIP, and run `RBG - RScreenRec.exe`.

Requires Windows and .NET Framework 4.8. No separate video encoder or installation wizard is needed.

## Features

- **Multi-monitor recording:** move your mouse onto the display you want to record, then start; only that monitor is captured.
- **Screen recording:** includes cursor and touch highlights on the selected monitor.
- **Window recording:** captures only the selected application window, without other overlapping windows or desktop notifications. Continue using other apps while recording.
- **Ready-to-share MP4:** H.264 video at 30 FPS.
- **Pause and resume:** skip breaks without adding frozen time to the video.
- **Recording indicator:** a draggable REC badge remembers its position and changes to PAUSE when paused; adjust its size in Settings.

## How to use

| Action | What to do |
| --- | --- |
| **Record a specific monitor** | Move the cursor onto that monitor, then open `RBG - RScreenRec.exe` normally. |
| **Record a window** | Hold **Shift** while opening the app, then select a window, search by title, or use **Click a window...** |
| **Stop and save** | Open the executable again, or press a configured recording shortcut. |
| **Open Settings** | Hold **Ctrl + Shift** while opening the executable. |

Videos are saved automatically to `%USERPROFILE%\Videos\Captures\` as `rec_[timestamp].mp4`.

### Optional keyboard shortcuts

Open Settings, choose your shortcuts, and select **Save settings** to enable them. Suggested combinations:

| Shortcut | Action |
| --- | --- |
| `Ctrl + Alt + R` | Start/stop screen recording |
| `Ctrl + Alt + W` | Start/stop window recording |
| `Ctrl + Alt + P` | Pause/resume the current recording |

Shortcuts are customizable and **are not enabled until you save them**. A small background helper keeps them working when the recorder is closed and starts with Windows. Clear all assignments and save to disable it. If you move the executable, save your shortcuts again.

**Window recording limitation:** Restore a minimized window before selecting it. When a recorded window is minimized, Windows may stop updating its content: RScreenRec keeps the last available frame until the window is restored. Some protected or GPU-rendered windows may not capture correctly.

## Support the project

Enjoying RScreenRec? [Support its development with a PayPal donation](https://www.paypal.com/donate?business=paolo.ruba.1913%40gmail.com&no_recurring=0&item_name=Support+RScreenRec&currency_code=EUR).
