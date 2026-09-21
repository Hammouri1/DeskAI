# ADR 0038: User-Controlled Visual Search Direction

- Status: Implemented as bounded, per-search visual inspection (2026-09-21)
- Date: 2026-09-21

## Context

The owner wants to find a forgotten file by describing it in ordinary English, including
an image embedded in a PDF or PowerPoint slide. The current Search can match some metadata
and local text, but it cannot understand images or read PowerPoint slide text. A connected
folder and a cloud AI key are not permission to disclose its pictures.

## Decision

The owner refined the choice during implementation on 2026-09-21. If a compatible local AI
is connected, Search can ask it to inspect permitted images on this computer. If no local AI
is connected, Search may offer to send selected images to the chosen cloud AI only after a
per-search disclosure of the provider, images, count, size, and cost implications, and an
explicit Send choice. A saved cloud key, image reading permission, or earlier Send never
authorizes another image upload. Declining leaves images closed for that search. No model
download is built into DeskAI. Search still enters only explicitly connected roots and
their bounded subfolders.

## Implementation

The visual-reading permission is a fresh Search dialog for each attempt, not a persisted
folder grant. Only after **Read pictures** does DeskAI open up to 30 indexed image-bearing
files and hold up to 12 encoded pictures / 4 MB in memory. JPEG, PNG, and WebP files,
images referenced by the first 40 modern PowerPoint slides, and extractable images on the
first 20 PDF pages are eligible. The local PDF worker sees bytes, no path. A picture in a
PowerPoint or PDF result names its slide or page. Other formats, rasterized whole pages,
unsupported PDF image encodings, and anything beyond the bounds are not promised.

For a configured local AI, the selected images go only to its loopback endpoint. For a
configured cloud provider, a second dialog lists the exact image batch, provider, size,
and cost warning. Cancel sends nothing. Each batch can be used once and expires after two
minutes; changed AI choice or disconnected root refuses before transport. No file name or
path is in the image request. A text-only model may reject images; DeskAI says so and does
not switch providers automatically. No vision model is downloaded or image index stored.
The text, PDF, and slide grants remain independent. See the visual security review.
