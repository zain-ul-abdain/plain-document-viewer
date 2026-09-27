# Build a read-only document viewer for Windows 11

## Goal
Build a simple Windows 11 desktop app for reading documents: open a file, read it comfortably, and close it.

The app must work offline without Microsoft Office installed and must never modify the original file. Prioritize reliable viewing, a responsive interface, and straightforward installation over extra features.

Keep the app and installer as small as practical, but explain tradeoffs between size, rendering quality, and performance.

## Audience
People with little technical experience. Messages must use plain language, explain what happened, and provide a useful next step.

## Supported formats

### Required for v1
`.pdf`, `.docx`, `.xlsx`, `.pptx`, `.csv`, `.txt`, `.md` (also accept `.markdown`)

### Optional for v1
Support these only if the chosen engines render them reliably:
`.doc`, `.xls`, `.ppt`, `.rtf`, `.odt`, `.ods`, `.odp`

### Later
Macro-enabled files, templates, and slideshow variants:
`.docm`, `.xlsm`, `.pptm`, `.dotx`, `.xltx`, `.potx`, `.ppsx`

A format counts as supported only after passing its acceptance tests.
* Required formats must pass before v1 is complete. If a required format cannot meet the requirements, explain the blocker and ask before reducing scope.
* Optional formats that fail may move to Later. Record the reason.
* Only advertise and register supported formats.
* Filter the Open dialog to supported formats. Validate files opened through every entry point and explain unsupported or mismatched file types clearly.

## Viewing behavior

### Documents and PDFs
* Display pages with continuous scrolling.
* Preserve layout, tables, images, headers, and footers as closely as practical.
* Provide page navigation and either thumbnails or a page number field with go-to-page.
* Document differences from the original application.

### Spreadsheets
* Provide sheet tabs and a scrollable grid with row and column headers.
* Virtualize the grid and load sheets on demand so large workbooks stay responsive.
* Preserve column widths, merged cells, and number and date formats as closely as practical.
* Support frozen panes where practical.
* Keep hidden sheets hidden.
* Display saved values and cached formula results. Never recalculate formulas or update external data.
* If a formula has no cached result, show "Result unavailable" and explain that the file needs to be recalculated and saved in a spreadsheet application. Never silently substitute zero or an empty value.
* Document support and limitations for charts, images, conditional formatting, and other advanced features.

### Presentations
* Show slide thumbnails and one large slide.
* Provide previous, next, and go-to-slide controls.
* Preserve text, images, shapes, and charts as closely as practical.
* Display static slides only. Explain that animations, transitions, audio, and video do not play.

### Text and CSV
* Support UTF-8 with and without a BOM, UTF-16, and Windows-1252.
* Detect encoding using BOMs and reasonable heuristics.
* Detect comma, semicolon, and tab delimiters.
* Provide a simple encoding and delimiter override.
* Handle quoted fields, escaped quotes, and multiline CSV fields.
* Preserve CSV field text by default, including leading zeros.
* Use streaming or virtualization for large files rather than loading the entire file into memory.
* Show CSV in a grid and plain text in a readable text view.

### Markdown
* Open `.md` and `.markdown` files in a readable, rendered view by default.
* Target CommonMark with GitHub-style tables and task lists. Document the supported dialect and any unsupported syntax.
* Support headings, paragraphs, lists, task lists, tables, blockquotes, links, fenced code blocks, and horizontal rules.
* Offer a simple toggle between Rendered and Source views. Both remain read-only.
* Use the same encoding detection as plain text.
* Support text search, selection, copying, and zoom.
* Wrap long prose and allow horizontal scrolling within wide code blocks.
* Match the app's light, dark, and high-contrast themes.
* Show embedded HTML as literal text. Never render it or silently drop it. Never run scripts.
* Show YAML front matter, Mermaid diagrams, and math as code blocks in v1.
* For v1, show image descriptions or placeholders instead of loading referenced images. Never load remote images and never access linked local files.
* Show `javascript:`, `data:`, `file:`, and network-share links as plain text. Other links follow the link rules under Safety and privacy.

### Shared controls
* Provide zoom, text search with next and previous results, and text selection and copying where supported.
* Show a match count when available. For large files, allow search to run progressively with cancellation and clear progress.
* Provide fit to page and fit to width for paginated documents and slides.
* Use appropriate zoom and navigation controls for text, Markdown, and spreadsheet grids.
* Disable unavailable controls and provide an accessible explanation. Never simulate functionality that does not work.

## Opening files and handling errors
Support:
* An Open File button.
* Drag and drop.
* Windows Explorer "Open with."
* Double-click opening when the user has selected the app as the default.
* A file path passed on the command line.

Identify files by their content, not only their extension.

Use one document per window. Opening another file creates another window.

Handle these cases without an unhandled crash or unexplained blank view:
* Unsupported or mismatched file type.
* Damaged or truncated file.
* Password-protected file.
* Valid empty document or zero-byte file.
* File exceeding a documented resource limit.
* File that cannot be read because of permissions or an exclusive lock.
* File moved, deleted, or changed during loading.
* OneDrive placeholder that is unavailable offline.
* Rendering timeout or worker failure.

