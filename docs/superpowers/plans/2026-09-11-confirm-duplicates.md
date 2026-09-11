# Confirming Duplicates by Content (V0.4 step 6, stage 2)

**Goal:** Home's "possible copies" can be checked for real. A person presses **Check if they're
really copies**, reads exactly how many files DeskAI would read and how much, and only after
pressing **Compare** does DeskAI read those files and say, group by group, which are identical,
which only share a size, and which it could not check and why. Nothing is deleted, moved, saved,
or sent.

**Security review:** `docs/security/2026-09-11-duplicate-confirmation-review.md`, written first.

## Why the existing permission is not reused

The roadmap (V0.4 step 6) is explicit: the "read inside files" permission people agree to says
DeskAI reads only the beginning of plain text files. Comparing copies reads every byte of any
kind of file — photos, videos, PDFs, programs. Quietly reusing that yes would stretch it into
something nobody agreed to.

## Decisions

- **Asked every time, for exactly these files.** No lasting permission is stored. The same
  two-call shape as Ask AI (ADR 0020): `DuplicateCheckService.PrepareAsync` builds a
  `DuplicateCheckQuestion` — the files, their folders, the count and total size — and reads
  nothing; the page shows it in a dialog; `CompareAsync` reads only files in that question, after
  checking each folder is still connected. A stored permission would let a later check read
  without asking, and would be one more thing to find and take back.
- **Only what the size check already found.** Candidates come from the size groups Home shows,
  in connected folders search may look in. Nothing else can be put in a question.
- **Reads as little as it can.** First the first 64 KB of each file: files whose beginnings
  differ are different, and are never read further. Only files whose beginnings match another's
  are read to the end. Each file is fingerprinted with SHA-256; equal size and equal fingerprint
  is reported as identical.
- **Bounds, all named constants:** at most 200 files in one check; files larger than 2 GB are not
  read in full ("too large to compare"); at most 8 GB read in full per check, after which the
  rest are reported as not compared. A Stop button cancels between and during reads.
- **Checked right before reading each file:** still inside its connected folder, not protected,
  not a link, not online-only (reading would download it), still the size and last-changed time
  DeskAI remembered — otherwise skipped with a plain reason. Opened read-only with others
  allowed only to read, so a file being written fails as "open in another program" rather than
  being fingerprinted while it changes; its size and time are checked again after reading.
- **Nothing kept, nothing sent.** Fingerprints live in memory for the one check and are
  discarded. No AI is involved. The file fingerprinter is a Core contract implemented in
  Infrastructure and registered for this service only; the AI, tidy, and automatic-check services
  cannot reach it, and tests assert that.
- **Describes, never acts.** No delete, move, or "keep one" button. Removing copies, if ever
  wanted, would go through Tidy a folder's list, Tidy button, and Undo, in its own step.
- **The health score is unchanged in this step.** It keeps describing possible copies; using
  confirmed results in it is a separate decision.

## Tasks (one commit each)

1. Plan and security review (docs).
2. **Engine:** `IFileFingerprinter` (Core) and `FileFingerprinter` (Infrastructure);
   `DuplicateCandidate` gains folder ID, size, and last-changed time; `DuplicateCheckService` with
   `PrepareAsync` and `CompareAsync`. Negative tests for every row of the review.
3. **Page:** the button, the dialog, progress and Stop, results in the card, "?" help. Page tests.
4. **Documents:** ADR 0024, review result, SECURITY, ARCHITECTURE, UI-UX, TESTING,
   MANUAL-TESTING, ROADMAP, README, INTERVIEW-NOTES.
