# Handoff

This file exists so a brand-new chat can pick DeskAI up cold. The owner clears the
conversation after each roadmap version to keep it cheap, so nothing important may live only
in a chat. If it matters after this version, it is written down here or in `docs/`.

**If you are a new session: read this file first, then `AGENTS.md`, then the documents it
names. Do not start a milestone unasked — ask the owner which of "What is left" comes next.**

Keep this file short. It records state and open threads, not history: finished work belongs in
`ROADMAP.md`, decisions in `docs/decisions/`, reviews in `docs/security/`, and manual checks in
`docs/MANUAL-TESTING.md`. Rewrite the sections below at the end of every version rather than
appending to them.

---

## Where things stand

- Updated: 2026-09-17, at commit `6474767` on `main`, tree clean. **`main` is pushed to GitHub**
  up to `8fb83d9`; `6474767` is newer and **not pushed yet**.
- **DeskAI is on GitHub, privately: https://github.com/Hammouri1/DeskAI.** Only `main` was
  pushed. No tag exists, so the release workflow has still never run and there is no zip on the
  Releases page. **Push only `main`. Never `git push --all` or `--mirror`** — four local refs
  still carry the owner's real email (see "What is left").
- V1.1 remains complete. This session added one fix on top of it, from the owner's third
  hand-found bug: `6474767` **"DeskAI can say whether your AI answers, and why a save was
  refused"**.
- **What that bug was, because it is the pattern worth remembering.** The owner entered their
  OpenRouter key, then a local AI, and neither ever said it was connected. Nothing was broken in
  the sense of a crash. DeskAI simply had **no connection check at all**: "Save AI choice" wrote
  the choice down and then claimed the service was "ready" — a claim nothing had ever tested —
  and a refused save appeared only as one line of small grey text under a button far down a long
  page. Worse, an empty **Model name** box (which shows a grey example, easily read as a filled-in
  value) produced .NET's own words: *"The value cannot be an empty string or composed entirely of
  whitespace. (Parameter 'modelId')"*. Three bugs now found by hand in three sittings, none
  visible to the test suite, and all three were about **what the app says**, not what it does.
- What was built for it: `IAiConnectionCheck` (Core) with `ConfiguredAiConnectionCheck` (AI),
  a **"Check this now"** button beside Save, a result card with a tick or a cross, plain-words
  refusals from `ProviderEndpointPolicy`, `PlainMessage` in `SettingsViewModel` to strip .NET's
  "(Parameter 'x')" tail, and a dialog on a refused save. `ServiceReply` was extracted out of
  `CloudChatCompletionsSuggestionProvider` so both paths strip the key out of a service's own
  error text. The check carries **no file information at all**, whatever the sharing choices
  allow, and reserves one request from the daily cap before sending. See `docs/AI-PROVIDERS.md`
  ("Checking the connection") and `docs/UI-UX.md`.
- Verification at `6474767`, the owner's DeskAI closed: Release build of `DeskAI.sln`, **no
  errors and no warnings**; **1372 tests pass, none skipped** (was 1355; +12 `SettingsCheckPageTests`,
  +5 `AiConnectionCheckTests`); `dotnet format` clean.
- **The owner set their OpenRouter key up again and confirmed it works (2026-09-17).** This is
  the first time any part of DeskAI has been verified against a live paid service rather than a
  fake transport. Their words were "the key works fine"; they did not quote the check's own
  sentence back, so treat the green path as confirmed and the failure wordings as still only
  test-verified. The paragraph below describes the state before they re-entered it.
- **The owner's AI setup was empty on their machine before that.** Verified with `cmdkey /list`:
  there is no `DeskAI/...` credential, and the page's dropdown reads "Don't use AI". The database
  still holds OpenRouter usage counters from 9, 10 and 16 September, so it did work once. They
  will have to enter the key again; the new check is what tells them whether it took.
- A shareable build exists at **`artifacts/DeskAI-1.0.0-win-x64.zip`** (89 MB zipped,
  self-contained), built before this session's fix, so it does **not** contain "Check this now".
  `artifacts/` is gitignored. It reports version 1.0.0 because `Directory.Build.props` does.
- **The owner has still not walked V0.7–V1.1 by hand.**

## What is left

1. **Push `main`.** `6474767` and `82ee85f` are local only. Push `main` alone.
2. **Look at the GitHub Actions result.** Two pushes have happened and **nobody has checked
   whether the build workflow passed**. If it failed, the likeliest causes are the locked restore
   (`--locked-mode`) disagreeing with the committed `packages.lock.json` files, or the .NET SDK
   pin in `global.json` being unavailable on `windows-latest`.
