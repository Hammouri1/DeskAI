# PowerPoint Slide Text Search Security Review

- Date: 2026-09-21
- Scope: bounded local `.pptx` slide text only
- Result: accepted with separate permission; visual and cloud-image search remain closed

| Threat | Control and evidence |
| --- | --- |
| Old reading grant silently opens presentations | New scopes 6 and 7; `RootCapabilitiesTests`, `PlainTextExtractorTests`, and the Search page test prove earlier grants cannot read slides. |
| PDF and slide permissions interfere | Independent scope transitions preserve the other grant; the page test withdraws slides while retaining PDF, then grants slides without PDF. |
| Path escape or link opens another file | Existing root/relative-path policy, canonical containment, link checks, and read-only handle run before the slide reader. No archive entry is written to disk. |
| ZIP/XML bomb or external entity | 8 MB container, 1,000 entries, 40 selected slides, 256 KB per slide part, XML depth 64, bounded text; DTDs prohibited and resolver null. Generated malicious XML and oversized-container tests are refused. |
| Pictures or embedded content read under text grant | Reader selects only `ppt/slides/slideN.xml` and DrawingML text nodes. A test includes a media entry and proves its contents are not returned. |
| Content leaks to AI or storage | Extractor has no provider dependency; current Search holds snippets and slide ranges only in memory. The existing AI button still sends only the typed sentence after its separate approval. |
| A partial result looks complete | Files checked reports partial reads; Search attempts at most 50 files and identifies a matching slide where available. |

The reader does not handle legacy `.ppt`, animations, notes, chart data, text inside
pictures, scanned pages, or visual scene search. A parse failure skips that file; it never
falls back to an unbounded parser. If this feature is reverted, scopes 6 and 7 fail closed
in older capability code rather than inheriting write permission.
