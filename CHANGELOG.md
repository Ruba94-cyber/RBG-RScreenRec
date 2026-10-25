# Changelog

## 1.3.2 — recording controls and visibility fixes

- Restore the blinking REC badge by removing an unused layered-window flag that prevented normal painting.
- Handle configured stop and pause keys inside the active recorder, including `Ctrl+Alt+R` and `Ctrl+Alt+P`, without requiring Explorer to launch another process. Remove the keyboard listener when recording ends.
- Split touch feedback into nonactivating tiles so no recorder window covers an entire monitor and suppresses Start-menu shortcut activation.
- Use native list rows and text-sized controls in the window picker to prevent overlapping entries and clipped buttons at high DPI.
- Apply the same text-sized layout to Settings, with a scrollable body and always-visible Save/Cancel actions.
- Refresh Windows shortcut registration on save, including when the same keyboard combination is saved again.
- Require actual keyboard input and rendered layout checks in the executable smoke; never substitute process launches for keyboard acceptance.

## 1.3.1 — settings, recording stop, and REC badge polish

- Keep the Ctrl+Shift settings dialog fully visible and resizable across display scaling and smaller work areas, and bring it to the foreground when opened.
- Clip the blinking REC badge to its rounded outline to remove the purple fringe around antialiased edges.
- Retry delivery of the stop event when a second launch races with recorder startup, so launching again reliably stops an active recording.

## 1.3.0 — keyboard controls, recording controls, and reliability

- Add Windows Explorer-backed, configurable per-user hotkeys for screen, window, and pause/resume modes without idle background processes.
- Verify persisted hotkeys, detect conflicts across RBG utilities, and roll back partially written shortcuts.
- Implement a pausable 30 FPS MP4 timeline and an amber paused-state badge.
- Reduce and make the REC badge draggable with remembered position and user-adjustable scale; log failures when exclusion from capture is unavailable.
- Improve window selection with title search and click-to-select; use effective per-monitor DPI for overlay positioning.
- Reuse RGB32 pixel buffers when window capture repeats the previous frame, avoiding redundant conversion.
- Add isolated Windows shortcut and settings tests and a pause/resume case in the interactive MP4 smoke suite.


## 1.2.0

- Added opt-in selected-window recording with Windows Graphics Capture and a warned PrintWindow compatibility fallback.
- Preserve the normal monitor start/second-launch stop workflow; report updated and repeated source frames.
- Use a 30 FPS H.264 quality profile tuned to retain screen text while keeping typical UI recordings compact.
- Keep the last complete selected-window image in the MP4 timeline while a target is minimized, then resume on restore.
- Added validation notes for unusual dimensions, occlusion, resize/move, minimize/restore, MP4 decoding, and long timelines.
- Added a Windows smoke runner for picker selection, synthetic WGC formats, occlusion, restore, source closure, MP4 decoding, and over-one-hour timeline checks.
- Tuned the H.264 quality profile to 60 after comparing encoded size and text fidelity on synthetic office UI; kept the 30 FPS output timeline.
- Added Paolo Rubagotti's author and contact email to the executable's Windows version metadata.
- Renamed the Windows product and executable to `RBG - RScreenRec` and prepared the x64 release package with a SHA-256 checksum.
