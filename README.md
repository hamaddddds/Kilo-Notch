<p align="center"><img src="docs/icon.png" width="96" alt="Kilo Notch icon"></p>

<h1 align="center">Kilo Notch</h1>

<p align="center">A living notch for Windows: music with word-synced lyrics, a focus timer, water reminders,<br>
a file shelf and to-dos, tucked into a little island at the top of your screen.</p>

<p align="center"><a href="https://hamaddddds.github.io/Kilo-Notch/"><b>Live preview</b></a> · <a href="https://github.com/hamaddddds/Kilo-Notch/releases/latest/download/KiloSetup.exe">Windows</a> · <a href="https://github.com/hamaddddds/Kilo-Notch/releases/download/v1.2.0/KiloNotch.apk">Android</a></p>

## What's inside

- **Music**: whatever Windows can control (Spotify, YouTube in the browser, Apple Music…). Cover art, seek bar and a live sound wave, both in the cover's own colors.
- **Lyrics**: syllable-timed lyrics from Better Lyrics' public cache, LRCLIB as a fallback, picked to match the song's own language. The sung line grows and glows; click a line to jump there.
- **Focus timer**: Pomodoro with a live ring and a chime; the page title shows your city.
- **Water reminder**: goal, glass size, active hours, hourly chart.
- **File shelf**: drag files onto the notch (light rises from below, the notch gulps them in), drag them back out with a photo preview under the cursor. Copy/link only, originals never move.
- **To-do**: tasks with a time get a reminder when due.
- **Peeks**: charging, headphones (with battery when the headset shares it), reminders.
- **Kilo**: the eyes that live in the notch. They follow the cursor, dance to your music when you step away, nap after five quiet minutes, cheer when you drink or finish a task, and sulk when the battery is low or a glass is overdue.
- **Photo booth**: Kilo winks like he's aiming the camera, counts down, takes three shots and prints a strip (Kilo, Film or Paper frame) under the notch. Copy it, or drag it into a chat. Saved to `Pictures\Kilo Booth`.
- **Themes**: six gradient presets, hue / gradient width / vibrance / glow sliders, dark or light, automatic text contrast.

## Android

`android/` is Kilo Notch for phones: an island on the camera hole, with Kilo's eyes on either side of the lens.
Native Kotlin and Jetpack Compose, ~1 MB, no internet permission.

- **Notifications** pop open the island with the sender's photo; tap to open, swipe up to dismiss. Respects Do Not Disturb, private on the lock screen, per-app mute.
- **Music**: cover and sound bars in the cover's colors; open it for seek and controls. Clock-app timers, stopwatches and calls keep counting in the island.
- **Water reminders** are exact alarms: on time with the app closed, in Doze, after a reboot. "Drink" works right from the island or the notification.
- **Focus timer** with a ring in the island and a lock-screen countdown; an alarm-clock alarm ends it.
- **Kilo** blinks and glances around, cheers when you drink, finish a session or plug in, sulks when the battery's low or a glass is overdue, sleeps at night.
- **Settings** only in the app (long-press the island): setup checklist, island size/position to fit your camera, colors, timer, water.

The island is an accessibility overlay, the only window type Android allows above the status bar that still takes taps.
It doesn't read the screen. Apps installed from an APK need *App info → ⋮ → Allow restricted settings* before
Android lets you switch it (and notification access) on.

Build (Android SDK with platform 37, JDK 17+):

```
cd android
gradlew assembleRelease
```
## Windows

Native WPF, with no browser engine. The x64 installer includes the .NET runtime and installs per-user.

### Desktop polish (1.3)

- Lyrics reveal to the **right** of the player, with reversible open/close motion and a gently staggered lyric scroll.
- Shelf has larger previews, **Add**, file actions on right-click, and **Undo** after clearing the shelf.
- To-do reminders offer **15 / 30 / 60 minutes**, tomorrow morning, or a day/hour/minute picker. In **Edit** mode, click a task to change its text or reminder, then **Save**. Past clock times roll forward to tomorrow.
- Photo Booth offers **Original, Warm, Mono, Vintage and Cool** filters. After taking a strip, choosing **Kilo / Film / Paper** or a filter automatically prints a new version from the original shots. Original shots remain in memory for this app session; every printed version is a separate PNG in `Pictures\Kilo Booth`.
- Kilo peeks while you type, winks on double-click, and has curious, surprised and low-power faces. Keyboard reactions inspect only key-down timing, never the keys or typed text; disable **Typing peek** in Settings.
- Battery warnings appear at **20% and 10%** when unplugged. **Gentle energy saver** dims Kilo's glow, stops idle dancing and caps the notch's frame loop near 30 fps when the battery is low or Windows energy saver is on. It does not change Windows power settings.
- **Camera reactions** in Settings or the tray explicitly enables local face presence/position detection. Kilo follows your face and greets you when you return; a green dot and tray state indicate that the camera is on. Turn it off from Settings/tray. It starts off on each launch and stops when Photo Booth starts. Photo Booth has its own **React to me** option that stops with its camera when the page closes. No recognition, emotion inference, recording or uploads are used for reactions.

