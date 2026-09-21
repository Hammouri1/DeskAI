# Content Consent and Inside-File Search Security Review

- Date: 2026-09-09
- Scope: V0.4 step 8, stage 3 — the consent that grants content access, and its one consumer
- Result: accepted; this is the point at which reading inside files becomes reachable

The two earlier reviews of this date built the gate and the reader while nothing could grant
the permission. This review covers the change that makes it real.

## What Changed

`ConnectedFolderService.AllowContentAsync` / `StopContentAsync` move a connected folder
between `MetadataOnly` and `MetadataAndContent`. The Search page asks first, with a dialog
naming exactly what will be opened. `ContentSearchService` is the only consumer of file
content: it finds files whose words match a typed phrase, in folders that allowed it.
`PlainTextExtractor` and `ContentSearchService` are now registered in the composition root.

## Threats and Controls

- **Consent reuse.** Connecting a folder grants metadata only, and a test asserts a freshly
  connected folder has no content permission. Reading inside is a separate question with its
  own dialog, which names the formats opened, the formats not opened, that nothing read is
  saved or sent, and that files still cannot be moved, renamed, or deleted.
- **Widening a mutation scope.** Only `MetadataOnly` and `MetadataAndContent` may be swapped
  between. The practice workspace and any organize-scoped folder are refused, so a consent
  belonging to the folder list cannot reach a scope that grants changes. Tested for both.
- **A permission that cannot be withdrawn.** `RemoveAsync` previously filtered on
  metadata-only alone, so allowing content would have made a folder impossible to
  disconnect. Both reading scopes are now accepted; the practice workspace is still excluded.
  Withdrawing content permission needs no confirmation and leaves the folder connected.
- **An invisible permission.** The folder row states in words whether DeskAI may read inside
  it. A permission a person cannot see is one they cannot reconsider.
- **Reading beyond the grant.** `ContentSearchService` selects roots with
  `RootCapabilities.CanReadContent` and reads only through `IContentTextExtractor`, which
  refuses independently. A test fake counts opened files and asserts zero for every
  non-content scope.
- **Unbounded reading.** At most 50 files per search, 256 KB each, and only files whose
  remembered name is a supported text format — decided from the index, so an unsupported
  file is never offered to the extractor. Phrases under three characters open nothing.
- **Silently partial answers.** The result carries how many files were read and whether the
  limit was reached, and the UI states it, so "nothing matched" is never mistaken for
  "nothing exists".
- **Untrusted content in the UI.** Snippets are file text: whitespace is collapsed and
  control characters dropped, and the text is displayed in a `TextBlock` and nothing more. A
  test puts instruction-like wording through and asserts it comes back as an ordinary
  snippet.
- **Retention.** Nothing extracted is stored. Withdrawing consent therefore leaves nothing
  behind to delete.

## AI Containment

Extracted text reaches no provider. `ContentSearchService` is the only consumer of content
and depends on the index, the roots, and the extractor — it has no AI dependency, and
`DeskAI.AI` still receives no filesystem or content service. Sending extracted text to a
provider remains **not accepted** and is a disclosure category of its own.

## Deferred Gate

Unchanged from the extraction review: reading PDF or Office documents, storing extracted
text, and disclosing content to any AI provider each remain unaccepted and require their own
review.
