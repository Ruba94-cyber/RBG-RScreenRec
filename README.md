<p align="center">
  <img src="Icon_screenshotrecord.ico" width="112" alt="RBG - RScreenRec official application icon" />
</p>

<h1 align="center">RBG - RScreenRec</h1>

<p align="center">
  <strong>Screen and window recording for Windows, made simple.</strong><br />
  Pick a monitor or an application window, record your work, and save a ready-to-share MP4.
</p>

<p align="center">
  <a href="https://github.com/Ruba94-cyber/RBG-RScreenRec/releases/latest"><strong>Download latest release</strong></a> ·
  <a href="#quick-start">Quick start</a> ·
  <a href="#keyboard-shortcuts">Shortcuts</a> ·
  <a href="CHANGELOG.md">What's new</a> ·
  <a href="#support-development">Donate</a>
</p>

<p align="center">
  <img alt="Windows" src="https://img.shields.io/badge/Windows-desktop-263238?logo=windows&logoColor=white" />
  <img alt=".NET Framework 4.8" src="https://img.shields.io/badge/.NET_Framework-4.8-512BD4" />
  <img alt="Output MP4 H.264" src="https://img.shields.io/badge/Output-MP4%20%7C%20H.264-24786E" />
  <img alt="MIT license" src="https://img.shields.io/badge/License-MIT-4F6573" />
  <img alt="Latest release" src="https://img.shields.io/github/v/release/Ruba94-cyber/RBG-RScreenRec?label=release" />
</p>

## Why RScreenRec?

**RBG - RScreenRec** focuses on a practical everyday recording workflow: start capturing the screen under your cursor, or select a specific application window, without setting up a complex video-production tool.

Useful for **software demos, bug reports, tutorials, walkthroughs and documentation**.

| Capability | What you get |
| --- | --- |
| **Monitor recording** | Select a screen simply by placing the mouse pointer on it before launching. |
| **Application-window recording** | Choose a window by title or click-to-select, avoiding unrelated overlapping windows. |
| **MP4 video** | H.264 output at **30 FPS**, designed for easy sharing. |
| **Pause / resume** | Skip interruptions without extending the recorded timeline with a frozen pause. |
| **Cursor & touch feedback** | Visual pointer and touch indications in screen recordings. |
| **Recording status** | Movable, resizable **REC** badge that changes to a pause indicator when recording is paused. |
| **Optional hotkeys** | Customizable shortcuts, including start/stop and pause/resume. |

## Download

**[Download RBG - RScreenRec for Windows](https://github.com/Ruba94-cyber/RBG-RScreenRec/releases/latest)**

1. Download the **Windows x64 ZIP** from the latest GitHub release.
2. Extract the archive to a folder you can keep.
3. Launch **`RBG - RScreenRec.exe`**.

**Requirements:** Windows and **.NET Framework 4.8**. There is no installer wizard or separately installed video encoder required by the distributed application.

For authenticity and integrity, use the checksums and certificate information attached to the release. Check the published fingerprint before trusting a signing certificate.

## Quick start

### Record a monitor

1. Move your mouse cursor onto the monitor you want to record.
2. Launch **`RBG - RScreenRec.exe`** normally.
3. Record the content you need. A small on-screen **REC** badge indicates recording.
4. Launch the executable again to **stop and save**, or use a configured stop shortcut.

### Record one application window

1. Hold **Shift** while launching the executable.
2. Select an application from the window picker. You can search its title or use **Click a window...**.
3. Continue working; the recording targets the selected window, rather than the rest of your desktop.
4. Launch the executable again or use a configured shortcut to finish.

**Where is the video?** MP4 files are saved automatically to:

`%USERPROFILE%\Videos\Captures\rec_[timestamp].mp4`

### Quick-reference controls

| Action | How |
| --- | --- |
| Start recording a monitor | Launch normally, with your cursor on the target monitor |
| Select a window to record | Hold **Shift** while launching |
| Stop and save | Launch the executable again, or use your configured stop key |
| Open settings | Hold **Ctrl + Shift** while launching |
| Pause / resume | Use the configured pause shortcut |
| Move the REC badge | Drag it; the position is remembered |

## Keyboard shortcuts

Keyboard shortcuts are **optional**. Open Settings with **Ctrl + Shift** while launching, configure the keys you want, then select **Save settings**.

| Suggested shortcut | Action |
| --- | --- |
| `Ctrl + Alt + R` | Start / stop monitor recording |
| `Ctrl + Alt + W` | Start / stop application-window recording |
| `Ctrl + Alt + P` | Pause / resume a recording |

> [!NOTE]
> Shortcuts are not active until you save them. When enabled, a small background helper starts with Windows to make the shortcuts available between recordings. Clear all shortcut assignments and save to disable it. If you move the executable to another folder, save the shortcuts again so they point to the correct location.

## Settings and recording tips

- Adjust the size of the **REC** indicator and move it out of the way.
- When recording a specific application, restore it before selecting it.
- If the target window is **minimized**, Windows may stop supplying new frames. RScreenRec retains the last available image until capture can resume.
- Some **protected, accelerated or GPU-rendered windows** may not be capturable. Window capture may fall back to a compatibility method, with different results.
- If a shortcut stops working after a version update, check whether it still targets an older extracted ZIP folder, then open Settings and save it again.

## Build from source

RScreenRec is a **.NET Framework 4.8** Windows desktop project. Use a Windows development environment with compatible Visual Studio / MSBuild tooling and the required targeting pack.

From the repository root, the project includes a build-check script:

```powershell
.\scripts\build-check.ps1
```

The repository also contains Windows recording, shortcut and settings smoke-test utilities under `tests/` and `scripts/`. Some tests need an interactive desktop and capture-capable Windows session. See the [changelog](CHANGELOG.md) for the latest improvements.

## Feedback & contributions

Found a problem or have an idea? **[Open a GitHub issue](https://github.com/Ruba94-cyber/RBG-RScreenRec/issues)** with your Windows version, application release, steps to reproduce and expected result. For capture bugs, use **non-confidential sample content** only.

## Support development

RScreenRec is independently developed. Donations are completely optional and help support maintenance, testing and future improvements.

**[❤️ Support RScreenRec with a PayPal donation](https://www.paypal.com/donate?business=paolo.ruba.1913%40gmail.com&no_recurring=0&item_name=Support+RScreenRec&currency_code=EUR)**

## License

Released under the **[MIT License](LICENSE)**. Copyright © Paolo Rubagotti. See the license file for the full terms.

---

<p align="center"><sub>Made by Paolo Rubagotti · <a href="https://github.com/Ruba94-cyber/RBG-RScreenRec/releases/latest">Download</a> · <a href="https://github.com/Ruba94-cyber/RBG-RScreenRec/issues">Feedback</a> · <a href="#support-development">Support the project</a></sub></p>
