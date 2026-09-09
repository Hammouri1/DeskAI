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

Home now shows real numbers from the index rather than placeholders: folders connected, files remembered with their total size, and how many have not changed in six months. Below that, size by category and the largest files explain where space went. Every number is a reading of remembered metadata, so the page states when it was last checked instead of implying it is live, and says "DeskAI could not read the storage summary" rather than showing zeros if the read fails. The headline states what is actually connected, for the same reason the navigation pane does. There is deliberately no cleanup button anywhere on this page: the summary describes, and any cleanup still goes through preview and approval. Possible duplicates follow the same rule: files sharing an exact size are listed as "possible duplicates" with an "up to" saving, never as confirmed copies, because DeskAI has compared their sizes and not their contents. Confirming would mean reading file bytes, which the metadata-only permission these folders were connected under does not allow.

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

## Implemented Visual System

The shared vocabulary lives in `src/DeskAI.App/Themes/DeskAITheme.xaml`, merged from `App.xaml` after `XamlControlsResources` so DeskAI's palette wins. No page invents its own colours.

The system is called **instrument panel**, and it has one governing rule:

> The accent colour means "safe or confirmed". It is never used as decoration.

A person should be able to learn one thing — green means DeskAI is allowed to do this — and have it hold on every screen. Anything that is merely structure uses the neutral line colour instead. This is why the palette is declared per theme rather than borrowed from the Windows accent: an arbitrary user-chosen accent cannot carry a fixed meaning.

Palette tokens are defined for both themes in `ResourceDictionary.ThemeDictionaries`:

| Token | Dark (`Default`) | Light |
| --- | --- | --- |
| `DeskGroundBrush` | `#0F1216` | `#F6F7F9` |
| `DeskSurfaceBrush` | `#161B21` | `#FFFFFF` |
| `DeskSurfaceRaisedBrush` | `#1D242C` | `#FFFFFF` |
| `DeskLineBrush` | `#272E38` | `#E1E5EA` |
| `DeskAccentBrush` | `#4DD8A8` | `#0E8C64` |
| `DeskCautionBrush` | `#E8955A` | `#A8541B` |
| `DeskDangerBrush` | `#F0685C` | `#C0392B` |
| `DeskTextPrimaryBrush` | `#E7EBF0` | `#10161D` |
| `DeskTextSecondaryBrush` | `#8C97A5` | `#5A6572` |

The `HighContrast` dictionary maps every token back to `SystemColor*` brushes, so Windows high contrast overrides the palette entirely.

Shared styles:

- `HeroPanelStyle` — a status readout, not a banner. A 3px left rail in the accent carries the state; the corner is square on the rail edge (`CornerRadius="0,6,6,0"`) so the rail reads as an edge marker rather than a pill. Used once at the top of Home, Organize, and Privacy and AI.
- `CardStyle`, `SoftCardStyle`, `RowCardStyle` — surfaces at 6px/6px/4px radius, differentiated by fill weight rather than all sharing one radius. `SoftCardStyle` is transparent with a hairline only.
- `DeskDisplayStyle`, `PageTitleStyle`, `SectionTitleStyle`, `MetricStyle`, `BodySecondaryStyle`, `CaptionStyle` — one type ramp on Segoe UI Variable Display for headings and Segoe UI Variable Text for body, with negative tracking on the display sizes. `MetricStyle` sets numbers large and light so the value reads before its label.
- `StepBadgeStyle` — a quiet bordered chip. It is deliberately **not** accent-filled, because the accent is reserved for safety state.

Accent buttons override `AccentButtonBackground` as well as `AccentFillColorDefaultBrush`, because the WinUI accent button style takes its fill from the former; setting only the latter leaves the button on the Windows accent colour. They are used for actions that confirm or authorize something, never for ordinary commands — the Search button is a plain button, since searching confirms nothing.

`HeroSheenBrush` is retired. It remains defined as a transparent brush so any page still referencing the old gradient wash renders nothing rather than failing to load.