3. **The owner's look at V1.1, then V0.7–V1.0**, in `docs/MANUAL-TESTING.md`: "Your folders"
   (inside "Desktop and wallpaper", steps 9–14), "Let AI read this", "Plan this folder with AI",
   "Ask DeskAI"; then "The command-center look", "Back up, restore, and Start fresh", "A release
   zip", "Tidy while I'm away", "My workspace", "Folder templates", "DeskAI's look", "V0.6
   sign-off", and "Checking after the window is closed" steps 2, 9, 19–24. Any bug found by hand
   gets a page test that fails before the fix.
4. **Delete the four local refs holding the old email**, once the owner has browsed the
   repository and is happy: `backup/before-email-rewrite`, and
   `refs/original/refs/heads/{main,v0.5-background-checking,v0.7-workspace-profiles}`.
   Until then, the push rule above stands.
5. **Decide the version number, then tag.** `Directory.Build.props` still says 1.0.0 and
   `RELEASE-NOTES.md` has a "1.1 — not tagged yet" section. Either tag `v1.0.0` at the V1.0
   commit and `v1.1.0` at the head, or bump to 1.1.0 and tag once. Tagging is what makes the
   release zip and the SBOM. Then unzip on a clean account and walk "A release zip".
6. **Two gaps found in the 2026-09-17 audit, neither a hole in shipping code, both holes in what
   would catch a future mistake:**
   - `TidyAiServiceTests.Constructor_CannotReachAnythingThatReadsOrChangesAFile` is a
     **denylist** of eight forbidden types. A ninth dangerous interface added later would pass.
   - **No test enforces the project-reference rules.** `DeskAI.AI` referencing `Core` alone —
     the strongest containment guarantee in the product — is true today but nothing fails if
     someone adds `DeskAI.Infrastructure` to that `.csproj`. A test that reads the `.csproj`
     files would fix both.
7. **Possible follow-ups the owner may ask for** (not started): size and date boxes on the rule
   form; more question kinds for Ask DeskAI (duplicates, old files); a narrow-window layout for
   the Your folders rows on Home; the ask-once behaviour on "Let AI read this" (that one still
   asks every time on purpose).
8. **Still deferred by decision, not to start unasked:** code signing when a certificate exists
   (ADR 0030), add-ons, localization, shortcut and icon suggestions, desktop layout previews,
   local image generation, and "keep both" unattended.

## Decisions made in conversation, not yet recorded elsewhere

