# Release notes

## 0.1.0 — private beta (September 2026)

The first version for testers. Installer: `PlainViewer-Setup-0.1.0-x64.exe`, Windows 11 x64, not code-signed. Guide for testers: [BETA.md](BETA.md).

### What it does

- Opens PDF, Word (.docx), Excel (.xlsx), PowerPoint (.pptx), CSV, text and Markdown files for reading. Never changes the original file or writes beside it.
- Works fully offline: no uploads, usage data, crash reports or update checks. Everything it needs is in the installer, including the .NET runtime, LibreOffice 26.2.6 (for Word and PowerPoint), PDF.js and Microsoft's WebView2 Runtime installer.
- PDF and Word: continuous pages, page number and go-to-page, fit to page and fit to width. PowerPoint: one large slide with thumbnails, previous and next.
- Excel: sheet tabs, saved values and cached formula results (never recalculated; "Result unavailable" when a formula has no saved result), number and date formats, column widths, merged cells, frozen panes. Hidden sheets stay hidden.
- Text and CSV: encoding and delimiter detection with manual override; quoted and multi-line CSV fields; leading zeros kept.
- Markdown: rendered or source view, tables, task lists, code blocks. Embedded HTML shown as text; pictures and local files never loaded.
- Search with next and previous (match count where available), zoom, copy, light and dark themes with a manual choice, keyboard shortcuts, drag and drop, one window per document.
- Large files: CSV and text files up to 1 GB and Excel sheets with hundreds of thousands of rows are read from disk as you scroll; long searches show progress and can be cancelled.
- Safety: documents are read by a separate low-integrity process with memory and time limits; macros, scripts, external links and remote pictures, fonts and templates are never used; links ask before opening in the browser. Optional Windows Firewall rules block the document-reading processes from the network.
- Installs per user without administrator rights; optional "Open with" entries that never change your default apps; clean uninstall.

### Known limitations

- Word and PowerPoint layout is close to Microsoft Office but not identical (fonts the PC lacks are substituted; long documents can shift by a page). Animations, transitions, sound and video do not play.
- Excel cell formatting (colours, fonts, borders), charts, pictures and conditional formatting are not shown.
- No PDF page thumbnails; large text files have no word wrap; CSV is limited to 512 columns.
- Not supported: .doc, .xls, .ppt, .rtf, .odt, .ods, .odp and macro-enabled or template files.
- Not yet checked: Narrator, display scaling above 100%, ARM64 PCs.
- Unsigned: Windows SmartScreen warns before installing, and Smart App Control may block it.

Full details: [SUPPORT.md](SUPPORT.md). Test evidence: [TEST-RESULTS.md](TEST-RESULTS.md).
