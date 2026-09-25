# ADR 0047: Quick Search Opens Files and Keeps DeskAI Near the Clock

- Status: Accepted
- Date: 2026-09-25
- Amends: ADR 0025 (what closing the window means)
- Builds on: ADR 0036, 0037, 0039 (the reading-inside permissions), ADR 0041 (search bounds)
- Design: `docs/superpowers/specs/2026-09-25-quick-search-design.md`
- Review: `docs/security/2026-09-25-quick-search-review.md`

## Context

Quick search is a small bar that Ctrl + Alt + Space shows over any app. It finds files in
connected folders by name and by the words inside them, and Enter opens the chosen file. DeskAI
has never opened a file in another app or started a process, and until now closing its window
quit it unless background checking was on. The shortcut should also work after the window is
closed.

## Decision

1. Only the person's own press (Enter, a click, or the row's button) opens a file. It must be a
   file in a connected folder, and its type must be on this allow-list: `.txt .md .rtf .csv .pdf
   .docx .xlsx .pptx .odt .ods .odp .jpg .jpeg .png .gif .webp .bmp .heic .mp3 .wav .m4a .flac
   .mp4 .mov .mkv .avi .zip`. Only the last extension counts, so "invoice.pdf.exe" is a program.
   A name ending in a dot or a space is not on the list. Everything else, including programs,
   scripts, shortcuts, web pages, macro-enabled and older Office files, and files with no
   extension, only gets **Show in folder**.
2. Just before starting anything, the launcher checks the file as it is **now**, not as the
   index remembered it. The folder must still be connected and searchable. The path, once
   normalised, must stay inside that folder, and no folder or file on the way may be a link,
   junction, or reparse point. `IPathPolicy` must not protect the place. The file must exist
   and be a file. For Open, its current name must still be on the allow-list. The first check
   that fails stops the launch, and the bar shows the reason in plain words.
3. Open uses the Windows shell's "open" action on that one path, so the person's own choice of
   app opens it. Show in folder starts `%WINDIR%\explorer.exe /select,"<path>"`, found by its
   full path and never through PATH. Nothing else is ever started. Both go through the
   `IShellStarter` seam. The shared registration (used by the page tests) holds one that starts
   nothing, and only the app registers the real one.
4. The shortcut is `RegisterHotKey(Ctrl + Alt + Space, MOD_NOREPEAT)`. Windows reports only this
   one combination, and DeskAI sees nothing else anyone types. There is no keyboard hook.
5. This amends ADR 0025. While quick search is on and the icon near the clock is actually
   showing, closing the window keeps DeskAI near the clock. In that state DeskAI does no checking
   or tidying unless background checking was separately turned on. If the icon cannot be shown,
   closing quits as before, so DeskAI is never left running with no window and no way to stop it.
   The icon's menu gains **Find a file**, and **Pause checking** appears only while background
   checking is on. The "DeskAI is still running" notification stays tied to background checking.
   Quitting is from the icon's menu, and DeskAI still never adds itself to Windows startup.
6. The bar reads inside files only through `ContentSearchService.SearchAsync`, with the
   per-folder, Word and Excel, PDF, and slide permissions already given on Search and the same
   limits (50 files, 20 seconds). It never grants a permission, and never runs the scanned-PDF
   reader (ADR 0040) or picture reading (ADR 0038). Nothing typed, found, or read is stored or
   logged.
7. Quick search uses no AI. `IFileLauncher` is held only by `QuickSearchViewModel`, and a test
   asserts that no other type, AI or otherwise, takes it.

## Consequences

- Opening a file is new. It is the person's own file, in a folder they connected, opened by
  their own press, and only a familiar type. That is why the short gap between the last check
  and Windows opening the file is accepted: the file could still be swapped in that moment, and
  such a swap needs something already running as the person.
- Which app Windows uses for each type is the person's own Windows setting and out of scope.
- Words from inside files can show over other apps, but only after the person's own shortcut
  press. They are the same short snippets Search shows, and they disappear when the bar hides.
- DeskAI staying near the clock after the window closes is now normal while quick search is on.
  Every sentence that says what closing does must stay true. "Closing it stops everything"
  becomes "Closing it stops checking", and Start fresh's dialog says checking stops.
