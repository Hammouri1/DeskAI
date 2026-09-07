# ADR 0004: Deterministic Classification and Folder Recipes

- Status: Accepted
- Date: 2026-09-07

## Context

Common file types should be classified locally, quickly, and explainably before optional AI is considered. Classification must remain separate from planning and mutation. Users will eventually need different organization layouts without changing the classification engine.

## Decision

Keep the classifier and recipe model in Core. `FileTypeRuleSet` contains explicit extension-to-kind/category rules and rejects duplicate extensions case-insensitively. Matching prefers the longest extension so compound forms such as `.tar.gz` remain deterministic. A narrow filename heuristic identifies screenshots only after the extension has established that the item is an image.

`Classification` uses closed category, kind, and provenance enums plus confidence and explanation. `FolderRecipe` separately maps categories to versioned, root-relative destination directories. It rejects rooted, traversal, alternate-data-stream, wildcard, and malformed segments but performs no filesystem action.

## Alternatives

- MIME/content sniffing was rejected because it would read file content and belongs behind a later permission gate.
- A single extension-to-folder dictionary was rejected because it mixes understanding a file with deciding where a user wants it organized.
- AI-first classification was rejected because ordinary extensions are faster, cheaper, offline, and more predictable.
- Arbitrary category strings were rejected inside the domain because closed enums make downstream planning safer and exhaustive.

## Consequences

Rule-only mode handles common formats with no network access. Classification and destination preferences can evolve independently. Adding or changing built-in rules requires tests and review; user-configurable persistence/UI remains future work. Unknown files remain unclassified instead of being guessed into a folder.
