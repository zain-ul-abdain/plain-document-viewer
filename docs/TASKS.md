# Shared task tracker

Update owner and status before editing. Codex is implementing SDK recovery, the native shell, core loading, and initial text/CSV/Markdown preview and tests. These files are currently claimed by Codex.

| Task | Status | Owner | Files / completion gate |
|---|---|---|---|
| Initialize Git repository and preserve final specification | Done | Codex | docs/SPECIFICATION.md |
| Shared agent instructions | Done | Codex | AGENTS.md |
| Private GitHub remote and first docs commit | Done | Agent 2 | origin = github.com/zain-ul-abdain/plain-document-viewer (private); commit contains root and docs files only |
| Initial architecture comparison | In progress | Agent 2 | docs/DECISIONS.md only; verify remaining engine, configuration and licensing details against official sources |
| Test corpus and security fixtures (proposed) | Proposed | Agent 2 | tests/corpus/, tests/harness/; independent fixture producers, manifest of expected results, request-recording listener. Starts only after Codex agrees, because Codex holds tests/ |
| Recover SDK setup after disk-space failure | In progress | Codex | .tools/; user freed disk space; retry extraction |
| Application solution and native shell | In progress | Codex | src/, scripts/, tests/; build and launch real native app |
| Safe loading and isolated worker | Not started | Unclaimed | source snapshots, process privileges, memory/time limits, archive/XML tests |
| Text, CSV and Markdown viewers | Not started | Unclaimed | streaming, virtualization, safe Markdown AST rendering, acceptance fixtures |
| PDF and Office viewers | Not started | Unclaimed | faithful rendering, cached spreadsheet values, independent fixtures |
| Accessibility, themes and keyboard | Not started | Unclaimed | UI/manual tests |
| Offline installer and release validation | Not started | Unclaimed | packaging, format registry, corpus, timings, notices |
