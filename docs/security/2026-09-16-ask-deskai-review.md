# Security Review: Ask DeskAI (V1.1, ADR 0035)

- Date: 2026-09-16
- Scope: the Ask DeskAI card on Home; `SentenceTask.Question`, the question prompt,
  `AiSentenceReading.ReadQuestion`, `AskDeskAiService`, `AskDeskAiViewModel`, the reply buttons.
- Gate: cloud transmission (`docs/SECURITY.md`). Written with the code.

## What changes

A third kind of sentence can leave the computer: a question typed on Home. Nothing else is new
in the request. What is new is that DeskAI acts on the answer itself — searching, summing, or
pointing — and shows the outcome with a button.

## What may be sent

The typed question (trimmed, at most 256 characters), the task, today's date, and the fixed
prompt. Nothing about any file, folder, or location; nothing DeskAI found; no key in the body.

## Threat cases and controls

| # | Threat | Control | Test |
|---|---|---|---|
| 1 | File names, folder names, or locations in the request | The request type has no field for them; the test asserts the recorded body holds the question and none of the folder's file names or the sandbox path | `AskDeskAiPageTests.A_search_question_sends_the_words_alone…` |
| 2 | Results sent back to the AI as a "conversation" | There is no second request; each question is one request and the reply is built locally | design; every page test records exactly one request per question |
| 3 | AI prose on the screen as fact | The AI's answer is a kind, a folder word, and a search shape; every reply string is DeskAI's own wording | `AskDeskAiServiceTests` (readings), `AskDeskAiPageTests` (reply wording) |
| 4 | A kind DeskAI does not know, or extra properties such as a command | `ReadQuestion` refuses the whole answer; the page shows a refusal and adds no reply | `AskDeskAiServiceTests.An_off_shape_question_answer…`, `AskDeskAiPageTests.An_unsure_answer…` |
| 5 | A folder word that is a path or an instruction | Reduced to letters, digits, spaces, and hyphens; matched by name to a connected folder or a personal folder, else the reply says where DeskAI works | `AskDeskAiServiceTests` (`..\Windows\System32` becomes words), `AskDeskAiPageTests.A_tidy_question_about_somewhere_else…` |
| 6 | A reply button that moves a file | The three actions open pages through the pages' own requests or the Your folders dialog; tidying still needs the permission and Tidy | `AskDeskAiPageTests.A_tidy_question_about_a_connected_folder…` (Organize asks permission, file untouched) |
| 7 | Connecting a folder from a reply without the dialog | The page shows `PersonalFolderDialogs.ConfirmConnectAsync` first; the view model's `Act` returns null for Connect so it cannot connect on its own | `AskDeskAiPageTests.A_tidy_question_about_an_unconnected_personal_folder…` |
| 8 | The service gaining reach | Holds the sentence service, search, storage, connected folders, the personal-folder policy, and the clock; no executor, journal, scanner, index, extractor, fingerprinter, vault, or folder service | `AskDeskAiServiceTests.Constructor_CannotReachAnything…` |
| 9 | Sending with AI off, without consent, past the cap, or after a settings change | Inherited from `SentenceAiService.SendAsync` and `ConfiguredSuggestionProvider` | `SentenceAiServiceTests`, `SentenceReadingProviderTests`, `AskDeskAiPageTests.With_AI_off…` |

## User-facing disclosure

The card line says only the question is sent and nothing about the files; the dialog shows the
words, the service, its address, and that DeskAI replies itself; help topic `home.ask` says the
same in three parts.

## Amendment, same day: asked once per service

The owner found a dialog before every question tiring and chose the first-use-only form (ADR
0035, amended). What that changes, and what holds it:

| # | Threat | Control | Test |
|---|---|---|---|
| A1 | A first question sent before the person ever agreed | `AskDeskAiService.AskAsync` refuses unless `NeedsPermissionAsync` is false or the caller passes the dialog's fresh yes; the page passes it only after the dialog returned Send | `AskDeskAiPageTests.Before_that_yes_nothing_is_sent_even_if_asked_to_send` |
| A2 | A yes for one service carried to another | The stored value is the service's name and address; a different service does not match, so it asks again before the first question there | `AskDeskAiPageTests.Choosing_a_different_AI_service_makes_DeskAI_ask_again` |
| A3 | A standing permission a person cannot see or undo | The line under the box states which way it stands and names the service; **Ask me each time** removes the yes with no dialog; Start fresh removes it with everything else | `AskDeskAiPageTests.Ask_me_each_time_brings_the_question_back`, `Start_fresh_forgets_the_agreement_too`, `DeskAI_asks_before_the_first_question_only…` (the Note wording) |
| A4 | Agreeing to more than was understood | The first dialog says DeskAI asks this once for that service, that questions then go on Ask, and how to bring the question back | dialog text (checked by hand; `MANUAL-TESTING.md`, "Ask DeskAI" steps 3–5) |
| A5 | The remembered yes leaking something | The value is a service name and a host, both already in the settings row; nothing about a file, and it is not in the backup file | `FreshStartPageTests`, `BackupPageTests` (backup holds rules and saved searches only) |

What did not change: the words sent, the strict reading, the deterministic answers, and the
buttons. The other three AI buttons still ask every time.

## Result

Accepted. The AI's reach is unchanged: a few typed facts in, DeskAI's own deterministic work out.

## Still not accepted

Sending any result or file information back; multi-turn conversation; AI text shown as a reply;
a reply button that runs a tidy.
