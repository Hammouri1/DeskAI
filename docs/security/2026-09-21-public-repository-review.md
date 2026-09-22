# Public repository review — 2026-09-21

## Scope

This review covers the tracked repository, all reachable local Git revisions, the release
workflow, and the owner-found title-bar and Home-layout follow-up. It is a focused pre-publish
review, not a guarantee that software can never contain a vulnerability.

## Checks and findings

- No database, environment file, key file, certificate, log, user-settings file, office
  document, PDF, screenshot, archive, or local build output is tracked.
- A credential-pattern scan across 325 reachable revisions found only deliberately fake test
  tokens in test files. No real API key or private key was identified.
- Current source contains no shell, PowerShell, CMD, installer, inbound listener, self-update,
  permanent-delete, or recursive-delete feature. The fixed PDF worker is the only child process.
- The only Windows setting writer is the already reviewed, user-confirmed wallpaper adapter.
  File mutation remains constrained to the validated move/create/empty-folder undo pipeline.
- The release workflow performs locked restore, the full test suite, a self-contained publish,
  an SBOM, a known-vulnerable-package check, a SHA-256 checksum, and a completeness check for the
  WinUI resource index and compiled interface. The exact V1.1.0 download reproduced the missing-UI
  failure; V1.1.1 includes the Windows App SDK publish workaround and must be smoke-tested again
  from the final GitHub asset. The release remains unsigned, so Windows SmartScreen can warn.
- GitHub reports the repository as public with secret scanning and push protection enabled.
  Private vulnerability reporting was enabled during this review, and a root `SECURITY.md` now
  directs reports there.
- The public handoff contained names of owner-supplied test documents and a personal folder.
  Those details were removed from the current tree.

## Unresolved privacy item

The reachable Git history contains 151 commits whose author metadata uses the owner's Gmail
address. Current Git configuration already uses GitHub's `users.noreply.github.com` address.
Removing the older address requires rewriting commit history and force-pushing rewritten branches
and tags; that changes commit IDs and must be an explicit owner decision. Until then, the email
remains visible in the public commit history even though it is absent from the current files.

## Release decision

The source and current tracked files are suitable for public review after the UI correction passes
the full quality gates. Do not claim zero risk. Before another release, either accept the historical
email disclosure or perform a coordinated history rewrite, then rebuild the tag from the reviewed
history. Code signing remains recommended for a later release.
