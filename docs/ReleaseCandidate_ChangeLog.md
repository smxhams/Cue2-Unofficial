This change log only covers changes between release candidate 1 and public release. Post public release all changes go into the main changelog.

## Brief

| Issue | Resolution |
|---|---|
| UI display scale not capturing / applying on some Macs (high-res first launch too small) | Re-read display scale after the window is mapped; combine OS scale, max scale, and DPI/size fallback. First-run window grows to a usable fraction of the screen. |
| Memory leak from Godot input (and StyleBoxes) on quit after dragging UI scale | Stop wrapping every mouse-motion in C# `_Input`; key-only handlers use `_UnhandledKeyInput`. Dispose C#-created StyleBoxes on teardown. |
| Default show save location sometimes a read-only folder | Save As opens in the current show folder, else last save folder, else a writable `Documents/Cue2` (Desktop / `user://Shows` failover). |
| Linux: assigning a canvas screen to a display removed the main UI window | House-screen placement never drives the operator window id; Wayland opens a movable output instead of covering the UI. |
| “Open last showfile” hung or left an empty workspace when that file was gone | Missing last-show path is dropped from recents; boot seeds a blank new session and logs an error. |
| Linux: canvas editor stage sometimes ignored clicks / resize handles | Stage picking uses a pointer overlay and canvas-space mouse coords, not raw viewport position. |
| LineEdits showed typed text that was never applied (click into another field) | Focus-loss commits the same as Enter in the canvas editor and other leftover settings fields. |
| OSC TCP send connections had no live status; a failed connect was easy to miss | TCP rows show a color status square (connecting / connected / failed) and a retry button when the session is down. |
| Closing Settings logged a Godot disconnect error on the OSC Listen allowlist field | Allowlist is wired with BindLineEditCommit; close no longer unhooks a method that was never connected. |
| Exported builds had no in-app update path | Settings → Cue2 Preferences → Updates checks GitHub Releases, verifies SHA-256, and can Install and Restart when the install folder is writable. |
| `.c2` showfiles had no OS icon and did not open Cue2 on double-click | Exported builds register `.c2` as a Cue2 Show (app icon). Double-click / Open With loads that file; a second launch forwards to the running instance. |
| Quit / New / Open replaced the session with no warning | Unsaved document edits prompt Save & close, Close, or Cancel (UI-scaled). |
| Active cue list wasted vertical space | Cue head, component rows, and list gaps are packed tighter so more live cues fit. |

## Details

### Mac high-res first-launch UI / window scale

On some HiDPI Macs the first open left the main window and UI at the 1152×648 design size. `BaseDisplayScale` was read once in autoload `_Ready` via `ScreenGetScale` only. That call often returns `1.0` before the native window is on a screen, and Godot’s macOS backend sizes windows with max scale, not the current-screen scale. First-run size was then just design × that factor, which is still small on 5K/6K even when scale is correctly 2×. The tiny size was saved immediately, so later launches restored it.

Scale detection now takes the max of `ScreenGetScale`, `ScreenGetMaxScale`, and a DPI/resolution guess used only when the OS still reports ~1× (1× 4K/5K, mixed-DPI, or scale not ready yet). GlobalData re-reads scale after the display server settles, before window finalize and the welcome dialog. First-run geometry is design size × scale, then grown to about 58–88% of the usable screen (below the menu bar) and centered. Leftover unscaled 1152×648 saves are ignored when real HiDPI scale is >1, so existing installs recover. `allow_hidpi` is explicit in project settings.

### Godot C# input / StyleBox leak on shutdown

After Settings → Cue2 Preferences → drag UI scale → quit, the log reported `Leaked unsafe reference` for a burst of `InputEventMouseMotion`, the close-button click, `StyleBoxFlat` / `StyleBoxEmpty` pairs, then `FATAL: !rc_owner`.

Dragging the slider generates many mouse-motion events. Several C# nodes overrode `_Input` / `_UnhandledInput` even when they did not need pointer events (`CueList` reorder/marquee always on, Settings filter popup, title-bar menu, InputActionsListener and inspectors that only care about keys). Godot Mono wraps each event; quitting immediately after a drag leaves that last batch un-GC’d. Separately, C# `new StyleBoxFlat()` / `new StyleBoxEmpty()` on shell rows, global chrome, and the window border were never disposed, so they died during engine teardown.

Pointer `_Input` is now enabled only while reorder/marquee, the settings filter menu, or the hamburger menu is actually tracking the mouse. Key-only listeners use `_UnhandledKeyInput`. Owned StyleBoxes and duplicated default InputMap events are disposed in `_ExitTree` / predelete.

