# Plain Document Viewer

A planned offline, read-only Windows 11 document viewer. **Status: repository setup and architecture draft only; no runnable app yet.**

Required formats: PDF, DOCX, XLSX, PPTX, CSV, TXT, Markdown (.md and .markdown). None is acceptance-tested or advertised as supported yet.

## Start here

- `docs/SPECIFICATION.md`: complete user requirements.
- `docs/DECISIONS.md`: proposed .NET 10/WPF hybrid rendering architecture.
- `AGENTS.md`: shared Both agents working instructions.
- `docs/TASKS.md`: ownership and implementation checklist.
- `docs/HANDOFF.md`: actual environment state and next steps.

## Build status

Build/run/test/package commands are not available yet. Local SDK setup failed because the disk ran out of space; see HANDOFF.md. The intended build requires .NET 10 on Windows. Do not treat this repository as a finished viewer or production release.
