# ADR 0011: AI Provider, Disclosure, and Credential Boundaries

- Status: Accepted
- Date: 2026-09-08

## Context

Optional models can improve ambiguous classification, but file names are untrusted, cloud calls may expose data and cost money, and a provider key must never become ordinary application configuration. Provider failure must not weaken deterministic organization.

## Decision

Core owns provider-neutral minimized DTOs and statuses. `AiRequestBuilder` filters disclosure before adapters. AI adapters receive no filesystem/executor services and return only category advice for opaque requested IDs. A strict versioned parser rejects unknown properties, commands, invented/duplicate IDs, invalid values, and oversized responses.

Rule Engine Only remains default. Local mode accepts only explicit loopback HTTP(S). Cloud mode supports one fixed-host Google Gemini adapter, protected by saved category consent and a second enable confirmation. Redirects, retries, and fallback are disabled. Gemini credentials use Windows Credential Manager; SQLite keeps only a reference. An atomic daily request cap, timeout, cancellation, and payload bounds limit cost and resource use.

## Consequences

AI can fail, be offline, return malicious JSON, or be disabled without affecting deterministic organization. Live-provider testing is manual and optional. Exact dollar cost is not estimated because pricing varies by model/provider; provider token usage and a hard request cap are shown instead. Adding another cloud provider requires its own fixed-endpoint/authentication review and cannot reuse Gemini consent silently.
