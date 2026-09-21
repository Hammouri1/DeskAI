# Local Document Search Security Review

- Date: 2026-09-20
- Scope: search inside `.docx` and `.xlsx`, natural-language content terms, and Search UI
- Result: accepted only with the separate document-reading grant; PDF/image parsing and
  image upload remain blocked

## Threats and Controls

| Threat | Control and negative test |
| --- | --- |
| Existing text consent silently expands | New numeric scope 4 is appended, not substituted for scope 3. `RootCapabilitiesTests`, `PlainTextExtractorTests`, and `ConnectedFolderServiceTests` prove scope 3 cannot read Office content. The page shows a new dialog before upgrade. |
| A forged path or link escapes the connected folder | The same root and relative-path policy, canonical containment, link refusal, and read-only file handle used for text run before Office parsing. No ZIP entry is extracted to disk. Existing path/link tests remain in the suite. |
| Malformed or hostile ZIP/XML consumes memory or accesses a local/remote entity | Container, entry count, part count, expanded-part, XML-character/depth, and output bounds apply. `DtdProcessing.Prohibit` and a null XML resolver block external entities; a generated entity document is refused in a negative test. Parse failures become a per-file refusal. |
| A document gives instructions to the AI | Its text is inert search data only. Search has no executor or provider transport. Existing injection tests prove that document wording is shown only as a snippet. |
| A search sends private data to a cloud provider | The content extractor has no AI dependency. `Let AI read this` still sends only the typed sentence after its existing dialog; it receives no file text, image, path, or results. No real provider is called in tests. |
| A changed or huge document is read without a visible limit | At most 50 files are attempted per request; 256 KB of words per file, 8 MB per Word/Excel container. The UI reports count and truncation; snippets are held only for the current page visit. |
| Disconnect leaves a permission or tidy history | The repository's removal and tidy-grant SQL include scope 4; a SQLite test proves disconnect erases it. |

## Known Limits and Rollback

Only modern unencrypted `.docx` and `.xlsx` text parts are considered. This is not a full
Office parser; drawings, comments, embedded objects, macros, formulas, legacy `.doc`/`.xls`,
PDFs, and photos are not searched by content. If the bounded parser fails, DeskAI skips the
file and changes nothing. Withdrawing reading or disconnecting stops future reads; there is
no stored extracted text to erase. Reverting this change leaves scope 4 fail-closed in older
`RootCapabilities` code, so it cannot inherit mutation rights.

Excel search reads bounded shared-string and inline-text XML rather than reconstructing a
workbook's displayed cells. An unreferenced shared string could therefore match, and words
beyond the bounded parts/character limit may be missed. Results show a snippet for review,
not a claim that every cell was searched.

The PDF gate remains separate. In-process third-party PDF parsers have had malformed-input
crashes, including uncatchable stack overflow; merely adding a package and a size bound is
not an adequate review. Image-content search additionally needs a specific disclosure of
image pixels, provider destination/model, count/size/cost limit, and an explicit Send action.
