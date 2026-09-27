# Handoff — 2026-09-27

## Verified state

- Repository: C:\Users\zainb\PhpstormProjects\plain-document-viewer. Git initialized locally; no remote, no commit created by Codex.
- The latest complete user request is preserved verbatim in docs/SPECIFICATION.md. It supersedes the earlier pasted amendment lists.
- AGENTS.md and the entry-point file provide shared instructions. Initial technology comparison is in docs/DECISIONS.md. Its pending research is explicitly marked.
- No application source, project, build script, installer or tests have been implemented. No application has been compiled or launched. Earlier commentary described intended work, not completed implementation.
- Git and Node v24.19.0 are available. No installed dotnet command or C:\Program Files\dotnet was found. Windows .NET Framework v4.0.30319 tools exist but have not been used to build the app.

## SDK failure

The official .NET 10 release metadata selected SDK 10.0.401. Node downloaded its Windows x64 ZIP and verified its SHA512 against official release metadata. PowerShell Expand-Archive then failed with insufficient disk space. Last observed C: free space was approximately 183 MB.

Task-created .tools/sdk.zip and .tools/dotnet may remain. Inspect before acting. A cleanup command targeting only these paths was rejected by automatic tool approval policy before execution. No deletion was performed by that command. Do not remove unrelated user files. The download script and sdk.json metadata are also under .tools/ and ignored by Git.

PowerShell Invoke-WebRequest and curl encountered Windows TLS credential errors; Node fetch worked. SDK installation must be repaired before selecting package versions and claiming a normal .NET 10 build. Do not silently switch production architecture to the old Framework to bypass this.

## Next steps

1. Read shared instructions, specification, decisions and task tracker; inspect git status.
2. Resolve available disk space and clean only confirmed task-created failed SDK artifacts when tool permissions permit. Otherwise explain the concrete blocker.
3. Finish technology verification, then scaffold .NET 10 WPF shell/core/worker/tests. Use scripts/build.ps1 and scripts/test.ps1 as the eventual entry points; these scripts do not exist yet.
4. Implement real viewing paths and safety boundaries incrementally. Keep all required formats pending until tested; do not claim extracted Office text is a document viewer.
5. Update this file with actual commands/results and task ownership when handing work back.

No commercial software, trial, certificate or key was obtained. No remote repository was created. No independent Office corpus, security instrumentation, UI accessibility check, benchmark or installer test has run.

## Agent 2, 27 September 2026

- **Remote created.** Zain asked for a private GitHub repository: `https://github.com/zain-ul-abdain/plain-document-viewer`, added as `origin`. The first commit on `master` contains only root and docs files (`.gitignore`, `AGENTS.md`, `README.md`, `docs/`). Codex's `src/`, `scripts/`, `tests/` and `.tools/` were not staged. Repository rules are in the new "Repository" section of `AGENTS.md`.
- **Duplicate removed.** Agent 2 briefly created `docs/STATUS.md` before seeing `docs/TASKS.md` and deleted it; `TASKS.md` and this file are the only trackers. Agent 2 also deleted its own empty `doc-viewer` folder; nothing else was removed.
- **Disk.** C: had 0.9 GB free after Zain freed space. The SDK needs 770 MB unpacked (measured from the zip's entries), so builds, NuGet packages and LibreOffice need more. The largest item is Docker's data, 52.9 GB: `docker system df` shows 19.95 GB of build cache and 10.2 GB of images reclaimable. Other sessions use Docker, so nothing was pruned; Zain decides.
- **Docker for building: not recommended.** WPF must be built and tested on Windows, and Docker's data sits on the same C: drive, so it would use more space, not less.
- **Ownership now.** Codex holds `.tools/`, `src/`, `scripts/` and `tests/`. Agent 2 is finishing `docs/DECISIONS.md` and will not edit Codex's files. Agent 2 proposes to own `tests/corpus/` and `tests/harness/` for independent fixtures and a request-recording listener; Codex, please accept or decline in `TASKS.md`.
