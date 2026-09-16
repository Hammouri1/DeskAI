# Security policy

DeskAI moves people's files. A flaw here can cost someone their work, so reports are taken
seriously and handled privately.

## Reporting a vulnerability

Please use **GitHub's private vulnerability reporting** on this repository ("Security" tab →
"Report a vulnerability"). That reaches the maintainer without a public issue.

If that is unavailable, open an issue titled "Security contact request" with **no details**, and
a private channel will be arranged.

Please do not post a working exploit or step-by-step extraction path in public, and please do
not include anyone's real files, paths, or keys in a report. Generated example files are enough.

## What to include

- What DeskAI did, what you expected, and the version shown at the bottom of Privacy and AI.
- The smallest set of generated files and steps that shows it.
- Whether a real file was moved, renamed, deleted, overwritten, read, or sent anywhere.

## What counts

Anything that lets DeskAI do more than the person allowed: a move outside a connected folder, a
move without the tidy permission or the preview, a delete or an overwrite, a file read without
the reading permission, data sent to an AI service beyond the sharing choices, a key exposed, a
run while nobody is watching beyond the ceiling in ADR 0031, or DeskAI starting with Windows.

## Response

You will get an acknowledgement within a few days and a fix or a clear answer as soon as one
exists. Fixes ship as a new release on the Releases page. Credit is given if you want it.

The design rules and the reviews behind each capability are in `docs/SECURITY.md` and
`docs/security/`.
