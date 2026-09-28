# Shared agent instructions

Read `docs/SPECIFICATION.md`, `docs/DECISIONS.md`, `docs/TASKS.md`, and `docs/HANDOFF.md` before changes. The specification is the user's canonical request. Do not reduce required format support or replace rendering with extracted text.

## Working together

- Every agent follows this same file. An agent-specific entry-point file, if a tool needs one, stays out of Git (list it in `.git/info/exclude`) and only points here; it is not a second rulebook.
- Before work, inspect `git status` and claim a bounded task in `docs/TASKS.md` with agent, files, and status. A claim is advisory, not an atomic lock: coordinate explicitly before editing overlapping files.
- Do not undo another agent's changes. Stop overlapping edits and agree file ownership when another agent is active. Use separate branches/worktrees for simultaneous implementation where practical.
- Keep commits focused. Do not commit credentials, SDKs, generated binaries, private documents, or downloaded installers.
- Update task status and handoff with exact commands, actual results, remaining risks, and next steps before ending work. Never fabricate verification.
- Do not spawn agents unless the user asks. These files support handoffs and user-directed collaboration; they do not launch another assistant.

## Repository

- Remote: private GitHub repository `https://github.com/zain-ul-abdain/plain-document-viewer` (`origin`), default branch `master`.
- Use neutral commit titles: no agent or product names, no `[agent]` prefixes and no attribution trailers. Commit only files from your own claimed task; do not commit another agent's in-progress files.
- Push finished, verified work to `origin`. If a push fails (for example, no GitHub credentials in the Codex sandbox), keep the commit local and say so in `docs/HANDOFF.md`; Agent 2 can push it later.
- The working folder is owned by the Codex sandbox account. Agent 2 runs git with `-c safe.directory=*` on each command instead of changing global git configuration.

## Engineering rules

- Use .NET 10, WPF, and the architecture in DECISIONS.md. Build with `scripts/build.ps1`; test with `scripts/test.ps1`.
- Treat every document as untrusted. Never execute its active content or follow document references. Keep XML resolvers disabled and enforce archive resource limits.
- Never modify originals. Preserve read/write/delete sharing and reject unsupported types explicitly.
- Do not enable unfinished engines in the release format registry. No mock rendering or silent source-only fallback for Office files or Markdown.
- Keep core parsing separate from UI; production document parsing requires an isolated worker before release.
- Required formats remain pending until acceptance tests pass. An engineering preview is not v1.
- Commercial packages require the user's approval before downloads, keys, contracts, or payment. Use public documentation for comparisons.
- Add meaningful tests for parsing, safety boundaries, and regressions. Mark UI, security, fidelity, and performance checks not run as pending.
