# My Workspace: Starter Packs and Pinned Searches Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A "My workspace" page where a person can add a starter pack (saved searches plus switched-off rules, after a preview) and see pinned saved searches with honest counts.

**Architecture:** Packs are a fixed catalog in `DeskAI.Core.Workspace`. `StarterPackService` previews and adds through the existing `ISavedSearchRepository` and `IRuleRepository`; `PinnedSearchService` pins and counts through `FileSearchService`. A pinned flag is one new column (schema 13). `WorkspaceViewModel` (Presentation) drives a new WinUI page; `SearchRequest` hands one saved-search ID to Search.

**Tech Stack:** C# / .NET 10, WinUI 3, CommunityToolkit.Mvvm, Microsoft.Data.Sqlite, xUnit v3.

**Spec:** `docs/superpowers/specs/2026-09-14-my-workspace-starter-packs-design.md`

## Global Constraints

- Nothing in this slice changes a file. No Workspace type takes `IFolderTidyExecutor`, `IOperationJournal`, `IOrganizationPlanner`, `IFileScanner`, `IContentTextExtractor`, `ICredentialVault`, or any AI type.
- Pack rules are always saved with `isEnabled: false`, forced by the service.
- Name clashes (case-insensitive) are skipped and reported, never overwritten.
- `SavedSearch.MaxSavedSearches` = 50; new `SavedSearch.MaxPinned` = 8.
- Count wording: "1 file", "N files", "200+ files" at `SearchQuery.DefaultLimit`, "No folders connected", "Search not understood", "Could not count".
- UI text plain; help text passes `HelpCatalog` limits (20/40/25 words) and banned words.
- Tests use generated temp folders only. Verification: `dotnet build DeskAI.sln -c Release --no-restore`, `dotnet test --solution DeskAI.sln -c Release --no-build --no-restore`, `dotnet format DeskAI.sln --no-restore --verify-no-changes`.
- Commit after each task, ending with the session's Co-Authored-By / Claude-Session lines.

---

### Task 1: Pinned state on saved searches (schema 13)

**Files:**
- Modify: `src/DeskAI.Core/Search/SavedSearch.cs` (add `IsPinned`, `MaxPinned`, `WithPinned`)
- Modify: `src/DeskAI.Core/Abstractions/ISavedSearchRepository.cs` (add `SetPinnedAsync`)
- Modify: `src/DeskAI.Infrastructure/Persistence/SqliteSavedSearchRepository.cs`
- Modify: `src/DeskAI.Infrastructure/Persistence/SqliteDatabaseInitializer.cs` (version 13 migration)
- Test: `tests/DeskAI.Core.Tests/SavedSearchTests.cs`, `tests/DeskAI.Infrastructure.Tests/SqliteSavedSearchRepositoryTests.cs`, `tests/DeskAI.Infrastructure.Tests/SqliteDatabaseInitializerTests.cs`

**Interfaces:**
- Produces: `SavedSearch.Create(Guid id, string name, string phrase, DateTimeOffset createdAtUtc, bool isPinned = false)`; `bool SavedSearch.IsPinned`; `const int SavedSearch.MaxPinned = 8`; `Task ISavedSearchRepository.SetPinnedAsync(Guid id, bool isPinned, CancellationToken ct = default)`.

- [ ] Tests first: a new search is unpinned; `Create(..., isPinned: true)` round-trips through the repository; `SetPinnedAsync` pins and unpins; `SaveAsync` updating name/phrase keeps the pin; a version-12 database (create table without column, insert a row) upgrades to 13 with that row unpinned; fresh schema reports 13.
- [ ] Run, see failures (compile errors count).
- [ ] Implement: `ALTER TABLE saved_searches ADD COLUMN is_pinned INTEGER NOT NULL DEFAULT 0` guarded by a `pragma_table_info` check (ALTER has no IF NOT EXISTS), recorded as version 13; `CurrentSchemaVersion = 13`; SELECT reads column 4; INSERT writes it on insert only, and the ON CONFLICT update leaves `is_pinned` alone; `SetPinnedAsync` is one parameterized UPDATE.
- [ ] Build + focused tests pass; full suite; commit `feat(workspace): saved searches can be pinned (schema 13)`.

### Task 2: Starter pack catalog

**Files:**
- Create: `src/DeskAI.Core/Workspace/StarterPack.cs`, `src/DeskAI.Core/Workspace/StarterPackCatalog.cs`
- Test: `tests/DeskAI.Core.Tests/StarterPackCatalogTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public sealed record StarterPackSearch(string Name, string Phrase);
  public sealed record StarterPackRule(string Name, IReadOnlyList<RuleCondition> Conditions, string Destination)
  { public AutomationRule ToRule(Guid id); } // AutomationRule.Create(id, Name, Conditions, new MoveToFolderAction(Destination), isEnabled: false)
  public sealed record StarterPack(string Id, string Name, string Summary,
      IReadOnlyList<StarterPackSearch> Searches, IReadOnlyList<StarterPackRule> Rules);
  public static class StarterPackCatalog
  { public static IReadOnlyList<StarterPack> All { get; } public static StarterPack? Find(string? id); }
  ```
