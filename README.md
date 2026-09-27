# Plain Document Viewer

A native Windows 11 read-only document viewer. **Status: runnable development preview, not the complete v1.**

The preview has text, CSV, and rendered Markdown (.md and .markdown) views. Required PDF, DOCX, XLSX and PPTX rendering remains pending. No format has completed the full release acceptance corpus.

## Start here

- `docs/SPECIFICATION.md`: complete user requirements.
- `docs/DECISIONS.md`: proposed .NET 10/WPF hybrid rendering architecture.
- `AGENTS.md`: shared Both agents working instructions.
- `docs/TASKS.md`: ownership and implementation checklist.
- `docs/HANDOFF.md`: actual environment state and next steps.

## Build and run

Use Windows with the .NET 10 SDK, installed normally or under `.tools/dotnet`. Scripts prefer the local SDK and keep build-tool data under `.tools`. From the repository root in PowerShell:

```powershell
.\scripts\build.ps1
.\scripts\test.ps1
.\scripts\test-markdown.ps1
.\scripts\smoke-test.ps1
.\scripts\run.ps1
.\scripts\run.ps1 -File "$PWD\tests\corpus\complex.markdown"
```

On this machine Markdig 1.4.0 is cached in `.tools/feed`, so `build.ps1 -Offline` and `test.ps1 -Offline` work. Fresh machines need NuGet access for normal restore. Implemented document views make no automatic network requests; confirmed links delegate to the default browser/email application.

Build output: `src/PlainViewer.App/bin/Release/net10.0-windows`. Use `run.ps1` so the worker knows the local .NET host. This framework-dependent preview does not bundle a runtime or installer.

## Implemented preview

- One document per window; Open, drag/drop and command-line loading.
- Read-only text, CSV grid, native rendered Markdown and source toggle.
- Search, native copy, zoom, system/light/dark theme, keyboard shortcuts.
- Encoding and delimiter overrides.
- Separate worker with opening timeout, memory limits, cancellation and bounded output.
- Inert Markdown image placeholders and literal HTML; confirmed HTTP/HTTPS/mailto links only.

## Limitations

- Text/Markdown: 4 MB. CSV: 256 MB input ceiling, first 1,000 rows only, 512 columns, 1 MB per row. Search covers loaded rows and says so.
- Worker runs with normal user privileges. Restricted containment and observed network tests remain required before release.
- Reparse points/cloud placeholders/network paths are rejected; this may reject locally available OneDrive files.
- Full Markdown math styling, horizontal code scrolling, accessibility and large-file virtualization remain unfinished.
- No PDF/Office renderer, file associations, installer or production signing. Docker is not used; native Windows builds and tests are intended.

See `docs/SUPPORT.md` and `docs/TEST-RESULTS.md`. Packaging instructions will follow an actual offline installer; no placeholder packaging script is provided.
