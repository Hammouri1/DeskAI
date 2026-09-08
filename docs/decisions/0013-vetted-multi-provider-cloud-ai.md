# ADR 0013: Vetted Multi-Provider Cloud AI

- Status: Accepted
- Date: 2026-09-09
- Supersedes the single-provider part of [ADR 0011](0011-ai-provider-and-credential-boundaries.md)

## Context

V0.3 shipped one cloud adapter hard-wired to OpenRouter. The owner's intent for
bring-your-own-key was broader: a person should be able to use whichever AI service they
already pay for, rather than being told which company to open an account with.

That intent collides with an existing security rule. `SECURITY.md` forbids arbitrary
user-typed cloud addresses, because a request built from the categories a user agreed to
share must reach the service they chose and nowhere else. A free-text endpoint field turns
a privacy decision into a typo, and makes server-side request forgery a UI feature.

## Decision

Replace the single hard-wired provider with a closed, compile-time catalog of vetted
services, and let the user pick from it.

`CloudProvider` records an ID, display name, one fixed HTTPS chat-completions address, a
provider-specific credential reference, a model hint, and where to obtain a key.
`CloudProviderCatalog.All` lists OpenRouter, OpenAI, Groq, Mistral, DeepSeek, and
Together AI. All six speak the same OpenAI-style chat-completions request and response
shape, which is why one adapter — `CloudChatCompletionsSuggestionProvider` — serves all of
them. `CloudProvider.Create` refuses any entry that is not a plain HTTPS address with a
default port, no credentials, no query, and no fragment, guarding the list against a
careless future edit.

Each provider owns a separate Windows Credential Manager reference
(`DeskAI/OpenRouter`, `DeskAI/OpenAI`, and so on). Switching services therefore cannot
reuse, expose, or overwrite a key saved for a different company, and removing a key
removes only the selected one. SQLite continues to store the reference, never the key.

`ConfiguredSuggestionProvider` resolves the saved `ProviderId` through the catalog. An
unrecognized ID is refused rather than guessed at, so a tampered settings row cannot pick
a destination. The daily request cap is counted per provider ID, so changing services does
not grant a fresh allowance for the day.

## Alternatives

- A free-text endpoint field was rejected: it is exactly the arbitrary cloud address
  `SECURITY.md` forbids, and it would let a mistyped or malicious host receive the data a
  user approved for someone else.
- Keeping OpenRouter only was rejected because it forces a specific commercial
  relationship on the user for no security benefit.
- Adding providers with a different API shape — Anthropic's Messages API, Google Gemini —
  was rejected *for now*. Each needs its own request/response mapping, error mapping, and
  contract tests. Listing them without that work would be a claim DeskAI cannot honour.
- One shared credential entry for all cloud providers was rejected outright: it would make
  sending one company's key to another a single bug away.

## Consequences

A person can use the AI service they already have, and every message names that exact
service instead of saying "the cloud". The consent dialog states the host that will
receive the data. Adding a provider is a reviewed code change with tests, not a settings
field, which keeps the destination list auditable.

The user still cannot point DeskAI at a service that is not listed. That is a deliberate
limit, not an oversight; a reviewed "custom endpoint" path would need its own threat
analysis, an explicit extra confirmation naming the host, and a decision about local and
private-network addresses. It is recorded here as an open question rather than shipped
quietly.
