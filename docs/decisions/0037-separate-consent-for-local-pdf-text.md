# ADR 0037: Separate Consent for Local PDF Text

- Status: Accepted
- Date: 2026-09-20

## Context

The existing text and Office grants explicitly kept PDFs closed. PDF bytes are untrusted;
an in-process parser can crash or hang the whole app on malformed input. Windows.Data.Pdf
renders pages but does not expose their text. OCR and cloud processing would be larger privacy
decisions.

## Decision

Append scope 5, `MetadataDocumentsAndPdf`. It requires a separate affirmative dialog after
document reading has been allowed. The folder row shows the PDF permission and offers a
direct **Stop PDF reading** action, which returns to scope 4. Stopping all content reading
returns to metadata-only. Existing scopes keep their exact meaning and numeric values.

The trusted extractor performs the root, protected-path, containment, and link checks. It
opens the file read-only and sends at most 8 MB through standard input to a fixed helper
beside the application. The helper receives bytes, not a path, uses PdfPig 0.1.16 in strict
parsing mode, and returns at most 64 KB of text from the first 20 pages. The parent waits at
most five seconds per PDF, kills a stuck helper, and treats a crash or bad response as a
skipped file. Search attempts at most 50 files and has a 20-second limit checked between
files. Skips and partial reads are visible; no extracted text is persisted or sent to AI.

The helper is a crash and timeout boundary, not a Windows security sandbox: it runs with the
same account rights as DeskAI. The fixed executable and no-path protocol reduce its reach,
but a parser vulnerability remains possible. Keep PdfPig updated and revisit OS-level
isolation before expanding the format surface.

## Deferred

Scanned/image-only PDF OCR, annotations, embedded files, forms, and photo understanding are
outside this slice. Publishing bundles a self-contained helper with the app. If it is absent,
PDFs are skipped rather than parsed inside the UI process.

See the [security review](../security/2026-09-20-pdf-text-search-review.md).