- IDs: `student`, `developer`, `gaming`, `productivity`, `minimal`. Contents exactly as the spec's table.

- [ ] Tests: five packs with those IDs in that order; unique IDs; every search name ≤ `SavedSearch.MaxNameLength` and every phrase creates a valid `SavedSearch`; every phrase is understood and yields its claimed chips (table below); every rule builds via `ToRule` and is off; rule names unique within a pack; Minimal has no rules; names and summaries contain none of `HelpCatalog.BannedWords`-style technical words (repeat the list locally, Core cannot see Presentation).
  - Chip expectations: `slides`→Category Presentations; `documents from last month`→Category Documents + ChangedAfter; `screenshots`→Category Screenshots; `archives`→Category Archives; `installers`→Category Installers; `big files`→MinimumSize only (no Text); `videos`→Category Videos; `big videos`→Category Videos + MinimumSize; `documents`→Category Documents; `spreadsheets`→Category Spreadsheets; `presentations`→Category Presentations.
- [ ] Implement; tests pass; commit `feat(workspace): the five starter packs as a fixed, tested catalog`.

### Task 3: StarterPackService — preview and add

**Files:**
- Create: `src/DeskAI.Core/Workspace/StarterPackService.cs` (service + result records)
- Test: `tests/DeskAI.Core.Tests/StarterPackServiceTests.cs` (in-memory fakes for both repositories and a fixed clock)

**Interfaces:**
- Consumes: Task 1 and 2 types; `IRuleRepository`; `IClock`.
- Produces:
  ```csharp
  public enum StarterPackItemKind { Search, Rule }
  public sealed record StarterPackItem(StarterPackItemKind Kind, string Name, string Description, string? SkipReason)
  { public bool WillBeAdded => SkipReason is null; }
  public sealed record StarterPackPreview(StarterPack Pack, IReadOnlyList<StarterPackItem> Items)
  { public bool AddsAnything => Items.Any(i => i.WillBeAdded); }
  public sealed record StarterPackOutcome(StarterPack Pack,
      IReadOnlyList<string> AddedSearches, IReadOnlyList<string> AddedRules,
      IReadOnlyList<StarterPackItem> Skipped, IReadOnlyList<string> Pinned,
      IReadOnlyList<string> AddedButNotPinned, string? StoppedBecause);
  public sealed class StarterPackService(ISavedSearchRepository searches, IRuleRepository rules, IClock clock)
  { Task<StarterPackPreview> PreviewAsync(string packId, CancellationToken ct = default);
    Task<StarterPackOutcome> AddAsync(string packId, CancellationToken ct = default); }
  ```
- Description for a search is its phrase; for a rule `AutomationRule.Describe()`. Skip reasons: "You already have a search called X.", "You already have a rule called X.", "You already have 50 saved searches."
- Unknown pack ID throws `ArgumentException`.

- [ ] Tests: preview saves nothing; preview skips clashing search and rule (case-insensitive); preview skips searches past 50; add saves searches and rules, all rules off; add re-reads state (item created after preview is skipped, not overwritten); adding twice adds nothing second time; pins added searches while fewer than 8 are pinned and reports the rest as added-but-not-pinned; a repository failure mid-way returns what was added and `StoppedBecause`; constructor reach test (forbidden types absent).
- [ ] Implement; pass; commit `feat(workspace): preview and add a starter pack, rules always off`.

### Task 4: PinnedSearchService — pin and count

**Files:**
- Create: `src/DeskAI.Core/Workspace/PinnedSearchService.cs`
- Test: `tests/DeskAI.Core.Tests/PinnedSearchServiceTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public enum PinnedCountKind { Counted, AtLimit, NoFolders, NotUnderstood }
  public sealed record PinnedCount(PinnedCountKind Kind, int Files);
  public sealed class PinnedSearchService(ISavedSearchRepository searches, FileSearchService search, IClock clock)
  { Task<IReadOnlyList<SavedSearch>> ListPinnedAsync(CancellationToken ct = default);
    Task<bool> PinAsync(Guid id, CancellationToken ct = default);   // false when 8 already pinned or id unknown
    Task UnpinAsync(Guid id, CancellationToken ct = default);
    Task<PinnedCount> CountAsync(SavedSearch saved, CancellationToken ct = default); }
  ```
- `CountAsync`: `NotUnderstood` when `UnderstoodNothing`; `NoFolders` when `FoldersSearched == 0`; `AtLimit` when `ReachedLimit`; else `Counted`.
- [ ] Tests use the real `FileSearchService` over the existing Core test fakes pattern (fake `IAuthorizedRootRepository` + fake `IFileIndex`, as in `FileSearchServiceTests`): each kind; pin cap; unknown id; listing only pinned. Constructor reach test.
- [ ] Implement; pass; commit `feat(workspace): pin saved searches and count them honestly`.

### Task 5: Open in Search

