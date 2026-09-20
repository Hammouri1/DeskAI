# ADR 0038: On-Device Visual Search Direction

- Status: Accepted product and privacy direction; implementation deferred
- Date: 2026-09-21

## Context

The owner wants to find a forgotten file by describing it in ordinary English, including
an image embedded in a PDF or PowerPoint slide. The current Search can match some metadata
and local text, but it cannot understand images or read PowerPoint slide text. A connected
folder and a cloud AI key are not permission to disclose its pictures.

## Decision

For Search, image understanding and OCR run on the person's computer. Image pixels,
image-derived OCR text, captions, and embeddings stay local and do not enter cloud-provider
requests. The existing optional cloud sentence interpreter may receive only the words the
person typed, under its existing consent flow. Search still enters only explicitly connected
roots and their bounded subfolders.

## Before Implementation

Design a separate visual-reading permission, local model and hardware fallback, bounded
processing of standalone and embedded images, protected-path and link checks, result
evidence such as page or slide number, and a storage and deletion policy for any derived
index. Review parser and model isolation, performance, and privacy before writing code.
Existing PDF and document grants do not expand to pictures automatically. Until then the
UI must continue to say visual search and OCR are unavailable.
