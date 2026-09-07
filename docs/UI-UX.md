# UI and UX Specification

## Experience Goals

DeskAI should feel calm, native, transparent, and reversible. It must communicate scope and consequences more clearly than a normal file manager. Fluent Windows patterns are appropriate, but visual polish never hides warnings or incomplete features.

## Information Architecture

Primary navigation:

- **Dashboard** — health summary, authorized folders, pending suggestions, recent activity, and undo.
- **Organize** — choose a root, scan, build a plan, review, approve, execute, and inspect results.
- **Search** — filters, natural-language query, results, and Smart Collections.
- **Automation** — deterministic rules, simulations, approval scope, schedules/watchers, and run history.
- **Settings** — AI mode/provider, privacy/disclosure, authorized and protected locations, appearance, data/history, diagnostics, and About.

Storage insights may begin on Dashboard and later gain a dedicated view. Workspace Profiles/Design appear only when implemented, not as misleading active navigation.

## First-Run Experience

1. Explain that DeskAI is local-first and does nothing until a folder is selected.
2. State the safety sequence: scan → suggestion → validation → preview → approval → execution → undo.
3. Default to Rule Engine Only; offer AI setup later.
4. Ask the user to add a folder with a trusted Windows picker. Do not pre-authorize personal roots silently.
5. Explain Allowed, Restricted, and Protected in plain language.
6. Offer a safe sample/demo folder before real files.

Do not overwhelm the first run with every future feature or provider.

## Dashboard

Suggested hierarchy:

```text
Greeting / status                         Active mode: Local / Rule-only / Provider

Organization Health 78/100                Improve my score
Desktop 61   Downloads 42   Other 89      (only authorized roots)

Needs attention                          Storage overview
Possible duplicates: 12                  Used / available
Old downloads: 39                        largest categories
Unorganized items: 74

Recent activity                          Undo last eligible action
```

The score opens an explanation of inputs and recommendations. It must not imply that moving more files is always better. Values are local unless telemetry is separately enabled.

## Organize Flow

### Select and scan

Show selected root, permission badge, exclusions, scan depth, metadata/content scope, and cloud/local processing state. Progress is cancellable and reports files scanned, skipped, and errors without blocking the UI.

### Plan preview

Group operations into Suggested, Warnings, Conflicts, and Blocked. Each row shows checkbox/eligibility, operation icon/type, source, destination, reason, provenance, confidence if meaningful, and safety status. Provide filters and a side-by-side tree preview for larger plans.

Blocked operations are not selectable. Editing a destination or exclusions creates a new plan revision and visibly triggers revalidation. The approval button says exactly what will happen, for example “Move 14 and rename 3 files,” not merely “Continue.”

### Execution and result

Show per-operation progress, allow safe cancellation between operations, and never turn a partial result into a green generic success. Result states include Completed, Partially completed, Failed safely, and Cancelled, with counts and actionable errors. Surface history and Undo when eligible.

### Undo

Preview undo consequences and conflicts. Explain that files changed after the original operation may require manual resolution. Never imply undo is guaranteed until validation completes.

## Safety Language

Prefer precise, neutral labels:

- “DeskAI can scan metadata in this folder.”
- “This plan contains 2 conflicts and cannot run yet.”
- “Cloud AI will receive filenames and extensions; file contents remain local.”
- “This item is protected and was not analyzed.”

Avoid vague text such as “The AI has access,” “Everything is safe,” or “Your files never leave your computer” when a cloud feature is active.

Status colors always pair with icons and text: Allowed, Needs review, Conflict, Blocked. Color alone must not communicate meaning.

## Privacy Dashboard

Make active state glanceable:

```text
Internet/provider use: Off
AI processing: Rule Engine Only
Cloud data shared: None
Telemetry: Off
Authorized folders: 3
Protected entries: 5
```

Changing from local/rule-only to cloud requires provider selection and a disclosure review. Expanding a disclosure category requires confirmation. Provide controls to remove credentials, revoke a root, clear index/history according to retention rules, and export redacted diagnostics.

## Search and Smart Collections

Always show actual scope (“Searching 3 authorized folders”). Natural-language input translates to visible filter chips so the user can correct interpretation. Results show path context, classification source, and why they matched. A Smart Collection is labeled virtual; saving it does not move files.

## Rules and Automation

Use a readable “When / If / Then / Scope” editor. When AI drafts a rule, show the deterministic interpretation and a simulation against sample/current indexed files before approval. Clearly distinguish enabled, scheduled/watched, manual-only, paused, and needs-review. Provide a kill switch/pause-all action.

## Visual Direction

- Use WinUI/Fluent conventions, system typography, spacing, rounded surfaces, and light/dark themes.
- Favor calm neutrals with one accent; reserve warning/error colors for meaning.
- Use density appropriate for file lists with optional comfortable mode.
- File icons/thumbnails must not leak content to cloud services.
- Animations are subtle, respect reduced-motion settings, and never delay confirmation.
- Wallpapers/themes should affect preview/design areas later, not readability of safety UI.

## Accessibility and Localization

All functions must be keyboard reachable with logical focus order, visible focus, automation names, scalable text, and adequate contrast. Announce scan/execution progress without excessive screen-reader noise. Do not encode actions only in icons. Prepare strings for localization and avoid building sentences by concatenation. Respect Windows high contrast, text scaling, locale, date, number, and path display conventions.

## Empty, Loading, and Error States

Every page has a truthful empty state with the next safe action. Use skeleton/progress only for real work, support cancellation, preserve recoverable user selections, and present per-item errors where possible. Settings/provider errors must not prevent rule-only local operation.

## V0.1 UI Scope

Implement a native shell, navigation, theme support from system defaults, placeholder page headings/descriptions, and a visible “Foundation only—no files are being accessed” status. Do not simulate fake scan results or functional buttons. The first real vertical UI slice belongs to V0.2.

## V0.2 Preview Demonstration

The implemented Organize page uses progressive disclosure. Its main surface shows a short Review → Choose → Try it flow, recognizable filenames, friendly source/destination folder names, simple Ready/Not included states, one plain-language attention card, and a button that states how many sample files will be organized. Internal create-folder operations are automatic and hidden from the main list. Plan revision, exact temporary path, and supporting-operation counts remain available in a collapsed Technical details section.

The sample uses the controlled temporary executor introduced in V0.2 step 5. Practice mode is named prominently and explains in one sentence that personal files are not used. Conflicts use calm language and remain unselected; technical safety terminology is not required to complete the demo.

Step 8 adds a separate, optional “Preview a folder” card below the practice flow. Its short description says exactly what is read and that moving/deleting remain off. Choosing a folder opens the native Windows picker and then a confirmation dialog showing the path and metadata scope. Results stay collapsed behind a file count by default, and Disconnect revokes permission without changing files.
