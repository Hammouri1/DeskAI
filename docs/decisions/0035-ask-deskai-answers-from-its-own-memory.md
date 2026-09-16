# ADR 0035: Ask DeskAI Answers From Its Own Memory

- Status: Accepted
- Date: 2026-09-16
- Review: `docs/security/2026-09-16-ask-deskai-review.md`

## Context

The third of the owner's AI features for V1.1: a place on Home to ask, in plain words, "what's
taking space in Downloads?" or "find my slides from last month" and get an answer, so DeskAI
feels like an AI workspace rather than a set of forms. The obvious shape — send the question and
the folder's contents to the AI and show its prose — is exactly what DeskAI must not do: it would
send file names and locations, and it would put untrusted AI text on the screen as fact.

## Decision

- **The AI reads the question; DeskAI answers it.** Only the typed question leaves the computer,
  through the same two-step sentence flow as Search and rules (ADR 0033). The AI answers with a
  kind — search, space, tidy, unsure — the folder name the person wrote, and for a search the
  same fixed search shape. `AiSentenceReading.ReadQuestion` refuses anything else whole.
- **Every answer is deterministic and local.** `AskDeskAiService` runs the search on the index,
  reads the storage summary, or points at Organize, and writes the reply in DeskAI's own words.
  The only file names shown come from the local index, as Search shows them. Nothing found is
  sent back; there is no conversation, each question stands alone, and the card's list is not
  saved.
- **One button per reply, never one that changes a file.** Open in Search and Open in Organize
  go through the same `SearchRequest` and `OrganizeRequest` the pages' own buttons use; Connect
  goes through the Your folders dialog. A tidy still needs the folder's permission and a press
  of Tidy.
- **The service holds nothing that opens or changes a file.** A reflection test fixes that.

## Consequences

- The dialog before every question is the same as before every sentence. It is the safe default
  for a first version; if the owner finds it too much for a chat box, a first-use-only dialog
  is the natural next step and a separate decision.
- A folder name the AI echoes is matched to a connected folder by name, case-insensitively, on
  this computer; the AI never learns which folders exist.