- **Reading inside PDFs and Office files was designed, then parked by the owner (2026-09-17).**
  Do not start it. It came up because the owner finds today's AI thin — "Plan this folder with AI"
  sends types, sizes, dates and names and never looks inside a file, so it is guessing from
  `invoice_2024.pdf`. They asked for real content-aware planning, a **chat screen**, a much more
  flexible AI search ("find the PDF that has a data structures chapter", "the PDF with a picture
  of a flower"), and guardrails so nobody can use the chat as a general assistant. The design got
  as far as an agreed shape before they said "forget this idea for now". What was settled, worth
  keeping if it is ever picked up:
  - It is **five stacked pieces**, not one: (1) read inside PDF/Word/PowerPoint, (2) planning that
    reads contents, (3) search that understands contents, (4) the chat screen, (5) seeing pictures
    inside files. Nothing above (1) can work first, because `PlainTextExtractor` refuses PDF and
    Office files before opening them, on purpose.
  - Formats agreed: **`.pdf`, `.docx`, `.pptx`**. Excel excluded — a spreadsheet's meaning is in
    numbers and layout, and extracted cell text makes search worse while being much bigger. Old
    binary `.doc`/`.ppt` refused outright rather than half-supported.
  - `.docx`/`.pptx` need **no new dependency** (zip + XML, guard against zip bombs and external
    XML entities). PDF needs one: **PdfPig**, Apache-2.0, pure managed, executes nothing inside
    the file. The owner was told it is theirs to approve under `AGENTS.md`; they did not get to
    a yes before parking it.
  - Agreed shape: **one doorway** — lift the permission and path checks out of
    `PlainTextExtractor` so every format passes the same guard and format code never decides
    whether it is allowed; keep **a bounded prefix, not the whole document**; parse **in-process
    behind one small boundary** with size, page, time, and encrypted-file limits (a separate
    short-lived process was considered and judged too much machinery for a first slice, but the
    boundary is there so it stays swappable); and **reading is not sending** — extraction and
    sending contents to a service are separate slices with separate switches.
  - **The owner decided DeskAI may remember the extracted words in its own database**, for fast
    search, **as long as it tells them before it starts**, with a way to wipe it
    ("Forget what I read inside my files"), separate from Start fresh.
  - **The owner decided permission should be asked in the conversation itself**: the chat asks
    "May I read inside the files in your Desktop?" and their yes there grants it, per folder —
    granting the existing read-inside capability rather than inventing a second one behind it.
- **The repository is private, on purpose, and publishing is not the same as pushing.** The owner
  pushed so they could show a friend, not to release. Do not tag, publish, or suggest making it
  public as though the decision were made. The agreed order was: fix the README, scrub the email,
  push privately, then let a person actually use the app before anything is announced.
- **No iOS, and no cross-platform work now.** Asked directly on 2026-09-17 and answered: iOS has
  no filesystem for DeskAI to organize and no problem for it to solve, since the sandbox already
  guarantees what DeskAI's safety model exists to prove. Recorded because the code is more
  portable than it looks — Core, Safety, AI, and Presentation all target plain `net10.0` with
  zero native calls, and only four files in the repository use `DllImport`
  (`SingleInstance.cs`, `TrayInterop.cs`, `WindowsDesktop.cs`, `WindowsCredentialVault.cs`).
  The expensive part of any port is not the UI but re-earning the guarantees: case sensitivity,
  links versus reparse points, known folders, credential storage, and **all 20 security reviews
  would need revisiting**. If macOS or Linux is ever reprioritized it is a roadmap change with
  its own ADR, not a quiet start. `AGENTS.md` already defers cross-platform beyond V1.
- **The owner is comfortable with the AI-assisted development being public.** `CLAUDE.md`,
  `AGENTS.md`, `docs/superpowers/`, and the `Co-Authored-By: Claude` trailer on the commits all
  went to GitHub, and the README says so in plain words rather than leaving it to be inferred.
- **A trap worth remembering:** `dotnet publish -r win-x64` silently adds empty
  `net10.0/win-x64` sections to five `packages.lock.json` files. Committing those changes what
  CI's `--locked-mode` restore expects. **Revert the lock files after any runtime-specific
  publish.**
- **A lesson from three hand-found bugs (tray wording, README, this one):** all three were about
  what the app *says*, and the suite could not see any of them. When a person reports that
  something "doesn't work", check what the screen actually told them before looking for a fault
  in the code — twice now the code was right and the words were wrong.

## What a new chat must know

- `AGENTS.md` is the main instruction file; `CLAUDE.md` adds the workflow. If documents
  conflict, `docs/SECURITY.md` wins.
- The permanent rule: AI decides what it recommends, deterministic code decides what is
  allowed to happen. `FolderTidyExecutor` is the only code that moves a file or makes or removes
  a folder. An automatic check holds no executor; the coordinator holds `IAwayTidyRunner`, which
  `AwayTidyService` alone implements, and that is the one unattended path (25 rule-placed files
  per run, stop on anything unexpected). The wallpaper setter is held only by `WallpaperService`.
  Reflection tests fail if any of that changes.
- Verified in code on 2026-09-17, not merely claimed in documents: **there is no `File.Delete`
  anywhere in the source**, no `Process.Start`, no registry write, and no telemetry. The only
  filesystem mutations are in `FileOperationRunner.cs` — `File.Move(..., overwrite: false)`,
  `Directory.CreateDirectory`, and `Directory.Delete(recursive: false)`. Path safety is layered:
  policy check, `GetFullPath`, containment, per-segment reparse-point refusal, `VerifyAsync`
  before each operation, and a per-file size and last-written match before each move.
- **The AI connection (`IOrganizationSuggestionProvider`) has two calls:** `SuggestAsync` for
  files (Ask AI, and Plan this folder with `AiSuggestionTask.PlanFolder`) and `ReadSentenceAsync`
  for a typed sentence (Search, rules, Ask DeskAI). Every adapter implements both. Three
  services talk to it and each has a reflection test fixing what it may hold: `TidyAiService`,
  `SentenceAiService`, `AskDeskAiService`. AI output is read strictly
  (`StructuredSuggestionParser`, `AiSentenceReading`) and refused whole on anything off-shape;
  a planned folder name passes `FolderNameCheck` in the parser, the service, the path policy,
  and the executor. **`IAiConnectionCheck` is a third, separate contract** (2026-09-17): one
  fixed greeting, no file information at all, used only by "Check this now" on Privacy and AI.
- **Only the person's own four folders can be connected** (`PersonalFolderPolicy`, ADR 0032),
  checked at connection and again before tidying, by path segment and case-insensitively, and it
  fails closed when Windows reports none of the four. In page tests the sandbox's folders root
  stands in for Documents, so every generated folder counts as inside a personal folder, and
  every test Desktop contains a protected "program folder", as the owner's does.
- A folder that *contains* a protected place connects with that part skipped; a folder *inside*
  one is refused (`WindowsPathPolicy.ValidateRoot` returns Warning versus Blocked).
- Every "nothing moves by itself" sentence in the app reads `AwayTidyService.CountActiveAsync`
  through `AwayTidyWords`; do not hard-code that promise anywhere again.
- Every sentence about the icon near the clock appends `BackgroundCheckingChoice.WhereToLook`,
  so a new one cannot be written that sends someone to the wrong place (ADR 0025, amended).
- Anything a person can see or do needs a page test in `DeskAI.Presentation.Tests` and a row in
  the Feature Coverage Map in `docs/TESTING.md`. Tests read the XAML for layout rules
  (`ShellLayoutTests`, `AccessibilityNameTests`, `NoPlaceholderUiTests`, `HelpPlacementTests`);
  every help topic has three parts with word limits (`HelpCatalogTests`).
- Tests and development never touch real personal folders, the real wallpaper, or the real
  Desktop. `TestApp` replaces the credential vault, the internet, notifications, the tray, the
  window painter, the wallpaper setter, and the four known folders.
- The app is unpackaged WinUI 3, self-contained Windows App SDK, x64,
  `net10.0-windows10.0.26100.0`; libraries target `net10.0`. SQLite schema version is **14**.
- **Check warnings with `--no-incremental`.** An incremental build hides analyzer warnings in
  projects it does not recompile.
- **Do not run two builds at once.** A second build while tests are running produces MSB3061
  file-lock warnings that look like real failures and are not.
- Verification:

  ```powershell
  dotnet build DeskAI.sln -c Release --no-restore
  dotnet test DeskAI.sln -c Release --no-build --no-restore
  dotnet format DeskAI.sln --no-restore --verify-no-changes
  ```

  While the owner's own `DeskAI.App.exe` is open the App's copy step fails with locked DLLs.
  Compile into a scratch folder then
  (`dotnet build src/DeskAI.App -c Release -p:OutDir=<somewhere else>`) and ask them to close it
  for the real build. **Do not run the whole solution's tests from one shared `OutDir`** — the
  test projects collide and 313 Presentation tests fail with a bogus WinRT error. Build and run
  each test project normally instead.
- Launchable app after a Release build:
  `src\DeskAI.App\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64\DeskAI.App.exe`

## Known limits worth repeating

- Nothing moves a file without a preview and an approval, except an unattended run under a
  standing yes that a person gave on Organize for that folder, bounded as above and undoable.
- AI never sees locations, folder names, file contents, or DeskAI's file IDs — at most type,
  size and date, and name, each only if allowed, and only after a dialog showing the request. A
  sentence or question carries the typed words and today's date and nothing else. A connection
  check carries a fixed greeting and nothing else at all. AI plays no
  part in templates, looks, wallpaper, backups, or away runs.
- AI never writes anything shown as fact: a category, a checked folder name, or a sentence in
  DeskAI's vocabulary is all it can produce, and every reply on screen is DeskAI's wording.
- PDF and Office files are refused before opening; content reading is plain text only, 64 KB.
- No permanent deletion anywhere. Template undo removes only empty folders DeskAI made.
- DeskAI never registers itself with Windows startup and never checks online for updates.
- Put back restores the wallpaper picture only. Tidying the Desktop leaves shortcuts alone.
- The download is unsigned, so Windows shows "Windows protected your PC" until a certificate
  exists (ADR 0030). `docs/INSTALL.md` and the README both explain it.

---

## How to update this file

At the end of every roadmap version, before the owner clears the chat:

1. Rewrite "Where things stand", "What is left", and "Decisions made in conversation, not yet
   recorded elsewhere" to match reality — including the commit and the date.
2. Move anything finished out of here and into `ROADMAP.md`.
3. Write down every decision the owner made in conversation that is not yet in code, an ADR,
   or a design document. A decision that exists only in the cleared chat is lost.
4. Commit this file with the version's closing change.
