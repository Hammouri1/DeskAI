# PDF Text Search Security Review

- Date: 2026-09-20
- Scope: local text extraction from PDFs in connected folders
- Decision: accept the bounded helper design under a separate PDF grant (ADR 0037)

| Threat | Control and evidence |
| --- | --- |
| Old consent silently opens PDFs | Scope 5 is appended. `RootCapabilitiesTests`, `PlainTextExtractorTests`, and `SearchPageTests` exercise the old grant and the new yes. |
| Forged path, protected file, or link escapes a root | The existing path policy, canonical containment, and link checks run before opening the file. The helper receives bytes only. Existing extractor path tests cover refusal; PDF-specific negative cases use generated temp folders. |
| Damaged or hostile PDF crashes or hangs Search | PdfPig runs in a separate process. Parent has a 10-second deadline, bounded pipes, and kills a stuck child. A crash or malformed response skips that file. `PlainTextExtractorTests` supplies malformed data. |
| A large or complex PDF consumes unbounded work | 32 MB per file, 100 pages, 256 KB output, 50 attempted files per search, and a 20-second search deadline checked between files. The helper also refuses input beyond 32 MB. An oversized generated file is tested. |
| Encrypted or image-only PDF is misrepresented as searched | Encrypted, unreadable, and empty-text outcomes are skipped; the UI reports skipped files and partial reads. No OCR is attempted. |
| Private text reaches AI or storage | Extracted text and snippets are in memory for the active result only. The helper has no provider, database, or file path; the Search service has no AI transport. The optional AI action still sends only the typed sentence after its own dialog. |
| Revocation leaves PDF reading enabled | Stop PDF returns to scope 4, and Stop reading returns to scope 0. The page test searches again after revocation. Disconnect removes the root record through the existing repository path. |

## Residual risk and rollback

The helper is not an OS sandbox and runs as the signed-in user. A vulnerability in its PDF
parser could do more than crash. The process boundary contains ordinary parser faults and
timeouts, but does not promise containment of malicious code execution. Removing the helper
or reverting this feature leaves scope 5 unrecognized by older capability code, so it fails
closed; returning to scope 4 withdraws PDF reading without disturbing the Office grant.
No PDF content is stored, so there is no extracted-text cache to erase.
