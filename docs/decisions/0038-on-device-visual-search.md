# ADR 0038: User-Controlled Visual Search Direction

- Status: Accepted product and privacy direction; implementation deferred
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

## Before Implementation

Design a separate visual-reading permission, compatible-model checks, bounded processing
of standalone and embedded images, protected-path and link checks, result evidence such as
page or slide number, and a storage and deletion policy for any derived index. The cloud
request must be built from an exact, rechecked selection and never expose a filesystem
tool to the model. Review parser and model isolation, performance, and privacy before code.
Existing PDF and document grants do not expand to pictures automatically. Until then the
UI must continue to say visual search and OCR are unavailable.
