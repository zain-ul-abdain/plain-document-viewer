# Shared task tracker

Update owner and status before editing. Codex completed the initial preview milestone and released file claims. Agent 2's proposed corpus/harness ownership is accepted; other implementation tasks are unclaimed.

| Task | Status | Owner | Files / completion gate |
|---|---|---|---|
| Initialize Git repository and preserve final specification | Done | Codex | docs/SPECIFICATION.md |
| Shared agent instructions | Done | Codex | AGENTS.md |
| Private GitHub remote and first docs commit | Done | Agent 2 | origin = github.com/zain-ul-abdain/plain-document-viewer (private); commit contains root and docs files only |
| Initial architecture comparison | Reviewed with open gates | Agent 2 | Codex review notes in HANDOFF.md: literal HTML, offline WebView2 prerequisite, no weakened network boundary |
| Test corpus and security fixtures | Ownership accepted | Agent 2 | tests/corpus/, tests/harness/; preserve existing fixtures/SOURCES.md; add independent fixtures, expected results and request-recording listener |
| Recover SDK setup after disk-space failure | Done | Codex | SDK 10.0.401 works; old ZIP may remain |
| Application solution and native shell | Preview implemented | Unclaimed | Builds; six worker/WPF smoke fixtures pass; manual checks pending |
| Safe loading and isolated worker | Partial | Unclaimed | Shared reads and Job resource limits; restricted privileges/snapshots/instrumentation pending |
| Text, CSV and Markdown viewers | Preview implemented | Unclaimed | 22 core tests pass; 4 MB text cap and first 1,000 CSV rows; full acceptance pending |
| PDF viewer (PDF.js in locked-down WebView2) and XLSX/XLS grid (cached values) | In progress | Agent 2 | Branch a local branch in worktree a separate worktree. New files under src/*/Pdf*, src/*/Sheet*, src/PlainViewer.App/Assets/pdfjs; small edits to MainWindow, WorkerClient, Worker Program.cs, csproj package references, tests runner. Codex: please avoid these files until merged, or note overlap here |
| DOCX/PPTX via LibreOffice | Not started | Unclaimed | Needs PDF viewer first, disk space for LibreOffice, and the AppContainer gate in DECISIONS.md |
| Accessibility, themes and keyboard | Not started | Unclaimed | UI/manual tests |
| Offline installer and release validation | Not started | Unclaimed | packaging, format registry, corpus, timings, notices |
