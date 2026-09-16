# ADR 0033: AI Reads a Typed Sentence Into DeskAI's Own Words

- Status: Accepted
- Date: 2026-09-16
- Review: `docs/security/2026-09-16-sentence-ai-review.md`

## Context

After V1.0 the owner said there was "no real use of the AI": it could be asked, from Organize
only, to name a category for files DeskAI could not place by type. Search and rule drafting read
sentences with a fixed local vocabulary, so "the slides from my trip last summer" was not
understood. The owner chose three AI features; this is the first: plain language on Search and
Automatic tasks, through the AI the person already set up.

## Decision

- **AI reads the sentence and nothing else.** The request carries the typed words, the task,
  today's date, and limits. No file name, size, date, folder name, location, or ID is ever part of
  it. A dialog shows the exact words, the service, and its address; only Send sends. The same
  consent switch, closed catalog, fixed address, key handling, timeout, and daily cap apply as for
  asking about files.
- **AI answers with a few typed facts, not a query or a rule.** One small JSON shape per task,
  read strictly by `AiSentenceReading`: unknown properties, another schema version, an unknown
  category, a bad number, or a destination that is not a plain folder name refuse the whole
  answer; free text is reduced to harmless words.
- **DeskAI writes those facts as a sentence in its own fixed vocabulary**, puts it in the box,
  and reads it with exactly the deterministic reader a typed sentence meets. AI therefore gains no
  reach a person typing does not have; the reading is visible and editable; and a saved or pinned
  search still holds words DeskAI can read on its own. A reading DeskAI's own reader would not
  understand is refused rather than shown.
- **`SentenceAiService` holds the settings, the AI connection, and the clock**, and nothing that
  can see a file. A reflection test fixes that, as for `TidyAiService`.
- **Where it appears:** a plain "Let <service> read this" button beside Search and beside "Read my
  sentence" on Automatic tasks, only while AI is set up, each with its own help topic.

## Consequences

- The provider contract has two calls. Every adapter, including the fakes, implements both.
- The rule reader's remark of 2026-09-09 ("when AI drafting is added later it must produce this
  same draft through the same validated path") is honoured literally: AI's output is a sentence
  the same reader reads.
- Sizes and dates an AI names for a rule are carried in the sentence but, as with a typed
  sentence, the rule form has no boxes for them yet and says so.
