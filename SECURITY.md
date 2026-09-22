# Security policy

## Supported version

Security fixes are made for the latest DeskAI release. Install the newest version from
[GitHub Releases](https://github.com/Hammouri1/DeskAI/releases/latest) before reporting a
problem that may already have been corrected.

## Report a vulnerability privately

Please **do not open a public issue** for a suspected security or privacy vulnerability.
Use GitHub's private reporting form instead:

<https://github.com/Hammouri1/DeskAI/security/advisories/new>

Include the DeskAI version, Windows version, what you expected, what happened, and the smallest
safe reproduction you can provide. Do not attach personal documents, API keys, credential files,
database files, or screenshots containing private paths. A generated sample is preferred.

The project owner will acknowledge the report through the private advisory, investigate it, and
coordinate a fix and disclosure there. Please allow time for a safe release before publishing
technical details that could put users' files or credentials at risk.

## What counts

Report anything that lets DeskAI do more than the person allowed: moving outside a connected
folder, changing a file without the tidy permission and reviewed plan, deleting or overwriting,
reading a file without the relevant permission, sending more to AI than the sharing choices
allow, exposing a credential, exceeding an approved unattended-tidy boundary, or starting with
Windows.

Fixes are published as a new version on the Releases page. Reporter credit is included when the
reporter wants it and disclosure is safe.

## Product security boundaries

DeskAI's detailed trust model is documented in [docs/SECURITY.md](docs/SECURITY.md). Important
boundaries include explicit folder permission, deterministic validation before file changes,
no silent overwrite or permanent deletion, Windows-protected credential storage, and explicit
consent before supported cloud-AI requests.
