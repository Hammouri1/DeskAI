# Product Specification

## Product Statement

DeskAI is a private, local-first Windows workspace that helps users organize, find, understand, and maintain files safely. It is not an autonomous computer-control agent. It combines predictable rules with optional AI to produce reviewable suggestions while deterministic code enforces permissions.

The long-term positioning is: **a private AI workspace for your computer** across four connected areas: Organize, Search, Automate, and Design.

## Problem

Desktop and Downloads folders accumulate screenshots, documents, installers, archives, assignments, and poorly named files. Manual cleanup is slow, ordinary file managers provide little context, and agentic tools can feel unsafe. Users need useful automation that remains understandable and reversible.

## Target Users

- Students managing courses, assignments, slides, research, and screenshots.
- Developers managing projects, archives, installers, references, and temporary downloads.
- Knowledge workers who want a clean workspace and fast retrieval.
- Privacy-conscious users who prefer local processing and no required account.
- Power users who want rules and automation without handing an LLM unrestricted access.

The first release should serve one local Windows user on one computer. Enterprise administration, multi-user collaboration, and cloud synchronization are later considerations, not implicit V1 requirements.

## Jobs to Be Done

- “Help me clean this folder, but show me exactly what you will do.”
- “Group these files meaningfully without losing anything.”
- “Find the assignment/project/document I vaguely remember.”
- “Show what consumes storage and which files may be duplicates.”
- “Turn my repeated cleanup habit into a predictable rule.”
- “Keep private data local and tell me when anything would go online.”
- “Let me reverse a cleanup if the result is not what I wanted.”

## Product Principles

1. **Safety beats autonomy.** There is no unrestricted mode.
2. **Local first.** Core organization, rules, indexing, and history work without a DeskAI server.
3. **Useful without AI.** Extensions, metadata, hashes, dates, and deterministic rules handle common work.
4. **Preview first.** A plan has no side effects; users see sources, destinations, reasons, warnings, and conflicts.
5. **Reversible by design.** Moves and renames are journaled; deletion is never silently permanent.
6. **Explicit permission.** Users choose roots, protected items, content access, provider, and cloud data categories.
7. **Explainability.** Every suggestion says why it exists and whether it came from a rule, heuristic, or AI.
8. **Progressive power.** Start manual and safe; automation is earned through explicit approval.

## Core Experience (V1)

1. The user adds a folder through a trusted picker.
2. DeskAI displays the authorized scope and scans metadata asynchronously.
3. Deterministic classifiers handle obvious file types; optional AI helps with ambiguous items.
4. A pure planning stage creates an `OrganizationPlan` containing proposed operations only.
5. The Safety layer validates roots, paths, protections, collisions, and operation policy.
6. A preview groups safe, warning, conflict, and blocked operations with explanations.
7. The user can exclude individual operations and approves the final plan.
8. The executor revalidates immediately before each change, writes history, and reports partial failures honestly.
9. The user can undo eligible completed operations.

### V1 Acceptance Outcome

A user can safely select one ordinary folder, scan it, receive deterministic organization suggestions, review the full plan, approve moves/renames/folder creation, see an audit trail, and undo supported changes. The flow must work without cloud AI.

## Feature Areas

### Dashboard

Show authorized folders, recent activity, pending suggestions, undo availability, and an explainable **Organization Health** score. Future score inputs may include root clutter, duplicate candidates, stale downloads, and unresolved items. It must never shame the user or imply precision the metric does not have.

### Organizer

Scan user-selected roots; classify common formats; identify screenshots, documents, university work, programming projects, images, videos, archives, and installers; suggest folders and names; and provide a visual plan. Favorites and protected items are always honored.

### Search and Smart Collections

Index allowed metadata locally. Support structured filters first, then natural-language translation into local queries. Smart Collections are saved virtual queries such as University, Coding Projects, PDFs, Screenshots, and Recently Used. A Smart Collection does not move files.

### Storage Intelligence

Summarize storage by type and folder, find old or large items, identify archives/installers, and detect exact duplicates by size plus content hash. “Possible duplicate” remains a review state. This feature never automatically deletes.

### Rules and Automation

Users may describe a rule in plain language. AI may translate it to a typed condition/action model, but the user reviews that deterministic rule. Approved rules run without repeatedly consulting an LLM. Automation records trigger, inputs, outcomes, and errors; risky changes still require confirmation.

### Workspace Profiles and Design

Profiles such as Student, Developer, Gaming, Productivity, Minimal, and Custom bundle suggested collections, folder templates, and views. Later design features may suggest layouts, themes, matching folder icons, wallpapers, widgets, shortcuts, and workspace templates. Image generation and desktop-shell modification are outside early milestones.

### Plugins

A future plugin model may add classifiers, importers, search enrichers, or organization recipes through capability-scoped contracts. Plugins must not bypass Safety or directly mutate files. Signing, isolation, permissions, compatibility, and review must be designed before loading third-party code.

## Privacy Dashboard

The Settings experience should make these facts visible:

- internet access status;
- active mode and provider;
- cloud data categories allowed;
- local authorized roots;
- protected items;
- telemetry status;
- recent provider requests described by category, not sensitive payload;
- uploaded file count, normally zero unless a user explicitly allowed content transfer.

Avoid an unconditional “files never leave your computer” claim when cloud processing is enabled. State the active behavior precisely.

## Permission Model

- **Allowed:** suggestions may include approved operation types within the root; execution still follows preview/approval policy.
- **Restricted:** metadata analysis may be allowed, but each mutation requires confirmation.
- **Protected:** exclude from analysis and mutation. Do not disclose paths or contents to AI.

System and security-sensitive paths are permanently protected regardless of user wording or model output.

## Non-Goals for Early Releases

- General-purpose AI control of Windows.
- Shell execution, registry editing, software installation, or launching arbitrary programs.
- Automatic permanent deletion.
- Antivirus, backup, disk-repair, or data-recovery guarantees.
- Replacing File Explorer.
- Cross-platform support in V1.
- Required DeskAI account, subscription, hosted inference, or synchronization backend.
- Training models on user content.
- Collaborative/shared workspaces.
- Wallpapers, widgets, plugins, and desktop-shell modification in the foundation milestone.

## Success Measures

Measure locally and privately by default:

- plan approval rate and per-operation exclusions;
- successful execution and undo rate;
- zero unauthorized-root mutations;
- zero silent overwrites or automatic permanent deletions;
- scan and plan latency for representative folder sizes;
- crash-free operations and recoverable interrupted transactions;
- percentage of useful classifications handled without AI;
- user-reported trust and clarity.

Telemetry is off unless deliberately designed, documented, consented to, and privacy-preserving. Local diagnostics must redact secrets and sensitive paths where possible.

## Open Product Decisions

- Final application name and visual identity.
- Minimum Windows and .NET versions, selected when tooling is inspected.
- Packaging strategy (packaged MSIX versus supported alternatives) and code signing.
- Exact local model runtime and whether any model is bundled; licensing must be reviewed.
- Whether content extraction ships in V1 and which formats are supported safely.
- Retention policy for index and operation history.
- How scheduled automation behaves while the app is closed.
