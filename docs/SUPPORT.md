# Format status

No format is release-certified. The Open dialog labels implemented formats as a development preview.

| Format | Current implementation | Release gaps |
|---|---|---|
| TXT | Read-only Unicode, encoding override, search/copy/zoom | 4 MB cap; streaming, complete encoding corpus and accessibility |
| CSV | Quoted/multiline records, delimiter override, string values, virtualized WPF grid, loaded-row search | First 1,000 rows only; full disk-backed virtualization and 200 MB test |
| MD / MARKDOWN | Markdig CommonMark plus tables/tasks mapped to native WPF; headings, emphasis, entities, lists, tables, quotes, literal math as code, source toggle, search/copy | Wide code wraps; ordered starts/table alignment not preserved; uncommon syntax, large corpus and accessibility pending |
| PDF | Pending | Real renderer, text layer, secure configuration, independent corpus |
| DOCX / PPTX | Pending | Conversion, offline isolation, fonts, Office reference corpus |
| XLSX | Pending | Cached-value reader, disk indexing, sheet grid/styles/merged cells |
| DOC / XLS / PPT / RTF / ODT / ODS / ODP | Optional, unevaluated | No engine enabled; none moved to Later based on tests |
| DOCM / XLSM / PPTM / DOTX / XLTX / POTX / PPSX | Later, as specified | Unsupported |

## Safety scope

Markdown image/file references are never followed by implemented code. Links require an allowed scheme and confirmation. Document HTML/XAML is never instantiated. The worker uses a Windows Job Object with a 256 MB memory cap, one-process limit and kill-on-close. Parent enforces 20 seconds for opening and 32 MB for serialized output.

These are resource controls, not privilege isolation. No AppContainer/restricted token, OS network denial, source-handle race hardening or full process-failure suite yet. Archive validation checks metadata; actual decompressed-byte enforcement must be added before Office ZIP ingestion. Secure XML helpers are tested but not connected to an Office reader.

Originals use shared read/write/delete access. Current text paths keep data in memory and make no temporary disk copies. Final modification checks may reject a source moved during opening. Signature/control-character screening rejects obvious binary mismatches; extension selects presentation among text/CSV/Markdown, which have no unique magic bytes.
