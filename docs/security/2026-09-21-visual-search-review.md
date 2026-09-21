# Visual Search Security Review — 2026-09-21

## Scope and decision

This review covers the separate **Find pictures with AI** Search action (ADR 0038).
The action does not tidy, move, rename, delete, download a model, or write a derived image
index. The owner chose a connected local AI when available and a fresh exact-image cloud
Send choice otherwise. Existing folder, text, PDF, slide, and cloud-key permissions are
not treated as image-reading or image-upload permission.

## Boundaries

1. The page asks **Read pictures** for the selected connected-folder scope before
   `VisualSearchService.PrepareAsync` may open anything. A false approval refuses before
   root or index access. The scanner's depth-4 / 2,000-entry cap still applies; the visual
   pass checks at most 30 eligible files for 30 seconds and holds at most 12 images / 4 MB.
2. Candidate paths are root-relative paths from the metadata index. The reader rechecks
   root and relative-path policy, containment, every path component for reparse points,
   and the final path of the opened Windows file handle. Files are opened read-only; missing,
   changed, protected, link, and unsupported items are skipped. One file is at most 8 MB;
   one encoded image at most 1 MB.
3. Modern PowerPoint parsing reads bounded slide XML and image relationships, accepts only
   embedded images referenced by a slide, and does not follow external relationships.
   PDF parsing runs in the existing fixed worker, which receives bytes on standard input,
   no path. It reads only the first 20 pages; damaged, encrypted, oversized, and unsupported
   image encodings are skipped. The worker has a five-second deadline. It runs under the
   signed-in user and is fault containment, **not** an operating-system sandbox.
4. A batch stays in process memory, can be used once, and expires after two minutes. Before
   transport the service checks the AI mode, provider, model, local endpoint, and connected
   roots again. A disconnected root or changed AI setting refuses the entire batch.
5. Local mode uses the validated loopback endpoint and no cloud fallback. Cloud mode requires
   a second dialog listing the exact selected images, provider destination, total size, and
   cost warning; Cancel supplies no cloud approval. The adapter uses the fixed provider
   catalog and the existing vault and daily request budget. It posts one data-URL image and
   the person's phrase per request, without a filename, path, index, or filesystem tool.
   The provider may charge for every picture checked. A text-only model may reject images;
   DeskAI stops rather than trying another service.
6. Model output is accepted only as a bounded JSON boolean and a short plain reason. The
   answer is displayed as AI evidence; no operation plan or executor receives it.

## Verification and limits

Generated-data page tests exercise a picture two subfolders down in a PowerPoint slide, a
PDF page image, separate read approval, no cloud request before Send, Cancel, changed AI
settings, local loopback, and removal of paths/names from the request. The fake transport
and in-memory key stand-ins keep tests away from personal folders, real keys, and networks.
The UI dialogs themselves need a manual visual check.

This is a bounded search, not complete visual indexing. Rasterized full pages, PDF vector
drawings, unsupported filters, older Office formats, animated formats, images beyond the
file/image/page/slide limits, and folders deeper than the metadata scanner's cap can be
missed. A vision model can misread a scene or pictured word. The app reports a partial
batch, tells the person to check the file, and makes no mutation from a match.
