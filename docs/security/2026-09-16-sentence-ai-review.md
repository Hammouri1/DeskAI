# Security Review: AI Reads a Typed Sentence (V1.1, ADR 0033)

- Date: 2026-09-16
- Scope: "Let AI read this" on Search and Automatic tasks; `SentenceAiService`,
  `AiSentenceReading`, `ReadSentenceAsync` on every adapter, the prompt, the dialog.
- Gate: cloud transmission (`docs/SECURITY.md`, "Security Review Gates"). Written with the code.

## What changes

A second kind of request can leave the computer: the words a person typed into the Search box
or the rule sentence box, with today's date. The answer is a small JSON shape that DeskAI turns
into a sentence in its own vocabulary and then reads exactly as a typed sentence.

## What may be sent

The typed sentence (trimmed, at most 256 characters), the task, today's date, and the fixed
prompt. Nothing about any file, folder, or location; no DeskAI ID; no key in the body.

## Threat cases and controls

| # | Threat | Control | Test |
|---|---|---|---|
| 1 | The request carries file names, folder names, or locations by accident | The request type has no field for them; the page passes only the phrase; the test asserts the recorded body holds the sentence and none of the folder's file or folder names or the sandbox path | `SentenceAiPageTests.Search_shows_the_words…`, `SentenceReadingProviderTests.ReadSentenceAsync_PostsTheSentenceAlone…` |
| 2 | Sending before the person sees the words | Two steps, `PrepareAsync` and `AskAsync`; the page calls the second only after the dialog's Send | `SentenceAiPageTests.Cancelling_the_dialog_sends_nothing…`, `SentenceAiServiceTests.Preparing_sends_nothing…` |
| 3 | The AI choice, service, or address changes between the dialog and Send | `AskAsync` reloads the settings and refuses if the mode, provider, or destination differs from the question | `SentenceAiServiceTests.A_changed_AI_choice…`, `SentenceAiPageTests.Turning_AI_off_between…` |
| 4 | The answer names a folder outside the connected folder, a path, a command, or a category DeskAI lacks | Strict reading refuses unknown properties, another schema, unknown categories, bad numbers, and any destination with a separator, drive, traversal, trailing dot, control character, or marker word; the whole answer is dropped | `AiSentenceReadingTests` (off-shape theory, destination theory), `SentenceAiPageTests.A_folder_the_AI_invents…` |
| 5 | Prompt injection through the sentence or the answer's free text | The sentence is marked untrusted in the prompt; free text in the answer is reduced to letters, digits, spaces, and hyphens and cut at 64 characters, then read as ordinary search words; a rule's destination markers are removed from text | `AiSentenceReadingTests.Free_text_becomes_harmless_words…` |
| 6 | AI's reading reaches somewhere typing could not | The reading is a sentence in the fixed vocabulary that goes through the same `NaturalLanguageQueryTranslator` or `RuleDraftTranslator` as typed words; a reading those do not understand is refused | `AiSentenceReadingTests` round-trip tests, `SentenceAiServiceTests.Asking_sends…` |
| 7 | Oversized or malformed answers | Response bounded at 8 KB by the transport limit; envelope errors map to MalformedResponse; reading refuses over-limit JSON before parsing | `SentenceReadingProviderTests.ReadSentenceAsync_AnUnreadableEnvelope…`, `AiSentenceReadingTests.An_answer_over_the_size_limit…` |
| 8 | Sending with AI off, without consent, to an unknown provider, or past the daily cap | `ConfiguredSuggestionProvider.ReadSentenceAsync` applies the same mode, consent, catalog, and budget checks as `SuggestAsync`; the page shows no button while AI is off | `SentenceReadingProviderTests.Configured_*`, `SentenceAiPageTests.With_AI_off…`, `The_daily_limit_counts…` |
| 9 | The key leaks into the body or a shown message | Bearer header only; the service's explanation text has the key replaced before display, as before | `SentenceReadingProviderTests.ReadSentenceAsync_PostsTheSentenceAlone…` |
| 10 | The service gains a way to look at files | `SentenceAiService` takes settings, the provider, and the clock only | `SentenceAiServiceTests.Constructor_CannotReachAnything…` |
| 11 | A saved search holds something only AI can read | What is saved is the reading in DeskAI's vocabulary, not the AI's JSON; pinning counts it deterministically | `SentenceAiPageTests.Search_shows_the_words…` (CanSaveCurrentSearch) and the existing pin tests |

## User-facing disclosure

The button carries the service's name. The dialog shows the words in a box, the service, its
address, "Nothing about your files is sent", and that the AI cannot search, move, or change
anything itself. The page line after Send quotes the reading and invites a correction. Help
topics `search.askAi` and `automation.askAi` say the same in three parts.

## Result

Accepted. AI's reach is unchanged: it still only ever produces advice that deterministic code
reads, and on these two pages the advice is literally a sentence a person could have typed.

## Still not accepted

Sending any file information with a sentence; a free-form chat that could carry a file name;
letting the AI's JSON become a query or rule without passing through the fixed vocabulary.