Three treatments were removed as generic and meaningless here: the all-caps eyebrow labels (`LOCAL AND PRIVATE`, `PRACTICE MODE`), the translucent gradient wash over the accent, and the 56–72px decorative icons in the page headers. Metrics that belong to one reading now share a single panel divided by hairlines instead of being split into identical repeated cards.

The window uses a Mica backdrop, pages paint `DeskGroundBrush`, and the navigation pane footer carries a permanent Practice-mode reminder on the same accent rail, because "which files can this app touch" should never require navigating to find out.

Status is expressed through `PreviewStatusLevel` (`Ready`, `Attention`, `Blocked`) mapped by converters to a system semantic brush, a paired Segoe Fluent glyph, and a tinted badge background. Colour is never alone: every badge carries an icon **and** the status word, so a blocked row still reads as blocked in greyscale or high contrast. `PreviewStatusLevel` is presentation severity only — Safety decides what is blocked, and the enum merely chooses how that decision is drawn.

Empty states stay truthful rather than becoming decorative. Automatic tasks states outright that DeskAI is doing nothing in the background, and no page implies a capability that does not exist.

The navigation pane always states the current scope, and that text is derived from what is actually connected rather than written as a fixed string. An earlier version hard-coded "Sample files only. Your personal folders are not connected.", which stayed on screen after a real folder was connected: the one label that promises what DeskAI can reach was the label that lied. It now reads "Practice mode" only while nothing is connected, and otherwise reports the folder and file counts. If the scope cannot be read it says so, and never falls back to the reassuring wording.

The scope a search reports and the folder list a page shows come from one predicate, `FileSearchService.IsSearchable`, so they cannot disagree. It requires both an `Allowed` permission and `MetadataOnly` scope; permission alone would include the controlled demo workspace and make the page claim to search a temporary folder nobody connected.

Search keeps three outcomes visibly distinct, because collapsing them is how a search screen starts lying:

| Outcome | What the page says |
| --- | --- |
| No folders connected | "No folders connected yet" and points to Organize |
| Phrase not understood | "I did not understand that", with examples. **No results are listed.** |
| Understood | A count, the matches, and the scope actually searched |

The second row is the important one. An unrecognised phrase produces a query with no filters, which would list every remembered file — a full listing presented as a search result. The page refuses instead.

Above the results, chips show how the phrase was read ("Photos", "Larger than 100 MB", "Changed in the last month"), so interpretation is visible and correctable rather than silently assumed. Vague words state their real threshold instead of hiding it. Scope is always stated — "Searched 2 connected folders" — so the page never implies the whole computer was searched, and a truncated list says so rather than passing as complete. Results show a folder name and a path relative to it, never an absolute path.

## Visual Direction

- Use WinUI/Fluent conventions and Windows system typefaces, with DeskAI's own declared palette for both light and dark. High contrast defers to Windows.
- Favor calm neutrals with one accent. The accent is reserved for "safe or confirmed" and is never decorative; warning and error colors likewise carry meaning only.
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

## V0.3 AI and Privacy UI

Settings is named “Privacy and AI” and starts with a short status card. Sharing controls use everyday names and require confirmation when allowing more information. The three main choices are “Don't use AI,” “AI running on this computer,” and “Online AI with my own key.” Choosing the online option reveals a list of supported services so a person can pick the one they already have an account with; the model hint, key label, agreement wording, and remove-key button all rename themselves to that service. Addresses, model names, timeouts, and daily limits are grouped below the main choice instead of leading with technical language. Online activation repeats exactly what may be shared, names the exact host that will receive it, and reminds the user that the chosen service controls pricing and service-side data handling.

Organize presents AI as an optional “second opinion” for generated samples. The card identifies where data would go, offers a Stop button, reports usage when available, and shows the category, confidence, explanation, and source for each idea. It explicitly states that AI cannot move, rename, or change files.
