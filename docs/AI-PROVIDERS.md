# AI Providers and Modes

## Role of AI

AI improves ambiguous classification, naming explanations, natural-language search translation, and rule drafting. It is not the filesystem controller. Common organization should be handled with extensions, metadata, hashes, and deterministic rules first; a useful design target is mostly conventional processing with AI reserved for uncertain cases.

## Supported Product Modes

### Rule Engine Only

No model is required. File types, dates, paths, metadata, user rules, and local indexes produce plans. This is the simplest, fastest, and most predictable mode and must remain a first-class experience.

### Local AI

Inference runs on the user's computer, potentially through a local runtime with a constrained HTTP or native adapter. Benefits are privacy, offline use, and no provider bill; costs include model download size, slower inference on some hardware, memory/GPU requirements, and model licensing. DeskAI must not silently download or start a model. Show storage/hardware expectations and verify redistribution rights before bundling anything.

### Bring Your Own API Key

The desktop app connects directly to the provider selected by the user. DeskAI has no required proxy and does not pay for usage. The UI must show that provider pricing, retention, and availability belong to that provider. Keys use Windows-protected storage and data sharing is opt-in and minimized.

DeskAI implements two adapters: an explicit OpenAI-compatible loopback address for a separately installed local runtime, and one cloud adapter that serves a vetted catalog of services using the user's own key and chosen model name. Arbitrary cloud addresses are deliberately not accepted because they could send disclosed information to an unexpected destination.

### Supported cloud services

The user chooses from a closed, compile-time list. Every entry has a fixed HTTPS address and its own credential entry:

| Service | Fixed address | Credential reference |
| --- | --- | --- |
| OpenRouter | `https://openrouter.ai/api/v1/chat/completions` | `DeskAI/OpenRouter` |
| OpenAI | `https://api.openai.com/v1/chat/completions` | `DeskAI/OpenAI` |
| Groq | `https://api.groq.com/openai/v1/chat/completions` | `DeskAI/Groq` |
| Mistral | `https://api.mistral.ai/v1/chat/completions` | `DeskAI/Mistral` |
| DeepSeek | `https://api.deepseek.com/chat/completions` | `DeskAI/DeepSeek` |
| Together AI | `https://api.together.xyz/v1/chat/completions` | `DeskAI/TogetherAI` |

All six speak the same OpenAI-style chat-completions shape, which is why one adapter serves them all. A service with a different API shape — such as Anthropic's Messages API or Google Gemini — needs its own adapter, error mapping, and contract tests before it can be listed. Adding a provider is a reviewed code change, not a settings field.

Because each service has its own credential reference, switching services never reuses or exposes a key saved for another company, and removing a key removes only the selected one.

## Provider-Neutral Contract

Start with capabilities, not provider names. A conceptual contract may offer:

```text
ClassifyAsync(ClassificationRequest, CancellationToken)
SuggestOrganizationAsync(OrganizationContext, CancellationToken)
TranslateSearchAsync(NaturalLanguageQuery, CancellationToken)
DraftRuleAsync(RuleDraftRequest, CancellationToken)
```

Requests contain minimal DTOs and explicit disclosure categories. Responses contain typed suggestions, confidence, and explanation—never executable commands or concrete filesystem services. Consider capability flags because local models and providers differ in structured output, vision, context size, and embeddings.

Keep transport DTOs internal to adapters. Map them into Core types only after strict validation.

## Data Disclosure Model

User-controlled categories:

- extension/file type;
- selected metadata such as size and timestamps;
- filename;
- folder names or redacted relative path;
- full path (normally unnecessary and off);
- extracted document text/content;
- image pixels/content.

The request builder applies policy before provider code sees data. A provider adapter cannot expand disclosure. Display a concise request summary and make “Local” versus named cloud provider obvious. Protected items never enter requests.

## Structured Output

Define a versioned JSON schema or equivalent narrow format with:

- known schema version;
- stable opaque file IDs rather than trusting paths where possible;
- allow-listed category and suggestion types;
- bounded string/list lengths;
- optional confidence in a validated range;
- plain-language reason.

Reject unknown operations, missing IDs, duplicate IDs, invalid enums, extra-large responses, destinations outside the allowed proposal vocabulary, and references to files not in the request. Do not repair dangerous output heuristically. A syntactically valid response is still untrusted until Safety validates the resulting plan.

## Resilience and Cost Controls

