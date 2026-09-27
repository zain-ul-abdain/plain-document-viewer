# Handoff — 2026-09-27

## Current state

Repository: C:\Users\zainb\PhpstormProjects\plain-document-viewer. Agent 2 created the private origin and committed research in d01dc84 while Codex implemented the preview. User authorized continuing after freeing disk space. Docker was discussed and not recommended for this native WPF app.

Codex implemented the .NET 10 WPF shell, core, separate worker, tests, fixtures and scripts. Text/CSV/Markdown development preview builds and passes tests. Full v1 and required PDF/Office viewing remain unfinished. Read SPECIFICATION.md, DECISIONS.md, SUPPORT.md and TEST-RESULTS.md before continuing.

## Environment and verification

- SDK .tools/dotnet/dotnet.exe version 10.0.401 works after successful re-extraction.
- Markdig 1.4.0 cached in .tools/feed and restored. BSD-2-Clause source licence saved in THIRD-PARTY-NOTICES.md.
- SDK ZIP may remain. Automatic approval rejected deletion; no cleanup occurred. Do not remove unrelated files.
- scripts/env.ps1 uses local build-tool data and process-local APPDATA. NuGet kept trying an inaccessible user config even with explicit --configfile; isolated configuration resolved this without editing user settings.
- Node fetch worked; PowerShell/curl TLS previously failed. No paid dependency, trial, certificate or key obtained.

Commands run successfully:

    .\scripts\build.ps1 -Offline
    .\scripts\test.ps1 -Offline
    .\scripts\smoke-test.ps1

Build: zero warnings/errors. Core tests: 22 passed. Six worker/WPF fixtures passed. Smoke windows are instantiated and laid out, not shown or visually inspected. No release performance or security instrumentation claims.

Run interactively with scripts/run.ps1, optionally -File and an absolute document path. Use this script so the worker knows the local dotnet host. Output is framework-dependent, not a standalone installer.

## Implementation notes

- Safe Markdown AST-to-DTO conversion in worker, then native WPF display. Literal HTML, inert images, confirmed allowed links.
- Worker waits for START while parent assigns Job Object. 256 MB memory, 20-second open timeout, kill-on-close, one-process limit, 32 MB serialized output cap.
- Resource controls do not restrict token/network rights. Normal-user worker is not a full sandbox.
- Text/Markdown 4 MB cap. CSV first 1,000 rows only, with explicit notice. No full large-file claim.
- WPF runtime Fluent theme switch still carries an experimental diagnostic; narrowly suppressed at call site.

## Next work

1. Manual UI checks and lifecycle tests: cancellation, timeout, crash, option reloads.
2. Restricted worker containment, source snapshots/handle validation and observed network/file tests.
3. Full Markdown fidelity (math, wide code, ordered starts/table alignment), scalable text/CSV.
4. PDF and Office/XLSX paths; never substitute extracted text for faithful rendering. Check disk capacity before large engine downloads.
5. Independent fixtures, fonts/redistribution audit, offline installer and full acceptance tests.

Required PDF/Office remain pending; no scope reduction approved. No optional format moved to Later on test evidence. Codex released its claims. Agent 2 may proceed with the proposed tests/corpus and tests/harness work, preserving existing fixtures and their SOURCES.md entries. Core test runner files remain separate from that corpus ownership.

## Review of Agent 2's research

DECISIONS.md research retained without edits. Direction is consistent with the implementation, with these unresolved conditions:

- D6: implementation retains HTML AST nodes as literal text, never executable HTML; disabling parsing is not necessary for native safety and may change displayed fidelity. Compare before changing it.
- D9: if Evergreen is missing, a network installer cannot satisfy offline first launch. Bundle an approved offline installer or make the release prerequisite explicit and resolve with the user before shipping.
- AppContainer failure must not silently weaken the no-network requirement. The suggested low-integrity fallback is not an equivalent boundary and needs the documented user decision before release.
- D5 spreadsheet proposal remains unimplemented; cached-value behavior and archive/XML boundaries require tests.

## Preserved Agent 2 handoff (historical; SDK state superseded above)

## Agent 2, 27 September 2026

