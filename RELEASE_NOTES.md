Kilo 1.3.1 focuses on animation pacing and memory lifetime. The notch's active render work targets 60 Hz even on 120/144/240 Hz displays, while energy saver keeps its 30 Hz notch limit. Sleeping and sad mascot loops stop when hidden.

Lyrics remain on the right, with staggered transitions confined to the nearby lines, reused effects and completed animation clocks released. Music controls now use custom vector icons. Visualizer shapes and drag/drop glow brushes are reused instead of recreated each frame.

The lyric cache is bounded to 24 songs and a character budget. Album art decodes to at most 256 pixels on its longest edge. Filter previews and camera reactions use independent 480/320 pixel snapshots without first cloning the full camera frame; original photo captures retain their resolution. Capture buffers are released when the camera stops.

Periodic forced full garbage collection and working-set eviction are removed to avoid reopen stalls. Memory is managed through bounded resources and background workstation GC. Physical FPS, CPU and RAM depend on hardware and workload; a target rate is not a measured guarantee. All executable checks and packaging run in GitHub Actions.

Includes the existing custom per-user installer/uninstaller and SHA-256-verified JSON updater. Settings, tasks and photos survive upgrades and removal.

Download **KiloSetup.exe** for installation, or **KiloNotch.exe** for portable use. Windows 10 version 2004 or later, x64. Camera reactions start disabled. This release is unsigned; Windows may show its own security prompt when launching a download.
