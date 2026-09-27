# Verification — 2026-09-27

Windows x64, local .NET SDK 10.0.401, Release builds. Hardware/performance baseline collection pending.

## Executed

- User manually confirmed MD and TXT files work on 2026-09-27. This confirms those user checks, not the full accessibility or corpus checklist.
- `scripts/test-markdown.ps1 -Offline`: 14 new regressions passed: visible entities, safe decoded text, soft/hard breaks, email targets, combined emphasis, literal inline/display math, currency, code, unsafe links/images and front-matter offsets. Original 22 tests and six WPF smoke fixtures rerun successfully after this change.

- `scripts/build.ps1 -Offline`: zero warnings/errors. WPF experimental runtime-theme diagnostic narrowly suppressed beside its use.
- `scripts/test.ps1 -Offline`: 22 core tests passed, 0 failed. CSV quoting/multiline/limits, encoding, unsafe links, Markdown HTML/images/tables/tasks/front matter/Mermaid, DTD rejection, ZIP limits/traversal, local paths, source hashes, shared access, wrong signatures, empty text, explicit CSV truncation.
- `scripts/smoke-test.ps1`: six fixtures passed actual worker and WPF views. Windows instantiated and laid out without being shown; rendered-search selection, zoom, source toggle and grid row count checked.

Successful worker tests exercise Job Object assignment. They do not prove memory termination, timeout/cancellation recovery, low-privilege isolation or lack of network access under instrumentation.

## Not run or measured

- Human visual inspection, Narrator, keyboard-only flow, high contrast and mixed-monitor scaling.
- PDF/Office/independent reference tests and large performance corpus.
- Observed network/filesystem instrumentation for hostile files.
- Cold/warm benchmarks and peak process memory.
- Installer, clean offline installation, uninstall, signing and Explorer registration.

Compilation timings and the loading indicator are not performance benchmarks. Installer and installed sizes unavailable: no installer exists.

## Manual checklist

- [ ] Open/drop; new window per document.
- [ ] Keyboard, copy, next/previous search.
- [ ] System/light/dark, high contrast, Narrator.
- [ ] 100–300% scaling and mixed monitors.
- [ ] Encoding/delimiter correction and CSV truncation notice.
- [ ] Inert unsafe Markdown references and link confirmation.
- [ ] Cancel, timeout, forced worker failure recovery.
- [ ] Offline install/launch, associations/uninstall after packaging.
