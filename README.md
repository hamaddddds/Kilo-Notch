<p align="center"><img src="docs/icon.png" width="96" alt="Kilo Notch icon"></p>

<h1 align="center">Kilo Notch</h1>

<p align="center">A living notch for Windows: music with word-synced lyrics, a focus timer, water reminders,<br>
a file shelf and to-dos, tucked into a little island at the top of your screen.</p>

<p align="center"><a href="https://hamaddddds.github.io/Kilo-Notch/"><b>Live preview</b></a> · <a href="https://github.com/hamaddddds/Kilo-Notch/releases/latest">Download</a></p>

## What's inside

- **Music**: whatever Windows can control (Spotify, YouTube in the browser, Apple Music…). Cover art, seek bar, live sound wave.
- **Lyrics**: syllable-timed lyrics from Better Lyrics' public cache, LRCLIB as a fallback, picked to match the song's own language. The sung line grows and glows; click a line to jump there.
- **Focus timer**: Pomodoro with a live ring and a chime; the page title shows your city.
- **Water reminder**: goal, glass size, active hours, hourly chart.
- **File shelf**: drag files onto the notch (light rises from below, the notch gulps them in), drag them back out with a photo preview under the cursor. Copy/link only, originals never move.
- **To-do**: tasks with a time get a reminder when due.
- **Peeks**: charging, headphones (with battery when the headset shares it), reminders.
- **Personality**: eyes that follow the cursor, blink, doze off and say hi.
- **Themes**: six gradient presets, hue / gradient width / vibrance / glow sliders, dark or light, automatic text contrast.

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

## Credits

Lyrics from [Better Lyrics](https://betterlyrics.org) and [LRCLIB](https://lrclib.net). Place names © [OpenStreetMap](https://www.openstreetmap.org/copyright) contributors.