## Installation and updates

### Animation and memory (1.3.1)

Active notch rendering targets 60 Hz with elapsed-time pacing that retains the fractional deadline on high-refresh displays. WPF animation timelines target 60 fps; the notch's energy-saving loop retains its 30 Hz limit. Hidden mascot and lyric views stop their recurring animation work. Visualizer geometries, drag glow brushes and lyric effects are reused. Only the current lyric neighbourhood animates; completed clocks are detached.

The lyric LRU holds at most 24 songs within a 180,000-character budget. Album images are decoded to at most 256 pixels on the longest edge. Filter/vision previews materialize independent 480/320 pixel images instead of cloning full camera frames. Original photo captures remain full-size. Camera stop releases its buffer and invalidates old queued callbacks.

There is no periodic forced GC or working-set eviction. Task Manager can therefore show a higher resident baseline than a freshly evicted process, while bounded buffers and caches reduce retained data and repeated allocations. CI checks frame scheduling, animation visibility, long-song seeks and resource lifetime; it does not establish a physical 60 fps guarantee or a universal RAM number. A display below 60 Hz, GPU load, camera drivers and software rendering can lower observed FPS.

Download `KiloSetup.exe` from Releases. The installer and uninstaller are native C++ executables drawn with Kilo's own Direct2D/DirectWrite UI. They install only to `%LOCALAPPDATA%\Programs\Kilo Notch`, register under HKCU, and preserve settings, tasks and photos. No administrator access is requested. Existing portable copies should be closed from their tray before installing.

Kilo checks [the stable JSON feed](https://hamaddddds.github.io/Kilo-Notch/updates/stable.json) after launch and every six hours. An update makes the mascot react and appears in Settings/the tray. Downloading verifies the release's SHA-256 checksum before opening the custom installer. Installation starts when you choose **Update Kilo**. A failed upgrade restores the previous application files. This release is unsigned.

## Development and quality checks

**Do not build, restore, package or run desktop/installer executable tests locally.** All Windows compilation and executable tests run in GitHub Actions. Local QC is source-only:

```powershell
pwsh -File scripts/qc.ps1
```

A static website preview is allowed: `node scripts/preview.mjs`.

The `Kilo quality and release` workflow runs source checks, WPF behavior/render smoke checks, then builds the self-contained app and custom installer. Installer tests cover fresh install, failed upgrade rollback, locked files, corrupt packages, foreign destinations and uninstall data preservation. Release assets and the matching JSON feed publish only after the quality job succeeds. Existing release assets are immutable; increment the version for a new desktop release. Visual test renders are CI artifacts, never committed to Git.

For hardware verification on a CI-built release: try typing in another app; toggle Camera reactions and check the indicator goes off; take a strip and switch themes; use Settings → Try it → Low battery. Automated QC cannot replace hardware testing or guarantee zero bugs.

## Privacy

No accounts, no analytics. Tasks, water log, shelf and settings live in `%APPDATA%\Notch\data.json`.
While the lyrics view is open, the song's title, artist and length go to Better Lyrics / LRCLIB. With "Show my city" on,
your position is rounded to ~1 km before OpenStreetMap's Nominatim names the place.
The photo booth's camera runs only after you press "Start camera" and stops as soon as the notch closes; photos never leave your PC. Optional Camera reactions keeps the camera running until you turn it off in Settings/tray or open Photo Booth's camera. Detection stays on this PC and its frames are not saved.

## Credits

Lyrics from [Better Lyrics](https://betterlyrics.org) and [LRCLIB](https://lrclib.net). Place names © [OpenStreetMap](https://www.openstreetmap.org/copyright) contributors.