### Showfile Save As start directory

With no current show path, the Save As dialog used Godot’s default folder (often the app/install directory), which is read-only for packaged Windows and macOS builds.

Start folder is now: (1) the open show’s directory when a session is already on disk; (2) else `LastShowSaveDirectory` in `user://user_data.json`; (3) else a writable first-run default. First-run order is `Documents/Cue2`, Documents, `Desktop/Cue2`, Desktop, then Godot `user://Shows`, then `user://`. Each candidate is created if missing and probed for write access. The remembered last location is the parent of the session folder (`…/MyShow/MyShow.c2` → `…`), so a later new show does not open inside the previous show’s folder. A leftover Save dialog handler that set a bad `SessionPath` and fired Save a second time was removed.

### Linux canvas screen assignment hid the main window

On Linux only, assigning a canvas screen to a physical display could make the operator UI disappear. Linux embeds OptionButton popups in `/root`, and an embedded child window’s `GetWindowId()` is the main viewport id (`0` — a valid id, not “missing”). DisplayServer then applied borderless full-monitor geometry to the Cue2 window. On Wayland, window position and current-screen are also no-ops, so a full-size house screen opened on the focused (operator) output and covered the UI.

Output windows now refuse DisplayServer calls unless they have their own native id. `CurrentScreen` is not set until that id exists. After placement, the main window’s mode, chrome, and rect are restored if they changed. Wayland cannot target a monitor, so physical outputs open as a smaller decorated window to drag onto the house display, with a log note. Windows, macOS, and X11 still place a borderless window on the chosen display.

### Missing last showfile on startup

With “Open last showfile” enabled, boot took the first recent path even when the file was gone. That skipped default screen and audio-patch seeding, showed the load overlay, then failed with “file not found” and never dismissed the overlay or built a usable empty show.

Startup now checks that the last path exists before starting the open pipeline. A missing file is removed from recents, `StartupOpenPath` stays empty, and boot seeds a normal new session. If the file disappears after that check (or the read fails), the overlay is closed and the session is reset to a blank show. File → Open of a missing path still only logs an error and leaves the current show alone.

### Linux canvas editor stage ignored mouse

Resize and move handles on the canvas editor stage sometimes did nothing on Linux. Hit-testing compared `GetGlobalRect()` (canvas / content-scale space) with `GetViewport().GetMousePosition()` (raw viewport pixels). On a native Settings window with UI scale those spaces only match at 1× in a lucky position, so the whole center stage looked like a miss. Decorative SubViewport nodes (`ColorRect`, outline panel) also used `MouseFilter = Stop` and could swallow GUI events.

A full-rect pointer overlay now sits on the stage and owns `GuiInput`. Clicks, handles, wheel-zoom, and hover use that overlay’s local coordinates. Global `_Input` stays on only during an active drag or pan so a gesture can finish after leaving the stage. Decorative stage nodes ignore the mouse. Resize handles have a larger hit slop.

### LineEdit click-away did not apply

Canvas editor property fields (and a few other settings fields) only applied on Enter. Clicking another LineEdit left the typed text on screen but did not write the model, so the field showed a value that was not saved.

`UiUtilities.BindLineEditCommit` now runs the same apply handler on Enter and on focus-loss. Handlers stay no-ops when the value did not change, so Enter does not create a second undo step. Wired on canvas size, zoom %, screen/layer name, size, position, and display offset; Cue2 Preferences and first-time welcome UI scale; cue-light brightness, name, and IP; and the OSC listen allowlist. Inspectors, the cue list, and cue/audio/video/text defaults already committed on focus-loss.

### OSC TCP connection status and retry

UDP send connections are connectionless; TCP is a session. Switching a named OSC connection to TCP started a background connect, but the Settings row gave no indication of connecting / ready / failed, and there was no way to retry without toggling transport or the destination.

Each connection row now has a status square (same pattern as Cue Lights) and a refresh button. TCP: amber while connecting, green when the session is open, red when it is down (tooltip includes the last error). Retry is enabled only for TCP when not connected and not already connecting; it calls the existing non-blocking reconnect. UDP keeps a muted square (no session) and a disabled retry so columns stay aligned. The Interface dropdown remains disabled for TCP because the TCP sender does not bind a local NIC — the OS routing table picks the source. Status updates refresh in place so a TCP connect no longer rebuilds the whole row list (which dropped LineEdit focus).

### Settings close: OSC Listen allowlist disconnect error

