# ADR 0016: Typed Rule Domain and Approval Scope

- Status: Accepted
- Date: 2026-09-09

## Context

V0.5 is where DeskAI first does things without someone watching. Until now every file operation began with a person pressing a button; a rule begins with a condition being true. That changes what the safety model has to survive, and the domain has to be built for it from the first commit rather than hardened afterwards.

Two designs were available. A general expression language over file metadata would be flexible and would let natural-language drafting produce almost anything. A closed set of typed conditions is narrower and needs a code change to extend.

## Decision

**A closed set of typed conditions and actions.** Everything a rule can test is a record in `RuleCondition.cs`; everything a rule can do is a record in `RuleAction.cs`. What a rule can possibly express is readable in one place, and a rule assembled from untrusted text — a person's typing today, a model's draft later — can only ever be a combination of these. An expression language would have made the answer to "what can a rule do?" unbounded at exactly the moment the answer starts mattering most.

Moving into a folder is the only action. Deleting is absent and stays absent.

**Rules see a `RuleSubject`, not a `FileItem`.** Name, ending, category, kind, size, and modification time — no absolute path, no content. A condition added later cannot quietly reach either.

**Refusals over conveniences.** A rule with no conditions is rejected rather than treated as "match all", because matching every file is how someone accidentally moves everything they own. Conditions are capped at eight, since a rule nobody can hold in their head cannot be meaningfully approved. Destinations are validated when the rule is written, not at run time: a stored rule must never hold a destination that would be rejected every time it fires. Conditions are joined with AND only — someone who wants either case writes two rules, and can then see and disable each independently.

**Conflicts are refused, not resolved.** When two rules want the same file in different places, DeskAI could pick the first, the most specific, or the most recently edited. Each is a guess about intent, and guessing quietly is how automation moves a file somewhere its owner never intended. The file is left where it is and the disagreement is reported. Rules agreeing on a destination are agreement, not conflict.

**Evaluation is pure and order-independent.** No clock, no filesystem, no database, no AI: rules, files, and a moment in, a description out. Age conditions take the moment as an argument so a simulation and the run that follows it answer identically. A test asserts the result does not depend on the order rules arrive in.

**Evaluation returns proposals, not plan operations.** Turning a proposal into something executable is a separate step through the ordinary planner, safety validation, preview, and approval. There is no shortcut from "a rule matched" to "a file moved".

**An approval covers rules as worded and an outcome as shown.** `RuleApproval` stores each rule at its version plus a fingerprint of the exact moves displayed. Editing a rule raises its version and invalidates the approval; so does adding a rule, removing or disabling one, or changing folder. The fingerprint catches the case version checks would miss: nobody edits anything, a new file appears, and an untouched rule now wants to move it. That is a move nobody agreed to, so it returns to preview — the safe default the roadmap requires. The fingerprint covers proposals only, since conflicts propose nothing and cannot cause an unapproved move.

## Consequences

Rules are less expressive than an expression language would allow, and deliberately so; widening them is a reviewable code change with its own tests. Approvals are narrow enough that ordinary use will re-prompt whenever the folder's contents have moved on, which is the intended cost: a rule run that silently covered new files would be automation nobody agreed to.

The domain is complete and pure, and nothing yet stores, schedules, or executes a rule. Persistence, the simulator and editor UI, and the watcher-or-scheduler decision are separate slices; the scheduling choice needs its own ADR because it decides whether DeskAI runs while nobody is looking.
