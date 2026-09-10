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
- The practice page's AI ideas use only six generated sample records and cannot modify the plan.

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
  was one it sent. A category maps to a folder through `TidyFolderRecipe`, so AI never names a
  folder. The AI's reason text is not displayed. Confidence below 0.7 shows as "AI isn't sure"
  and starts unticked; no percentage is shown.
- At most 100 files per request; one press is one request against the daily limit.
- See ADR 0020 and `docs/security/2026-09-10-real-folder-ai-disclosure-review.md`.
