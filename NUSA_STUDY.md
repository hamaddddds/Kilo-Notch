# Nusa rendering study — Kilo 1.3.1

Static inspection with REA/Ghidra 12.1.4, 23 October 2026. Neither the supplied installer nor the extracted application was executed. Raw extracts and evidence bundles remain under ignored `artifacts/nusa-analysis/`; they are not release assets.

## Artifact identity and coverage

| Input | Size | SHA-256 |
| --- | ---: | --- |
| NusaSetup.exe | 2,809,698 bytes | `63170651afbabd0aefce1a011b01a8fb28c887790e3463da5952e2236c533c99` |
| Extracted nusa.exe | 909,312 bytes | `2dfa517fe20fb944cd263597e9f731b8245fc2b5253d9f1a241221636e8d1bdb` |

The Inno package was unpacked with the maintainer's innounp 2.71.2. It contains one native x64 application, installer artwork, installer language/code data and the recovered installation script. Its application resource tree contains 15 PNG logo sizes, one icon group and one version resource. All 17 resource payloads were extracted and indexed. No separate animation sprite files were present in that package inventory. Native procedures were inspected selectively; this is not recovery of all 1,712 analyzed functions or original source.

## Findings and evidence

| Finding | Evidence | Confidence / limit |
| --- | --- | --- |
| Application resources are logo variants, not a collection of music-control or motion frames. | `ev_a0557ad9e8e07919f499221ecff3d2df77a9387c16df8a875816a0b76687aa4c` | Observed resource inventory and extracted largest PNG. Native drawing code can contain additional shapes. |
| Rust and Windows rendering API strings include Direct2D/DirectWrite. | `ev_b66e87083af55921bdcc918ef9a9ccff5ba0f96ddd245d4b21d9b95ce08ac49b` | Observed strings, not a runtime GPU trace. |
| A lyric-related constructor retains text formats/brush references and creates status text. | `ev_1b659ac3325cf573f41f3c9cea8df84a76e88fbf6a291d82a42a7cf8ed722784` | Derived from constructor pseudocode and the lyric-status string reference. Original types/names are missing. |
| An activity predicate checks state flags, position errors below 0.002 and velocity-like values below 0.05; it also checks a lyric-related subsystem. | `ev_31fed15b7987388b857520fc710f9fd5d592a7561b2aa2a25adeb57f5aedb338`, `ev_65d48e8796905c003e715d2f982d7fa57a2903d42e765293dc0c22e4e6803993` | Settling/activity detection is inferred from comparisons and callers. Actual scheduling rate was not measured. |
| State transitions avoid repeating the same target and set activity/time fields when work becomes active. | `ev_a43862f9f13f91d8d7f42405601b0710cc3e52eafac019f836a9229701098f42`, `ev_b0fc4677121de949309fbeaf1eef756c2f2ef36291b63f597c23ff7b16ceadb6` | Derived. Float pairs 110/15, 200/29 and 230/19 are candidates for motion parameters, not verified spring constants. They were not copied into Kilo. |
| A helper reached from these transitions updates the window region. | `ev_be43f5c6282268c078757f0752b6d31b589b5f792c275e237d0c92e243b12316` | Observed CreateRectRgn/SetWindowRgn calls; this helper must not be described as a frame timer. |
| One toggle starts/stops a 50 ms Windows timer. | `ev_d93fbfe95f3804094c862a90ae8e5e0d4c41991e72a901055bd4795c87e0444e` | Observed timer argument; its exact feature role is unresolved. It does not prove a 20 fps render rate. |

## Kilo implementation

Kilo keeps its WPF renderer and branding. The changes apply retained drawing resources, bounded image/cache ownership, stopping hidden work, releasing completed lyric clocks and elapsed-time frame pacing. Music controls are newly authored custom vector paths. The extracted Nusa logo and native machine code are not embedded in Kilo.

CI exercises 60 Hz scheduling with synthetic 60/120/144/240 Hz timestamps, 30 Hz energy-saving scheduling, duplicate timestamps and stalls; warmed frame-pacer allocation; reusable visualizer geometry; hidden mascot clocks; independent reduced camera images; bounded album decoding and lyric caches; and forward/backward seeks through 1,000 lyric rows. These checks establish specific code properties, not physical presentation performance.

Unresolved without hardware profiling: actual Nusa/Kilo FPS, frame-time percentiles, GPU utilization, working set under sustained real playback, and webcam-driver behavior. The change does not promise constant 60 fps on every device or a fixed RAM figure. Original photos remain full-resolution for reprinting; lyric rows are still laid out in full, with animation work restricted to nearby rows.