**Files:**
- Create: `src/DeskAI.Presentation/ViewModels/SearchRequest.cs`
- Modify: `src/DeskAI.Presentation/ViewModels/SearchViewModel.cs` (take the request in `InitializeAsync`)
- Modify: `src/DeskAI.Presentation/Composition/DeskAiApplicationServices.cs` (register `SearchRequest`, `StarterPackService`, `PinnedSearchService`)
- Test: `tests/DeskAI.Presentation.Tests/WorkspacePageTests.cs` (first tests)

**Interfaces:**
- Produces: `SearchRequest.Ask(Guid savedSearchId)`, `Guid? SearchRequest.Take()`.
- [ ] Page tests: asking then opening Search runs that saved search (results, chips); the request is used once; a deleted saved search opens Search with "That saved search no longer exists".
- [ ] Implement; pass; commit `feat(workspace): Search can open on a saved search it was handed`.

### Task 6: WorkspaceViewModel

**Files:**
- Create: `src/DeskAI.Presentation/ViewModels/WorkspaceViewModel.cs`
- Modify: `DeskAiApplicationServices.cs` (register `WorkspaceViewModel` transient)
- Test: `tests/DeskAI.Presentation.Tests/WorkspacePageTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public sealed record PinnedSearchTileViewModel(Guid Id, string Name, string Count);
  public sealed record UnpinnedSearchViewModel(Guid Id, string Name, string Phrase);
  public sealed partial class StarterPackCardViewModel : ObservableObject
  { string Id; string Name; string Summary; string Result; bool HasResult; }
  public sealed class WorkspaceViewModel : ObservableObject
  { ObservableCollection<PinnedSearchTileViewModel> Pins; ObservableCollection<UnpinnedSearchViewModel> OtherSearches;
    IReadOnlyList<StarterPackCardViewModel> Packs; bool HasPins; bool HasOtherSearches; string PinsCaption; string PinMessage;
    bool CanPinMore; Task InitializeAsync(); Task<StarterPackPreview> PreviewPackAsync(string packId);
    Task AddPackAsync(string packId); AsyncRelayCommand<Guid> PinCommand; AsyncRelayCommand<Guid> UnpinCommand;
    void OpenInSearch(Guid savedSearchId); }
  ```
- Result line: "Added {n} search(es) and {m} rule(s)." + " Skipped {k} you already had: A, B." + " {X} was added but not pinned — you have 8 pins." + " The rules are switched off — turn them on in Automatic tasks." (only when rules added) ; when nothing added: "Nothing added — you already have everything in this pack."; stopped: "Added … before DeskAI stopped safely: {reason}".
- [ ] Page tests (generated folders): packs listed; preview lists items and saves nothing; Add shows result under that card and pins; clash skipped and named; pin counts "1 file" in a folder, "No folders connected" with none, "Search not understood" for a nonsense phrase saved via repository; pin cap message; unpin moves it to other searches; after adding Productivity pack the "Invoices" rule is Off on Automatic tasks, Check now finds nothing, Tidy shows no "Invoices" group, and after turning the rule on Tidy shows it; tiles contain no file name.
- [ ] Implement; pass; commit `feat(workspace): the My workspace page's logic, tested the way a person uses it`.

### Task 7: The WinUI page, navigation, and help

**Files:**
- Create: `src/DeskAI.App/Views/WorkspacePage.xaml`, `WorkspacePage.xaml.cs`
- Modify: `src/DeskAI.App/Navigation/NavigationService.cs` (`["workspace"]`), `src/DeskAI.App/MainWindow.xaml` (menu item after Automatic tasks, glyph `&#xE8A1;`), `src/DeskAI.App/MainWindow.xaml.cs` (make `GoTo` internal), `src/DeskAI.App/App.xaml.cs` (`AddTransient<WorkspacePage>()`)
- Modify: `src/DeskAI.Presentation/Help/HelpCatalog.cs` (`workspace.page`, `workspace.pins`, `workspace.packs`)
- Test: existing `HelpCatalogTests`, `HelpPlacementTests`

- [ ] Add help topics and place them; run help tests.
- [ ] Page XAML following `SearchPage.xaml` styles; pack preview `ContentDialog` in code-behind (Add = primary, accent; Close = Cancel; `DefaultButton = Close`); Open in Search calls `ViewModel.OpenInSearch(id)` then `((MainWindow)app.MainAppWindow).GoTo("search", fresh: true)`.
- [ ] Full Release build (XAML compiles), full tests, format; commit `feat(workspace): My workspace page in the side menu`.

### Task 8: Documentation

**Files:** `docs/decisions/0026-profiles-as-starter-packs.md` (new), `docs/ARCHITECTURE.md`, `docs/UI-UX.md`, `docs/ROADMAP.md`, `docs/TESTING.md` (coverage map rows), `docs/MANUAL-TESTING.md` (new section), `docs/INTERVIEW-NOTES.md` (learning log), `docs/HANDOFF.md`, `README.md` status line.

- [ ] Write/update each; full verification again; commit `docs(v0.7): My workspace documented; handoff updated`.