- Provider-specific timeout, cancellation, retry, and rate-limit behavior.
- No blind retry for non-idempotent or costly calls; classify failures clearly.
- Batch within safe context and privacy limits; cache non-sensitive results using input/model/schema version.
- Let users set model and optional spending/request limits.
- Never silently switch provider or send local data to cloud after local failure.
- Surface offline, authentication, quota, malformed-output, and safety rejection separately.
- A 401 means the key was not accepted; a 403 means this request was refused (OpenRouter uses
  it for moderation) and is never reported as a key problem. Both pass on the service's own
  `error.message`, shown as text only: control characters removed, at most 200 characters,
  and the saved key replaced if the service echoes it.
- A pasted key is trimmed at the edges. A key with a space or line break inside (such as a
  pasted `Bearer ...`) is refused with a reason. A key that does not start the way the chosen
  service's keys usually do (`sk-or-` for OpenRouter, `gsk_` for Groq) is saved with a
  warning, never refused, because formats can change.

## Prompting

Prompts describe the classification task, permitted taxonomy, output schema, and that content is untrusted data. They should favor “unknown” over invention. Prompt changes are versioned and evaluated with a fixed corpus. Prompts do not enforce filesystem safety.

## Testing Providers

Use deterministic fakes for unit/integration tests. Contract tests cover successful mapping, malformed JSON, unknown fields/operations, invented IDs, prompt injection in filenames/content, oversized output, timeouts, cancellation, rate limits, and redaction. Live-provider tests are manual/opt-in, never required for the normal suite, never use personal files, and incur no hidden cost.

## Implementation Sequence

1. Define Core request/response and fake provider.
2. Build rule-only classification and complete the safe plan flow.
3. Add disclosure-policy/request-builder tests.
4. Add one local or mock-compatible adapter behind a feature flag.
5. Add one cloud adapter with secure credentials and consent UI.
6. Learn from the contract before adding more providers.
7. Add natural-language search and rule drafting only after typed query/rule models exist.

## Provider Settings

Store provider ID, endpoint (where allowed), model ID, capability cache, timeout, disclosure policy, and credential reference. Never store the secret itself in SQLite. Validate custom endpoints, require HTTPS except an explicitly local loopback runtime, and guard against server-side request forgery-style access to sensitive local network endpoints.

## V0.3 Implemented Behavior