If a document has already been loaded into a private snapshot, allow viewing to continue if the original is moved or deleted.

Password-protected PDFs may support password entry. Other protected formats only need a clear explanation in v1. Never store or log passwords.

## Interface
Use a native Windows 11 appearance, such as WinUI 3 or an equivalent framework with Fluent styling.
* One small toolbar and a large viewing area.
* No ribbon, editing tools, ads, sign-in, or unnecessary panels.
* File name in the title bar.
* A status area showing information relevant to the current view: page, slide, sheet, and zoom.
* Light and dark themes following Windows settings, with a manual override.
* The app theme applies to the app itself and to formats without their own styling (text, CSV, Markdown). Documents with their own colors (PDF, Word, Excel, PowerPoint) keep them.
* Support 100–300% display scaling, monitors with different scaling, and high-contrast themes.
* Provide full keyboard operation and accessible labels for every control.
* Test with Narrator. Document accessibility limitations within rendered document content.

Keyboard shortcuts:
* `Ctrl+O`: open.
* `Ctrl+F`: find.
* `F3` and `Shift+F3`: next and previous match.
* `Ctrl+C`: copy selected text or cells.
* `Ctrl+Plus` and `Ctrl+Minus`: zoom.
* `Ctrl+0`: reset zoom.
* `Page Up`, `Page Down`, `Home`, and `End`: navigation appropriate to the current view.
* `Ctrl+Page Up` and `Ctrl+Page Down`: switch spreadsheet sheets.
* `Ctrl+W`: close the window.
* `F11`: toggle full screen.

## Safety and privacy
These are required behaviors.

### Protect original files
* Never modify, rename, or delete the source file, or create files beside it.
* Open sources for reading with sharing that permits other applications to read, write, and delete them where supported.
* Never request exclusive access.
* Give rendering engines a private temporary snapshot when necessary.
* If the source changes while being copied, retry safely or explain the problem.

### Disable active content
* Never execute document macros, scripts, ActiveX, embedded programs, or OLE activation.
* Never update external links, DDE, or external data connections.
* Never fetch remote images, fonts, or templates.
* Treat network locations referenced inside documents as remote. Never access UNC paths (\\server\share), file:// URLs pointing to other machines, or WebDAV locations, because Windows can send the user's credentials to them.
* Disable DTD processing and external entity resolution in every XML parser.
* Cap decompressed size, compression ratio, and entry count for ZIP-based formats. When a limit is hit, show the damaged-file message.
* Verify engine configuration and behavior instead of assuming defaults are safe.
* Do not open hyperlinks or launch external applications automatically.
* Clicking a link shows its full address and asks before opening it in the default browser. Only http, https, and mailto links may be opened. Show every other link type as text.

### Work offline
* No document uploads, telemetry, crash uploads, or update checks.
* Opening and rendering documents must require no network access.
* Bundle required runtime components for offline installation and first launch.
* If using WebView2, permit only app-owned local resources. Block external navigation and remote requests.

### Isolate document processing
* Parse and render untrusted documents in a separate process with restricted privileges.
* Apply enforceable memory limits and operation timeouts.
* Support cancellation and recovery if a worker fails.
* Describe the actual isolation mechanism and its limitations. Do not claim that isolation eliminates all security risks.

### Temporary data
* Store temporary files in an app-owned location with appropriate access restrictions.
* Delete them when documents close.
* Clean up leftovers after a crash on the next launch.
* Avoid logging document contents.

## Performance
Measure release builds on an available Windows machine. Record its CPU, RAM, storage, and Windows version.

Use a laptop with roughly four CPU cores and 8 GB RAM as the target baseline. If unavailable, report the actual hardware and leave baseline validation pending.

Targets:
* Main window ready within two seconds.
* First page of a representative 20-page DOCX visible within five seconds on a cold open, including conversion.
* First page of a representative local PDF visible within one second.
* Long operations leave the interface responsive.
* Operations lasting more than approximately half a second show an activity indicator or meaningful progress and support cancellation.
* A 200 MB CSV and a 500-page PDF open without exhausting memory.
* A large XLSX (for example 50 MB with several hundred thousand rows) opens its first sheet without exhausting memory, and the grid scrolls smoothly.

Report cold and warm timings separately, along with peak memory usage. If a target is missed, report it honestly and explain the bottleneck.

## Technology decision (do this first)
Before writing application code, create `docs/DECISIONS.md`.

Compare at least:
1. Bundled LibreOffice for headless conversion to PDF, combined with a PDF renderer.
2. Commercial SDKs, including Syncfusion, Aspose, and Apryse.
3. Separate open-source libraries for each format.

A hybrid approach is acceptable and may be necessary.

Evaluate commercial SDKs from public documentation and pricing only. Do not download trials, request keys, or accept licence terms without asking.

For each option, assess:
* Rendering fidelity by format.
* Support for older binary Office formats.
* Spreadsheet grid support.
* Search, selection, copying, and accessibility.
* Offline operation and runtime dependencies.
* Installer and installed sizes.
* Expected first-open performance.
* Licence and redistribution obligations.
* Cost.
* Maintenance status.
* Security exposure and isolation options.

