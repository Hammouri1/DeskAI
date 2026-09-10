# ADR 0020: AI Suggestions for Your Own Folders

- Status: Accepted
- Date: 2026-09-10

## Context

V0.6 step 2b lets AI suggest where files in a person's own folder belong. Until now AI had
only seen six generated sample records. The V0.3 privacy review left a gate for this moment:
real-folder disclosure needs its own review, protected-item integration, a UI preview of the
exact real request, and more tests. The agreed design also said AI would be asked about files
DeskAI cannot place "if AI is set up", which reads as asking on its own when the list loads.

## Decision

- **Preview, then send.** `TidyAiService.PrepareAsync` builds the request and describes each
  file in words, built from the request's own fields. The page shows that in a dialog naming
  the service and its address. Only `AskAsync`, called after Send, hands the same request to
  the AI connection. Nothing is sent when the page opens. This amends the design, because a
  request sent on page load has no moment for a preview and spends the daily limit unasked.
- **Re-checked before sending.** `AskAsync` refuses, sending nothing, if the folder may no
  longer be tidied, or the AI choice, its destination, or the sharing choices have changed so
  that the shown request is no longer covered.
- **A narrow ceiling on what real folders can send.** `TidyAiService.RealFolderShareable` is
  file type, size and date, and name. The request uses the saved choices intersected with
  that. Full locations are never sent from a real folder even when the switch is on, and
  folder names are never sent.
- **Random stand-ins instead of file IDs.** Scanned IDs are a SHA-256 of the folder ID and the
  path, identical on every scan. Each request instead uses fresh random numbers mapped back
  locally, so a service cannot recognise the same file across requests.
- **Only what can still move, and only what rules do not place.** Left-alone files (downloading,
  recent, online only, hidden, system) are never candidates. Rule-placed files are never sent.
  Protected paths are checked again through `IPlanSafetyCheck.IsProtected` and dropped.
- **AI names a category, DeskAI names the folder.** The category goes through
  `TidyFolderRecipe`. The AI's reason text is not shown; the row says "AI idea from <service>".
  Ideas below `UnsureBelow` (0.7) are grouped as "AI isn't sure" and start unticked.
- **Order of authority.** Rules, then AI when the person asked about every file, then file type,
  then AI for files DeskAI cannot place. Advice expires when the file's size or last-changed
  time differs from when it was asked about.

## Alternatives

- Asking automatically when the list loads: rejected for the reasons above.
- Honouring the full-location switch for real folders: rejected. It adds nothing to judging a
  loose file and discloses the Windows user name and folder layout.
- Showing the AI's own reason: rejected. A crafted file name can make a model say anything, and
  DeskAI's screen should not become a channel for it.
- Sending scanned file IDs: rejected as a stable cross-request identifier.

## Consequences

A person can get AI help for files DeskAI does not know, or for every file, and always sees
what leaves the computer first. Every refusal case is a page or engine test with a recording
internet. AI still cannot move anything: the Tidy button stays off until step 3, and when it
arrives AI ideas go through the same plan, safety check, preview, and approval as any other
suggestion.
