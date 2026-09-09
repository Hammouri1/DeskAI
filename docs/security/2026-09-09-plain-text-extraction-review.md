# Plain-Text Extraction Security Review

- Date: 2026-09-09
- Scope: V0.4 step 8, stage 2 — the first code in DeskAI that opens a file
- Result: accepted for plain text behind the content gate; still unreachable from the app

The gate review of the same date deferred extraction and listed what a further review had to
cover before any file is opened. Each item is answered below.

## What Changed

`IContentTextExtractor` (Core) and `PlainTextExtractor` (Infrastructure) read a bounded
prefix of text from one file in a content-authorized folder. `TextExtraction` carries the
words or a specific reason none were read.

## The Deferred Items, Answered

- **Consent step.** Still not built, and still the deferred gate. Nothing in the product can
  grant `MetadataAndContent`, so no real folder can reach this code.
- **Formats and parser.** Plain text only, via the BCL. No PDF or Office parsing, so no
  third-party parser runs over attacker-controlled binary structure. Unsupported extensions
  are refused before opening, so those files are never touched. See ADR 0015.
- **Bounds.** 64 KB per file by default, read from the beginning; one file per call. A very
  large file cannot be pulled into memory.
- **Malformed and hostile documents.** A zero byte marks the file as not text rather than
  decoding it into nonsense. Invalid UTF-8 is replaced, never thrown, because the bytes are
  whatever happened to be in the file. A test feeds prompt-injection wording through and
  asserts it comes back as inert text.
- **Storage and deletion.** Extracted text is returned to the caller and stored nowhere, so
  there is nothing to retain, disclose, or delete.
- **Disconnect for a content-authorized folder.** Still unsupported in
  `SqliteAuthorizedRootRepository.RemoveAsync`, and still moot while no such folder can
  exist. It must be fixed in the slice that first grants the scope.

## Threats and Controls

- **Reading without permission.** `RootCapabilities.CanReadContent` is checked first, before
  any path work, so an unauthorized folder never reaches a path calculation or a handle.
  Tested for all three other scopes.
- **Escaping the folder.** Root and relative path go through `IPathPolicy`; the resolved path
  is canonicalized and must sit inside the canonicalized root, with a separator required
  after the prefix so a sibling folder with a similar name is not mistaken for a child.
- **Links and reparse points.** Refused before opening and checked again once the handle is
  open, since a file can be swapped for a link in between. The handle still refers to what
  was opened, so a link found afterwards abandons the read.
- **Creating files by reading them.** `FileMode.Open`, never `OpenOrCreate`. A test asserts a
  missing file stays missing.
- **Untrusted content.** Extracted text is data with no command vocabulary and no path to an
  operation. It reaches no provider: the extractor is registered nowhere and `DeskAI.AI`
  receives no filesystem service.
- **Resource exhaustion.** Bounded read, single file per call, cancellation honoured.

## Deferred Gate

Sending extracted text to any AI provider is **not** accepted. Extracted content is a
disclosure category of its own and does not inherit the metadata disclosure consent. Reading
PDF or Office documents is **not** accepted and requires a dependency justification, a
malformed-document strategy, and its own review. Storing extracted text is **not** accepted
and requires schema, retention, and deletion design.
