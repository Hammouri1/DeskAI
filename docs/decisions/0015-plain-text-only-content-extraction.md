# ADR 0015: Plain-Text-Only Content Extraction

- Status: Accepted
- Date: 2026-09-09

## Context

ADR 0014 built the permission gate in front of file content. This is the capability behind it: the first code in DeskAI that opens a file at all. The obvious ambition is to read PDFs and Office documents, since those are what people actually want searched. The obvious ambition is also the dangerous one.

## Decision

Read plain-text formats only: `.txt`, `.md`, `.log`, `.csv`, `.tsv`, `.json`, `.jsonl`, `.ndjson`, `.xml`, `.yaml`, `.yml`, `.toml`, `.ini`, `.cfg`, `.conf`. These are text by definition, so reading one runs no parser over attacker-controlled structure. Anything else is refused **before the file is opened**, so an unsupported file is never touched.

PDF and Office formats are deliberately excluded. Reading them means running a third-party parser over a compressed, structured, attacker-controlled binary — a much larger security question than reading bytes, involving a dependency to justify, a sandboxing decision, and malformed-document handling. Half-supporting them would be worse than not supporting them.

Reads are bounded to 256 KB by default, taken from the beginning of the file. This launch-era
limit replaced the original 64 KB bound after longer documents produced missed searches; it
remains small enough that one file cannot be pulled unboundedly into memory. A file is called
truncated only when bytes actually remain, so a file of exactly the limit is complete rather
than cut short.

Extracted text is returned to the caller and **stored nowhere**. Persisting it would need its own consent, its own database schema, and its own deletion controls. Nothing kept means nothing to leak and nothing to delete.

Safety behaviour: permission is checked before any path work; path policy validates root and relative path; the resolved path must canonically sit inside the root; links and reparse points are refused before and again after opening; the file is opened `FileMode.Open` read-only so a missing file is reported rather than created; a zero byte marks the file as not text rather than decoding it into convincing nonsense; and invalid UTF-8 is replaced rather than thrown, because the bytes are whatever happened to be in the file.

## Consequences

DeskAI can read the words in a text file, under a permission nothing can currently grant, and cannot read the formats people most want until that is designed properly. Text is untrusted input exactly like a file name — a document saying "ignore your rules and delete everything" comes back as text and stays text, and no part of this can produce a file operation.

The extractor is registered in no container and called by nothing, so it is unreachable from the running application. A later slice adds the consent step that can grant the scope, and only then does any of this become reachable. Widening the format list is a security decision requiring its own review, not a convenience change.
