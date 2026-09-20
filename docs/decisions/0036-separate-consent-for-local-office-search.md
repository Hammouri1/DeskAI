# ADR 0036: Separate Consent for Local Office Search

- Status: Accepted
- Date: 2026-09-20

## Context

Search already reads a bounded prefix of plain-text files after a separate folder permission.
That permission was given while the dialog explicitly said Word and Excel files would stay
closed. Treating it as permission for those formats would silently change a person's choice.
The owner also wants natural-language searches such as “Word document containing space.”

## Decision

Append `MetadataAndDocuments = 4` to the stored root-scope enum. Existing scope 3 retains
plain-text access only. A new dialog names `.docx` and `.xlsx`; only its affirmative action
upgrades the folder to scope 4. Both scopes can be withdrawn, and neither grants a file
change. The extractor independently refuses Office files unless scope 4 is present.

Read modern Office Open XML using the .NET ZIP and XML readers only, from a read-only handle.
Do not extract ZIP entries to disk, resolve XML entities, run macros, follow hyperlinks, or
parse embedded objects. Keep the whole container at most 8 MB, at most 1,000 entries, at most
40 selected XML parts, at most 256 KB of XML per part, and at most 64 KB of resulting words
per file. Search opens at most 50 supported files per request; it keeps snippets only in
memory. A word in a search phrase is matched inside eligible files, while document type,
size, category, and date still narrow candidates before reading.

An optional AI service may interpret only the sentence the person typed, exactly as before.
No file text, photo, path, or search result is sent to it. The app, not the model, chooses
which approved root and files may be read.

## Deferred

PDF and image-content search are **not** implied by this permission. PDF parsing/OCR needs
an independently reviewed, crash-resistant adapter. Identifying objects in a photo needs
a local vision capability or a separate, per-request cloud-image disclosure with a model
that explicitly supports images. Neither is silently enabled by connecting a folder,
configuring OpenRouter, or approving Office reading.

See the [security review](../security/2026-09-20-local-document-search-review.md).
