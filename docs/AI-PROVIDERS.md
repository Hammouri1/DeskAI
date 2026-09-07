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

Planned adapters may include OpenAI, Anthropic, Google Gemini, Groq, OpenRouter, and custom OpenAI-compatible endpoints. This list is direction, not a promise that they are implemented.

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
