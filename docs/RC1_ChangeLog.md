This change log only covers changes between release candidate 1 and public release.
This is a logged issue tracked for RC1.

| Issue | Resolution |
|---|---|
| UI display scale not capturing / applying on some Macs (high-res first launch too small) | Re-read display scale after the window is mapped; combine OS scale, max scale, and DPI/size fallback. First-run window grows to a usable fraction of the screen. |
| Memory leak from Godot input (and StyleBoxes) on quit after dragging UI scale | Stop wrapping every mouse-motion in C# `_Input`; key-only handlers use `_UnhandledKeyInput`. Dispose C#-created StyleBoxes on teardown. |
| Default show save location sometimes a read-only folder | Save As opens in the current show folder, else last save folder, else a writable `Documents/Cue2` (Desktop / `user://Shows` failover). |
| Linux: assigning a canvas screen to a display removed the main UI window | House-screen placement never drives the operator window id; Wayland opens a movable output instead of covering the UI. |
| “Open last showfile” hung or left an empty workspace when that file was gone | Missing last-show path is dropped from recents; boot seeds a blank new session and logs an error. |
| Linux: canvas editor stage sometimes ignored clicks / resize handles | Stage picking uses a pointer overlay and canvas-space mouse coords, not raw viewport position. |
| LineEdits showed typed text that was never applied (click into another field) | Focus-loss commits the same as Enter in the canvas editor and other leftover settings fields. |


