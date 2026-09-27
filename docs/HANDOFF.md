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
