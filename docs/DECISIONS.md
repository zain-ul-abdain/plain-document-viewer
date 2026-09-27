# Technology decisions

Status: implementation foundation; research checked 2026-09-27. No commercial trials or licences obtained. No engine fidelity, installer size, or first-open estimates below are measurements.

## Selected direction

Use .NET 10 and WPF for the Windows shell, with a core library independent of UI. WPF provides native keyboard, text selection, accessibility, and virtualized controls. Use its Fluent theme. Build a small development preview first; the production release requires the full acceptance gates in SPECIFICATION.md.

Use a hybrid renderer: Markdig for Markdown syntax mapped to safe native display elements; a dedicated streaming spreadsheet reader and grid; PDF.js in a tightly restricted local WebView2 surface for PDFs; LibreOffice conversion for DOCX/PPTX only after worker isolation and disabled external-content behavior are verified. The PDF/Office paths are recommendations, not currently enabled integrations. Do not distribute an insecure conversion shortcut.

## Options

| Approach | Fidelity and old formats | Grid, text and accessibility | Distribution, cost and maintenance | Size, speed and exposure |
|---|---|---|---|---|
| LibreOffice plus PDF viewer | Broad import coverage; Word/slide layout needs independent Office comparisons. Old DOC/XLS/PPT require corpus validation. | PDF text layer can enable search/copy; conversion loses spreadsheet grid and tabs. Accessibility depends on output tagging and viewer. | MPL-2.0 distribution and third-party notices/source obligations must be audited for the bundled binaries; this does not automatically require publishing our separate app. Active upstream project. | Largest expected bundle; cold conversion overhead. No measured size or time yet. Large native parser surface; conversion must be isolated and offline. |
| Syncfusion | Document SDKs cover multiple Office formats; verify each product's legacy and rendering capabilities in public docs before selection. | Separate spreadsheet/viewer components may be required. Search and accessibility need component-specific validation. | Community eligibility is conditional, not assumed. Commercial pricing/redistribution require an approved quote or qualifying licence. Maintained product suite. | Size and cold-start unknown without an approved evaluation. Proprietary parsing still needs isolation. |
| Aspose | Words and other format products provide document processing; per-product rendering fidelity and binary formats need tests. | Processing APIs alone do not provide our complete interactive grid/UI. | Paid licensing; exact total and redistribution terms unresolved. Maintained commercial products. No trial downloaded. | Size/performance unmeasured. Multiple SDKs and parser surfaces. |
| Apryse | PDF and Office conversion capabilities depend on selected modules. Legacy format scope requires vendor confirmation. | Viewer features may reduce custom PDF work; spreadsheet grid still requires validation. | Modular commercial pricing; exact cost requires quote and approval. Maintained SDK. | Bundle/performance unknown; native conversion and viewer need isolation. |
| Separate open-source libraries | Markdig covers Markdown syntax; Open XML parsing reads structure, not faithful Word/slide pagination. PDF.js renders PDFs. Old Office binaries need additional engines. | Best control over cached spreadsheet values and virtualized grid. Search and accessibility must be implemented per view. | Audit each pinned package and transitive licence. Markdig BSD-2-Clause; PDF.js Apache-2.0. No per-seat charge anticipated. | Smaller individual components, but higher implementation cost. Streaming and limits require deliberate design; not inherently safer. |

## Pitfalls and release gates

- Windows.Data.Pdf renders pages; its documented API does not supply the text-search/selection layer needed here. Do not choose it as the sole viewer engine.
- Office preview handlers are not a reliable Office-free dependency. Do not depend on installed Office or shell preview handlers.
- LibreOffice must receive a private copy and a private profile, with macro execution, updates and external content disabled. Block network access at the worker boundary, not only with preferences. Lock files must stay inside private storage. Exact configuration and hostile fixtures are pending.
- Spreadsheets must use a grid. Parse cached values only; no formula evaluator. Use disk indexing and virtualized rows for large workbooks; merged-cell layout and style fidelity require dedicated implementation.
- Markdown must create only trusted native UI objects from syntax nodes, never instantiate XAML/HTML from the file. Image references never cause file or network reads. Unsafe link schemes are inert text. HTML is displayed literally.
- PDF.js needs local assets and a text layer; WebView2 must block external requests, popups and navigation. PDF scripting/attachments/actions must be disabled. Fixed runtime offline redistribution and process isolation are not yet validated.
- Missing fonts alter Office layout. Candidate fallback families: Carlito, Caladea and Liberation; verify exact font files and OFL notices before bundling. No fonts bundled yet; never download document-referenced fonts.
- Prefer an offline per-user EXE installer, subject to verifying the chosen installer tool's current licence. No installer engine has been downloaded. Only register acceptance-tested formats and never overwrite defaults. ARM64 is untested.
- No production signing certificate will be bought. Unsigned preview builds are allowed; distribution signing is a later documented step.

## Official sources

- WPF/.NET: https://learn.microsoft.com/en-us/dotnet/desktop/wpf/ and https://devblogs.microsoft.com/dotnet/announcing-dotnet-10/
- LibreOffice licensing: https://www.libreoffice.org/licenses/
- Syncfusion eligibility: https://www.syncfusion.com/products/communitylicense
- Aspose licence: https://docs.aspose.com/words/net/licensing/ and pricing https://purchase.aspose.com/pricing/total
- Apryse pricing: https://apryse.com/en-au/pricing
- Markdig and licence: https://github.com/xoofx/markdig
- PDF.js viewer/API: https://mozilla.github.io/pdf.js/getting_started/ and https://mozilla.github.io/pdf.js/api/
- Windows.Data.Pdf: https://learn.microsoft.com/en-us/uwp/api/windows.data.pdf.pdfdocument

Unresolved items are research/implementation tasks, not verified promises. Independent Office files, cold/warm timing measurements, native containment, renderer configuration, dependency redistribution audit and installer tests are release blockers.