Verify current technical and licensing claims using official documentation. Link sources and record the date checked. Distinguish estimates from measurements.

Explicitly investigate:
* Whether the proposed PDF renderer supports text search and selection; do not assume page rendering includes them.
* Whether conversion engines create lock files or access linked external content, and how to prevent this.
* Whether any preview-handler approach requires Microsoft Office.
* How spreadsheets retain grid scrolling and sheet tabs.
* How to avoid recalculating spreadsheet formulas.
* How missing fonts affect layout.
* Which fallback fonts to bundle, including their licences and known limitations.
* How runtime dependencies can be installed offline.

Prefer dependencies that allow free redistribution for the intended app. Ask before using a paid dependency, accepting new contractual terms, or choosing a licence that requires disclosure of the application's source code. Explain the specific obligation prompting the question.

Otherwise, proceed with the recommended approach. Continue with an unsigned or locally self-signed build and document production signing separately.

## Installer
* Produce one installer for Windows 11 x64.
* Choose MSI, MSIX, or an EXE installer such as Inno Setup, and explain the choice, including signing and offline installation requirements.
* State whether ARM64 is supported, works through emulation, or is untested.
* Prefer installation per user without administrator rights.
* Register "Open with" only for supported formats.
* Do not change existing default applications.
* Bundle everything needed for offline installation and first launch.
* Uninstall app-owned associations and temporary data cleanly.
* Report installer and installed sizes.
* Include third-party licence notices in the install folder and an About page.
* Document signing steps and possible Windows installation warnings or restrictions.
* Do not buy a signing certificate. You may create a self-signed certificate for local testing only, never for distribution.

## Testing
Create `tests/corpus` with at least three fixtures per supported format:
1. A simple file.
2. A complex file exercising features appropriate to that format.
3. A large file.

Also cover:
* Password protection.
* Damaged and truncated files.
* Empty documents and zero-byte files.
* Incorrect extensions.
* Unsupported macro-enabled files.
* External links or remote resources.
* Spreadsheet formulas with and without cached results.
* Hidden sheets.
* CSV quoting, delimiters, encodings, multiline fields, and leading zeros.
* Right-to-left and complex scripts: Urdu, Sindhi, and Arabic text in DOCX, PDF, XLSX, and Markdown fixtures, plus CJK text.
* Attack fixtures: a ZIP bomb, an XML external-entity file, a DOCX with a remote template, and documents whose images and links use UNC paths.
* Markdown: basic formatting, tables, task lists, fenced code, Unicode, long documents, embedded HTML, remote images, an image with a network-share path, links to local files, and `javascript:` links.

Use only generated files or files licensed for redistribution. Record the source, producing application, licence, purpose, and expected result for each fixture in `tests/corpus/SOURCES.md`. Files created by the same engine that renders them do not prove fidelity; prefer files saved by Microsoft Office or other independent producers. Large fixtures may be generated by a reproducible script.

Automated checks must verify:
* Successful opening or the expected error within a defined timeout.
* Correct page, sheet, and slide counts where applicable.
* Expected representative text and cell values.
* Original files remain unchanged.
* No document processing creates files beside the source.
* Cancellation and worker failure recovery.
* No prohibited network requests or active-content execution, using suitable observation or instrumentation.
* Viewing Markdown causes no network requests, script execution, or access to referenced local files.

Do not treat a single passing security fixture as proof that all possible documents are safe.

If Microsoft Office is available, export reference PDFs and compare representative pages visually. Record differences. Otherwise, state that Office reference comparison was not performed.

Provide a manual checklist covering:
* Keyboard-only operation.
* Narrator.
* Light, dark, and high-contrast themes.
* Display scaling and movement between monitors.
* Drag and drop.
* Explorer integration.
* Offline installation and first launch.
* Uninstallation.

Clearly distinguish tests actually run from tests not run. Never invent measurements or verification results. If the environment cannot build or test Windows software, identify the remaining work and do not claim the app or installer is verified.

## Implementation stages
Proceed in this order:
1. Research and document the technology decision.
2. Build a working viewing path for one representative file in each required format.
3. Validate difficult requirements early, especially spreadsheet rendering, cached formulas, offline conversion, and process isolation.
4. Complete the interface, error handling, and accessibility.
5. Run corpus tests, measure performance, and package the installer.

Maintain a checklist against this specification.

Do not substitute mock viewers, placeholder screens, or text extraction for faithful document rendering without approval. Do not stop after planning if implementation can proceed.

## Deliverables
* Application source code.
* README with prerequisites and build, run, test, and packaging instructions.
* `docs/DECISIONS.md`.
* `docs/SUPPORT.md` listing each format's status, supported features, limitations, and test evidence.
* Installer and reproducible packaging script.
* Test fixtures or fixture-generation scripts.
* Automated test results and manual test checklist.
* Measured opening times, memory usage, installer size, and installed size.

Finish with a concise report stating:
* What works and was verified.
* What does not work or remains unverified.
* Optional formats moved to Later and why.
* Performance results and missed targets.
* Installer location and size.
* Any decisions or blockers requiring my input.