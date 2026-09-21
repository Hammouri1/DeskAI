# ADR 0039: Separate Consent for PowerPoint Slide Text

- Status: Accepted
- Date: 2026-09-21

## Context

The owner wants a query such as “PowerPoint with Hammouri on a slide” to find a file in a
nested connected folder. Earlier Word, Excel, and PDF dialogs did not permit opening
PowerPoint. Slide pictures, embedded objects, and older binary `.ppt` files are a separate
visual-processing problem.

## Decision

Append stored reading scopes 6 (`MetadataDocumentsAndSlides`) and 7
(`MetadataDocumentsPdfAndSlides`), preserving all earlier numeric meanings. PDF and slide
grants are independent after the Word/Excel grant. Search shows a separate confirmation
and a direct **Stop PowerPoint reading** action. Stopping either PDF or slides retains the
other grant; stopping all content reading returns to metadata only. Neither grant allows
file mutation.

Only modern `.pptx` slide XML is read locally. The reader considers a 32 MB ZIP container,
at most 1,000 entries, the first 200 numbered slide parts, 1 MB per slide XML part, and
256 KB of returned text. It disables DTDs and external entities, refuses deep XML, never
extracts archive entries, and ignores relationships, pictures, macros, and embedded files.
The shared content-search limit remains 50 files and 20 seconds. Each text hit identifies
the slide it came from. Text and slide labels live only in the current result, not SQLite
or a provider request.

## Consequences

Words drawn into a picture need OCR and are not found by this grant. A word beyond the
bounded slides or text may be missed; partial reads are reported. The `.pptx` parser uses
the BCL ZIP/XML reader in process, not an OS sandbox. Its fixed XML part selection and
bounds reduce parser reach, but malformed inputs remain untrusted and become skipped files.

See the [security review](../security/2026-09-21-slide-text-search-review.md).
