# Verification — 2026-09-27

Windows x64, local .NET SDK 10.0.401, Release builds. Hardware/performance baseline collection pending.

## Executed

- User manually confirmed MD and TXT files work on 2026-09-27. This confirms those user checks, not the full accessibility or corpus checklist.
- `scripts/test-markdown.ps1 -Offline`: 14 new regressions passed: visible entities, safe decoded text, soft/hard breaks, email targets, combined emphasis, literal inline/display math, currency, code, unsafe links/images and front-matter offsets. Original 22 tests and six WPF smoke fixtures rerun successfully after this change.

- **Word and PowerPoint preview (Agent 2, 27 Sep 2026, commit 5f4cf8b):** `build.ps1 -Offline` 0 warnings/errors; `test.ps1 -Offline` 67 passed, including 27 manifest-driven Office preparation tests (every bad package refused with the expected message and nothing written; every good or hostile package copied with no outside reference except hyperlinks and no content-fetching field; originals unchanged, nothing beside them); `test-markdown.ps1` 14 passed; `smoke-test.ps1` 16 passed, covering all seven required formats, including `docx/complex-20-pages.docx` (20 pages) and `pptx/complex.pptx` (12 slides) through the real worker, LibreOffice and viewer, with 0 blocked requests; `security-smoke.ps1` 32 of 32 (10 hostile files opened safely, 22 refused with specific messages), 0 network requests, with the converter's dead-proxy layer switched off so the listener observed LibreOffice directly. Separately, before any sanitising, LibreOffice 26.2.6 alone converted the remote-image, network-share, remote-template and INCLUDEPICTURE fixtures with 0 requests and no lock files. Timings on this machine: first conversion about 15.7 s (profile creation), later ones about 2.8 s. Captured PNGs of the 20-page document and the slide view (thumbnails and slide) were inspected. Not checked: fidelity against Microsoft Office, WebDAV/SMB paths with the WebClient service running, Narrator.
- **Network and hostile-file check (Agent 2, 27 Sep 2026, commit 508308f):** `scripts/security-smoke.ps1` started `tests/harness/request-listener.mjs` on 127.0.0.1:47831, confirmed it records requests with a canary, then opened 13 fixtures in the real app. Three hostile files opened safely (PDF with auto-run JavaScript, PDF with launch/UNC/file links, workbook with WEBSERVICE and external-workbook formulas). Ten were refused with specific messages (empty and mislabelled PDF; XML-entity, ZIP-bomb, password, older-format, truncated, empty, mislabelled and macro-enabled workbooks). Requests recorded: 0. Limits: the WebClient (WebDAV) service was stopped, so UNC paths could not have reached the listener anyway; SMB on port 445 is not observed. Word and PowerPoint hostile fixtures exist but wait for the LibreOffice path.
- **XLSX viewer (Agent 2, 27 Sep 2026, commit 9ad7446):** `build.ps1 -Offline` 0 warnings/errors; `test.ps1 -Offline` 40 passed, including one test per workbook fixture driven by `tests/corpus/manifest.json` (17 exact cell values in `xlsx/complex.xlsx` with en-US formatting, merged range, frozen panes, right-to-left sheet, hidden sheets absent, and the original unchanged with no files beside it; plus password, older-format, truncated, zero-byte, mislabelled, macro-enabled, ZIP-bomb and XML-external-entity files each giving the expected message); `test-markdown.ps1` 14 passed; `smoke-test.ps1` 12 passed including `xlsx/simple.xlsx` and `xlsx/complex.xlsx` with zero blocked requests. Captured PNGs of the complex workbook (both sheets) and complex PDF were inspected: formats, merge, right-to-left layout and Urdu/Sindhi/Arabic/CJK shaping display correctly. Frozen-pane behaviour while scrolling was not visually checked.
- **PDF viewer (Agent 2, 27 Sep 2026, commit 4aafdeb, run on the rebased code including Codex's e6bf1a0):** `build.ps1 -Offline` 0 warnings/errors; `test.ps1 -Offline` 27 passed (5 new PDF snapshot tests: exact copy and unchanged original, header within 1,024 bytes, empty and mislabelled messages, shared write access, network path rejected); `test-markdown.ps1` 14 passed; `smoke-test.ps1` 10 passed, including `pdf/simple.pdf`, `pdf/complex.pdf`, `pdf/attack-javascript.pdf` and `pdf/attack-links.pdf`. The smoke test opens each PDF in a real off-screen window, checks the page count, searches for "Hello", zooms, and fails if the PDF view attempted any blocked request (all four: 0). That counts requests made inside WebView2 only; it is not OS-level network instrumentation.
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