Closing Settings printed `Attempt to disconnect a nonexistent connection` on `AllowlistLineEdit` `text_submitted`. `_Ready` wires that field with `BindLineEditCommit` (a lambda on Enter and focus-loss). `_ExitTree` then did `TextSubmitted -= OnAllowlistTextSubmitted`, which was never the connected callable.

That unhook is removed. The BindLineEditCommit hooks die with the node, matching Cue2 Preferences and the canvas editor.

### In-app updater (exported builds)

Cue2 Preferences → Updates checks [Tech-mop/Cue2](https://github.com/Tech-mop/Cue2) GitHub Releases. Auto-check runs a few seconds after launch in exported builds (opt-out in prefs); the editor does not check. The footer shows “update available” and opens this panel; there is no blocking startup dialog.

Stable checks use `latest.json` on `/releases/latest/download/` (no REST quota). “Include pre-release versions” still reads that file and only prefers the GitHub API when a prerelease is actually newer. In-app download requires HTTPS on GitHub plus SHA-256 of the archive; a mismatch deletes the zip. Install and Restart is blocked while cues are playing. If the install folder is not writable (Program Files, read-only DMG), Cue2 reveals the verified archive in the file manager instead of swapping files.

The quit-and-copy helper waits for Cue2 to exit, retries Windows overwrites (Defender/file locks), treats `robocopy` failure as abort (no relaunch of a half-copied tree), and on macOS copies to a sibling `.app.new` then swaps so a failed `ditto` does not `rm -rf` the live bundle. Linux relaunch prefers `Cue2` / `Cue2.x86_64` and never a `.pck`. Stale versioned hosts from older exports are removed when the new payload does not include them. Failures after quit are logged under the OS temp folder `Cue2Update/apply-update.log`. Inner binaries in release zips must stay `Cue2.exe` / `Cue2.x86_64` / `Cue2.app` — see `docs/github-releases.md`.

### `.c2` showfile icon and double-click to open

`.c2` was only a save/open filter inside Cue2. The OS did not know the type, a path on the command line was ignored, and double-clicking a show started nothing (or a second Cue2 that would fight OSC, MIDI, and audio devices).

Exported builds now treat `*.c2` as a Cue2 Show and stamp the **app icon** on the file:

- **macOS:** Info.plist document type / UTI `live.cue2.show` (export preset). Launch Services registers on first launch of the notarized `.app`. The macOS export icon is filled in so Finder has something to show.
- **Windows:** per-user registry (`Cue2.Showfile`, `DefaultIcon` = `Cue2.exe,0`, open command `"Cue2.exe" "%1"`). No admin. First launch registers if `.c2` is unassociated.
- **Linux:** user XDG MIME `application/x-cue2`, a `.desktop` with an absolute `Exec`, and hicolor icons generated from `icon.svg`.

A `.c2` on the command line wins over “Open last showfile” and loads through the existing `SaveManager` path. Dropping a `.c2` on the main window (or on the cuelist with no media files) does the same. A second launch of an exported build forwards the path to the running process and exits (editor instances are not limited). Cue2 Preferences → **Associate .c2 files with Cue2** repairs the association after moving a portable folder. The Godot editor does not write registry/xdg entries. See `docs/export-packaging.md`.

### Unsaved changes on session close

New, Open, Open Recent, OS double-click / drop of a `.c2`, Quit, and the window close button replaced the live session immediately. Document edits (cues, list structure, show settings) were only in memory and the undo stack.

Those actions now check document history. If the show has changed since the last save (or since New / Open), a UI-scaled dialog offers **Save & close**, **Close** (discard), or **Cancel**. Save & close writes in place when there is a path; otherwise it opens Save As and continues only after a successful write. Newer-format shows that cannot overwrite the original also go through Save As. An empty new session or a freshly opened file does not prompt until something is edited. Autosave of the main `.c2` counts as saved. Startup “open last showfile” does not prompt. New Session no longer clears the file path before you can choose Save.

### Tighter active cue list

Each playing cue used a 50px two-line head (name and times stretched to fill it), 25px component rows, 4px gaps between cues, and extra panel padding on pre-wait / post-wait / continue bars. Nested children added another 4px between rows. With several cues running, the Active Cues pane filled up fast.

The cue head is now 36px and the name/time lines sit together instead of spreading apart. Component rows match the 20px wait/continue bars. Gaps are 2px between cues and 1px under nested children. Wait and sequence wrappers no longer pick up default panel padding. Playback, scrub, and pause/stop controls are unchanged.
