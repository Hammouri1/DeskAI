# ADR 0040: Per-search local PDF OCR

Status: accepted, 2026-09-21.

## Decision

DeskAI may use Windows on-device OCR for scanned or flattened PDFs only after a separate
confirmation for that search. Existing content and PDF grants are still required. The pass
opens at most 10 indexed PDFs, each no larger than 8 MB, and renders at most the first 20
pages. Recognized text is capped at 256 KB per file, kept only in memory, and is neither
persisted nor sent to an AI or network service.

Normal Search continues to read embedded PDF text only. AI picture reading remains disabled
for launch. OCR results are labeled approximate and tell the person to verify the original.

## Why

Many legitimate PDFs are flattened page images. A visible name can therefore be absent from
the PDF text layer. Treating OCR as ordinary PDF-text permission would silently widen an
existing grant from reading text objects to rendering page pixels. A per-run confirmation
keeps that new access visible and revocable without adding a stored permission.

## Consequences

OCR may miss stylized, small, rotated, handwritten, or unsupported-language text. It is a
search aid, not evidence that a PDF does or does not contain a phrase. It remains read-only;
no OCR result can propose or execute a filesystem change.
