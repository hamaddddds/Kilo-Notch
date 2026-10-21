<p align="center"><img src="docs/icon.png" width="96" alt="Kilo Notch icon"></p>

<h1 align="center">Kilo Notch</h1>

<p align="center">A living notch for Windows: music with word-synced lyrics, a focus timer, water reminders,<br>
a file shelf and to-dos, tucked into a little island at the top of your screen.</p>

<p align="center"><a href="https://hamaddddds.github.io/Kilo-Notch/"><b>Live preview</b></a> · <a href="https://github.com/hamaddddds/Kilo-Notch/releases/latest/download/KiloNotch.exe">Windows</a> · <a href="https://github.com/hamaddddds/Kilo-Notch/releases/latest/download/KiloNotch.apk">Android</a></p>

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
## Lightweight

Native WPF, no browser engine. It draws only while something moves, and while collapsed it hands idle memory
back to Windows: about 35 MB in Task Manager even with the visualizer running. One ~25 MB exe.

## Run

Needs Windows 10 2004+ or Windows 11 and the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0).

```
dotnet run
```

Build the single-file exe:

```
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

Right-click the notch or the tray icon for settings, "Start with Windows" and exit.

## Privacy

No accounts, no analytics. Tasks, water log, shelf and settings live in `%APPDATA%\Notch\data.json`.
While the lyrics view is open, the song's title, artist and length go to Better Lyrics / LRCLIB. With "Show my city" on,
your position is rounded to ~1 km before OpenStreetMap's Nominatim names the place.
The photo booth's camera runs only after you press "Start camera" and stops as soon as the notch closes; photos never leave your PC.

## Credits

Lyrics from [Better Lyrics](https://betterlyrics.org) and [LRCLIB](https://lrclib.net). Place names © [OpenStreetMap](https://www.openstreetmap.org/copyright) contributors.