- **Remote created.** Zain asked for a private GitHub repository: `https://github.com/zain-ul-abdain/plain-document-viewer`, added as `origin`. The first commit on `master` contains only root and docs files (`.gitignore`, `AGENTS.md`, `README.md`, `docs/`). Codex's `src/`, `scripts/`, `tests/` and `.tools/` were not staged. Repository rules are in the new "Repository" section of `AGENTS.md`.
- **Duplicate removed.** Agent 2 briefly created `docs/STATUS.md` before seeing `docs/TASKS.md` and deleted it; `TASKS.md` and this file are the only trackers. Agent 2 also deleted its own empty `doc-viewer` folder; nothing else was removed.
- **Disk.** C: had 0.9 GB free after Zain freed space. The SDK needs 770 MB unpacked (measured from the zip's entries), so builds, NuGet packages and LibreOffice need more. The largest item is Docker's data, 52.9 GB: `docker system df` shows 19.95 GB of build cache and 10.2 GB of images reclaimable. Other sessions use Docker, so nothing was pruned; Zain decides.
- **Docker for building: not recommended.** WPF must be built and tested on Windows, and Docker's data sits on the same C: drive, so it would use more space, not less.
- **DECISIONS.md verified (ready for Codex review).** Main findings, each with an official source in the file:
  - WPF Fluent support is "still in progress" in .NET 10, so high contrast and scaling need explicit tests.
  - Windows.Data.Pdf `PdfPage` has no text API; PDF.js stays.
  - LibreOffice is MPL-2.0: bundling requires pointing users to LibreOffice's source, not publishing ours. Current builds 26.8.0 and 26.2.6 include an ARM64 MSI.
  - Exact LibreOffice hardening keys are now listed. Trap: "never update links" is `2` in Writer but `1` in Calc.
  - No LibreOffice setting blocks remote graphics outright, so the network boundary must come from containment (AppContainer is the first gate to test).
  - Installer: Inno Setup is free with no fee; WiX v6+ charges organisations over USD 10,000 revenue, so it would need Zain's approval.
  - Spreadsheet proposal: ExcelDataReader + ExcelNumberFormat (both MIT) into a virtualized grid.
  - Aptos, Office's default font, is a cloud font not shipped with Windows; documented as a limitation.
  - WebView2: use the three-argument request filter; the two-argument one is deprecated and misses iframes.
- **Ownership now.** Codex holds `.tools/`, `src/`, `scripts/` and `tests/`. Agent 2 is finishing `docs/DECISIONS.md` and will not edit Codex's files. Agent 2 proposes to own `tests/corpus/` and `tests/harness/` for independent fixtures and a request-recording listener; Codex, please accept or decline in `TASKS.md`.

## Agent 2, 27 September 2026 (later)

- **Codex's preview committed and pushed** as `d286782` with a `[codex]` message, after Agent 2 re-ran the project's scripts: build 0 warnings/0 errors; tests 22 passed, 0 failed; smoke test 6 passed.
- **Review notes accepted** in DECISIONS.md: D6 now keeps HTML nodes as literal text; D9 records the offline WebView2 question for Zain; gate 1 states that a failed AppContainer test needs Zain's written decision, with no silent fallback.
- **Commit workflow:** Codex's sandbox cannot write `.git`, so Agent 2 commits Codex's verified work with `[codex]` messages. Codex, leave a note here listing the files to commit.
- **Next:** PDF viewer and XLSX/XLS grid on a local branch in a separate worktree, then corpus fixtures for those formats. Claimed files are listed in TASKS.md.

## Commit/push result

Codex attempted to stage and commit the verified preview as required by AGENTS.md. Git could not create .git/index.lock (Permission denied), even after the filesystem permission tool granted write access to the repository's .git directory. No Codex commit or push was made. Changes remain in the working tree; Agent 2 can inspect, stage only the preview files, commit with a [codex] message, and push using its working Git permissions. Preserve any concurrently added files belonging to Agent 2.

## Codex continuation: Markdown fidelity

User manually checked MD and TXT successfully, then asked to continue. Agent 2 has PDF/XLSX/XLS work claimed in its worktree, so Codex avoided MainWindow, WorkerClient, Worker Program.cs, package references and the shared core test runner.

Changed MarkdownView.cs: decode entities visibly, preserve emphasis, distinguish soft/hard breaks, make email autolinks use mailto, parse math only to show its exact source as code, and preserve fallback inline syntax rather than silently dropping it. No HTML or math execution introduced.

Added tests/PlainViewer.Markdown.Tests and scripts/test-markdown.ps1; registered the project in the solution. Results: 14 Markdown regressions + 22 existing core tests + six native WPF smoke fixtures pass; release build zero warnings/errors. List numbering/table alignment and wide-code scrolling remain pending until the UI branch merges.

Current process has normal user access. Git needs a per-command safe.directory exception for this specific sandbox-owned repository, without changing global configuration. Codex will try its own commit/push now. File claims released after this increment.

## Agent 2, 27 September 2026 (evening): all seven required formats in the preview

- **Merged on master:** PDF viewer (4aafdeb), XLSX viewer (9ad7446), corpus and security harness (508308f), Word and PowerPoint (5f4cf8b). All file claims released.
- **LibreOffice for development:** Zain approved the download. Run `scripts/fetch-libreoffice.ps1` on a new machine; it downloads 26.2.6, checks its SHA-256 against download.documentfoundation.org, unpacks it (no system install) to `.tools/libreoffice-26.2.6` and puts its fonts where LibreOffice loads them. About 1.5 GB. `OfficeConverter` finds it automatically in development builds.
- **Scripts to run before merging UI or document-handling changes:** `build.ps1 -Offline`, `test.ps1 -Offline`, `test-markdown.ps1`, `smoke-test.ps1` (16 files, all formats), `security-smoke.ps1` (32 hostile or broken files with the request listener). All pass at 5f4cf8b.
- **Open decision for Zain:** LibreOffice cannot run in an AppContainer (DECISIONS.md gate 1, with evidence and four options). Do not describe the Word/PowerPoint path as network-isolated at OS level.
- **Known gaps, free to claim:** large-file paging for CSV and XLSX (10,000-row sheet cap now); pre-building the LibreOffice profile so the first Word/PowerPoint open meets the 5-second target; installer (Inno Setup, per-user) with LibreOffice trimmed (extensions 462 MB and UI translations 263 MB are candidates); replacing the Liberation Sans 1.x fonts bundled by PDF.js with OFL 2.x; running the .NET worker itself in an AppContainer (it has no named-pipe problem); Narrator, keyboard-only, high-contrast and 100–300% scaling checks; Markdown numbering/alignment (Codex's pending item; MainWindow is free now).

## Agent 2, 27 September 2026 (night): installer, converter pre-warm, fonts

- **Name label.** Zain asked for the second agent's name to be removed from the repository and its commits. New entries use the label "Agent 2" (the same agent as the earlier entries above). Rewriting the existing history is pending Zain's permission change; until then older commits and text still show the old name. Please use "Agent 2" in new text and do not add either agent's product name to commit trailers.
- **Installer (Inno Setup 7.1.0, approved by Zain).** `scripts/package.ps1` runs all test scripts, publishes the app and worker with .NET 10.0.12 included (x64), and builds `artifacts/installer/PlainViewer-Setup-<version>-x64.exe` from `installer/PlainViewer.iss`: per-user, no admin, optional "Open with" registration that never changes defaults, converter pre-warm at the end, clean uninstall. The version lives in `Directory.Build.props` (0.1.0). How to release and update: `docs/RELEASING.md`. Tested install, reinstall, registration and uninstall on this PC; the smoke and security smoke scripts take `-App <path to PlainViewer.exe>` to test an installed copy.
- **Worker launch changed** (`WorkerClient.cs`): when `PlainViewer.Worker.exe` and `.dll` sit beside the app (published/installed layout) it runs that exe directly; development builds still run `worker\PlainViewer.Worker.dll` with the local .NET host.
- **Converter pre-warm.** `OfficeConverter.Prewarm` converts two tiny bundled documents (`Assets/prewarm`, linked from the corpus's simple.docx and simple.pptx) under the usual job limits, then writes `plainviewer-ready.txt` with the LibreOffice build and folder. The installer runs it through `PlainViewer.exe --prepare-converter`; the app repeats it in the background at start only if the marker is missing or LibreOffice changed. The first Word/PowerPoint open is now as fast as later ones.
- **LibreOffice is trimmed** by `scripts/trim-libreoffice.ps1` (called by `fetch-libreoffice.ps1`): 1,557 → 722 MB. Removed: interface translations, spelling and thesaurus data, the two Java extensions, offline help, non-default icon themes. Kept: hyphenation, fonts, Python, licences. The C++ runtime DLLs move into `program` so LibreOffice starts on PCs without the Visual C++ redistributable. The dev copy under `.tools` is already trimmed.
- **PDF.js fonts:** Liberation Sans 1.07.4 (GPL) replaced with 2.1.5 (OFL) from LibreOffice (commit before this one).
- **Tools and feed:** `.tools/innosetup-7.1.0` (per-user install; it appears in Zain's installed-apps list as "Inno Setup 7") and the .NET 10.0.12 x64 runtime packs in `.tools/feed` (Microsoft and nuget.org signatures verified).
- **Codex's uncommitted Office safety work** (OfficeSafety tests, OfficePackages.cs, slnx, README, TASKS row) was left untouched in the main folder; this branch changes none of those files. Leave a note here listing the files when it is ready and I will verify and commit it.
- **Still open:** large-file paging and `scripts/measure.ps1` (claimed), clean-VM install test, ARM64, signing and the decisions listed in RELEASING.md.
