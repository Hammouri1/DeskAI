# ADR 0029: The Wallpaper Picture Is the One Windows Setting DeskAI Changes

- Status: Accepted
- Date: 2026-09-16
- Review: `docs/security/2026-09-16-desktop-and-wallpaper-review.md`

## Context

V0.7 piece E promised desktop layouts, icons, shortcuts, and wallpaper, behind a `SECURITY.md`
gate because it changes Windows. On 2026-09-16 the owner chose what to build: the wallpaper,
with a way to put the old one back, and a shortcut that tidies the Desktop folder through the
ordinary Tidy flow. Shortcut and icon suggestions, layout previews, and image generation were
deferred beyond V0.7.

## Decision

- **DeskAI changes exactly one Windows setting: the wallpaper picture.** Through the documented
  `SystemParametersInfo` call, with Windows persisting it. DeskAI writes no registry value of its
  own (the existing source-scanning test forbids registry APIs).
- **Only from the button, only a picked file.** The path comes from the Windows file dialog,
  limited to picture extensions. DeskAI never lists folders for pictures, never opens the file,
  and never makes or downloads one. The service refuses network paths, URLs, relative paths,
  non-picture extensions, links, folders, empty files, and files over 50 MB — at preview and
  again at use.
- **The old wallpaper is written down before the change**, in its own settings row, so Put back
  works after a crash and after reopening. A plain-colour desktop is recorded as empty and
  restored as such. If Windows refuses, the record is rolled back so Put back is not offered
  for a change that never happened. If the person changes the wallpaper in Windows afterwards,
  the page says so beside Put back, and the next Use remembers their newer choice instead.
- **`IWallpaperSetter` has two calls and one holder.** `WallpaperService` takes it, the picture
  inspector, and the settings store, and nothing else; the automatic-check and presence
  reflection tests forbid the setter, so nothing that runs with no window can reach it.
- **"Tidy my Desktop" adds no reach.** The Desktop path comes from the known-folder API, the
  folder is connected exactly as the picker connects one (names, sizes, dates), and Organize
  asks its own permission before anything moves. Shortcuts are unknown types to the classifier
  and stay where they are.
- **No test touches the real wallpaper or Desktop.** `TestApp` replaces both, the way it replaces
  the credential vault, and asserts the test Desktop is inside its own temp folder.

## Alternatives

- Wallpaper from a connected folder or a DeskAI-chosen picture: rejected; a picture DeskAI
  found is a picture nobody picked.
- `UserProfilePersonalizationSettings`: needs a packaged app; DeskAI is unpackaged.
- A registry write for the wallpaper: rejected; DeskAI has no registry access and keeps it so.
- Shortcut and icon changes: deferred; each is a new executor command with its own review.

## Consequences

`docs/SECURITY.md` now names the wallpaper as the one Windows setting DeskAI can change. Any
second setting, any other source of pictures, any shortcut or icon change, or any path to these
without a window needs a new review.
