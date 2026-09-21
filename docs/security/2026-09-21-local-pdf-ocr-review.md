# Security review: local scanned-PDF OCR

Reviewed 2026-09-21 for the launch build.

| Risk | Control |
| --- | --- |
| Old PDF consent is broadened silently | OCR has a separate confirmation on every run; cancel opens no page pixels. |
| Private page images leave the computer | The adapter uses Windows `PdfDocument` and `OcrEngine` only; no AI transport is involved. |
| OCR content is retained | Page images and recognized words live in memory for the active search and are never indexed or stored. |
| A large document exhausts resources | Existing authorized-root/path/link checks run first; OCR then caps each PDF at 8 MB, the pass at 10 PDFs, pages at 20, and recognized text at 256 KB. |
| Approximate words are treated as fact | Confirmation and result wording require verification in the original PDF; matched page number is shown where available. |
| Document text becomes an instruction | OCR output remains inert search data and has no route to file operations, shell, or AI tools. |

Residual risk: Windows language availability and page design affect accuracy. A missed match
is possible and is stated in the UI. This is acceptable for a bounded search aid, not for an
authoritative compliance or legal search.