- Rule Engine Only is the default and needs no provider.
- Local mode accepts only loopback HTTP(S); DeskAI neither installs nor launches the runtime.
- Cloud mode posts to the selected service's single fixed address with an `Authorization: Bearer` header. Users may choose the service and the model name, but cannot change or type a cloud destination.
- An unrecognized saved provider ID is refused rather than guessed at, so a tampered settings row cannot choose a destination or reuse another service's key.
- The daily request cap is counted per service, so switching services does not grant a fresh daily allowance.
- Cloud mode requires a consent switch and a second disclosure confirmation that names the exact host which will receive the data.
- The request builder includes only allowed metadata fields and excludes protected file IDs.
- JSON output is versioned, byte/count bounded, duplicate-property checked, unknown-field rejecting, and limited to requested IDs and known categories.
- Requests have a user-configurable timeout and daily cloud-request cap. There are no automatic retries or provider fallbacks.
- Provider-reported token counts are displayed when available. DeskAI does not guess dollar cost; the user checks provider billing/pricing.
- AI is asked only from Tidy a folder, about files in a folder the person allowed DeskAI to tidy, after a dialog shows exactly what will be sent. (The practice page's AI ideas on generated sample records were retired on 2026-09-11, ADR 0023.)

## V0.6 Step 2b: Your Own Folders

- On "Tidy a folder", AI can suggest a category for files in a connected, tidy-permitted
  folder: by default only files DeskAI cannot place by type, or, if chosen, every file the
  person's rules do not place. Rules always win; left-alone files are never asked about.
- `TidyAiService` prepares the request and the page shows it before anything is sent; only
  Send sends that same request. Send re-checks tidy permission, the AI choice, its
  destination, and the sharing choices, and refuses without sending if any changed.
- From a real folder at most file type, size and date, and name can be sent
  (`TidyAiService.RealFolderShareable`), each only if allowed. Full locations and folder names
  are never sent. Each file carries a random per-request number, not DeskAI's file ID.
- The answer goes through the same strict parser; the service checks again that every number
  was one it sent. A category maps to a folder through `TidyFolderRecipe`, so for "Ask AI" AI
  never names a folder (for "Plan this folder", see V1.1 below). The AI's reason text is not
  displayed. Confidence below 0.7 shows as "AI isn't sure" and starts unticked; no percentage
  is shown.
- At most 100 files per request; one press is one request against the daily limit.
- See ADR 0020 and `docs/security/2026-09-10-real-folder-ai-disclosure-review.md`.

## V1.1: Reading a Typed Sentence (ADR 0033)

- **Let AI read this** on Search and on Automatic tasks sends the words the person typed, and
  nothing else: no file names, sizes, dates, folder names, locations, or IDs, because the
  sentence is the only input the task has. Today's date goes with it so "last summer" can be
  worked out. A dialog shows the exact sentence, the service, and its address first; only
  Send sends. The same consent switch, closed catalog, fixed address, key handling, timeout,
  and daily cap apply as for asking about files; there is no sharing check because nothing
  about a file is involved.
- The provider contract gained one call, `ReadSentenceAsync`, alongside `SuggestAsync`. The
  prompt (`AiPromptFactory.CreateSentencePrompt`) asks for a small fixed JSON shape per task:
  for a search, endings, categories from the closed list, a larger-than and smaller-than size
  in bytes, a number of days, and free text; for a rule, one ending, one category, sizes, an
  older-than number of days, name text, and a destination folder name. Adapters return the
  answer text unread.
- **AI never produces a query or a rule.** `AiSentenceReading` in Core reads the JSON strictly
  (unknown properties, another schema version, an unknown category, a bad number, or a
  destination that is not a plain folder name refuse the whole answer; free text is reduced to
  letters, digits, spaces, and hyphens, with the words to/into/in removed) and writes the facts
  as a sentence in DeskAI's own fixed vocabulary — "photos larger than 5 mb last 30 days
  holiday", "move .pdf statement into Bank". That sentence is put in the box and goes through
  exactly the deterministic reader a typed one meets. So AI gains no reach a person typing does
  not have, the reading is visible and editable, and a saved or pinned search still holds words
  DeskAI can read on its own tomorrow.
- `SentenceAiService` holds the settings, the AI connection, and the clock, and nothing that
  can see a file (a reflection test fixes that). It refuses an empty or over-long sentence
  before building anything, refuses to send if the AI choice changed since the dialog, and
  refuses a reading DeskAI's own reader would not understand.
- Review: `docs/security/2026-09-16-sentence-ai-review.md`.

## V1.1: Plan This Folder (ADR 0034)

- **Plan this folder with AI** on Organize sends exactly what "Ask AI" would about every file
  the person's rules do not place (the page switches to that choice first), under the same
  sharing rules and the same dialog, with one more line saying the AI may also name folders.
- The request carries `AiSuggestionTask.PlanFolder`. The prompt asks for at most 12 plain
  folder names and one `folder` per file; `category` becomes optional. `StructuredSuggestionParser`
  refuses a `folder` on a classification, requires one on a plan, checks each with
  `FolderNameCheck` (the same rule a typed template name passes: one segment, no separators,
  drive, wildcards, reserved names, trailing dot, control characters, at most 64), counts
  distinct names case-insensitively, and refuses the whole plan on the first bad name or the
  thirteenth folder. `TidyAiService` checks every name again before it becomes advice.
- A planned name reaches the planner as `TidyAiAdvice.FolderName` and takes the place of the
  recipe's folder; the plan validator and the executor check the path again. Groups are named
  by the AI's folders, ideas show "AI idea from <service>", unsure ones start unticked, rules
  still win, and Tidy and undo are the ordinary ones.
- Review: `docs/security/2026-09-16-plan-folder-review.md`.

## V1.1: Ask DeskAI (ADR 0035)

- A card on Home: a question in the person's own words, such as "what's taking space in
  Downloads?". Only the question is sent, through the same `SentenceAiService` two-step flow as
  any sentence (`SentenceTask.Question`), with the same dialog, consent, catalog, key, timeout,
  and daily cap. Nothing about any file goes with it, and nothing DeskAI finds is ever sent back;
  each question stands alone and the card's list is not saved.
- The AI answers with a kind — `search`, `space`, `tidy`, or `unsure` — the folder name the
  person wrote (reduced to harmless words), and for a search the same search shape as on
  Search. `AiSentenceReading.ReadQuestion` refuses anything else whole.
- `AskDeskAiService` then does the work itself, deterministically: a search runs through
  `FileSearchService` on the local index (narrowed to the named folder when it is connected)
  and replies with up to five matching names and an "Open in Search" button; a space question
  reads `StorageSummaryService` and replies with totals, the biggest kinds, and the largest
  file, with a button to see big files in Search; a tidy question offers "Open in Organize" on
  a connected folder, "Connect <folder>" for one of the four personal folders (through the Your
  folders dialog), or says where DeskAI works; unsure says what can be asked. Every reply is
  DeskAI's wording; the AI's text never reaches the screen.
- The service holds the sentence service, the search, storage, and connected-folder services,
  the personal-folder policy, and the clock — nothing that opens or changes a file; a reflection
  test fixes that. Its buttons open pages through the same `SearchRequest` and `OrganizeRequest`
  the pages' own buttons use.
- Review: `docs/security/2026-09-16-ask-deskai-review.md`.
