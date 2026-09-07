# ADR 0001: Target Framework and Foundation Packaging

- Status: Accepted
- Date: 2026-09-07

## Context

The machine has .NET SDK 10.0.400 and Visual Studio Community 2026, but no installed Windows SDK component or WinUI template. DeskAI needs a repeatable Windows-first foundation without changing the agreed WinUI stack.

## Decision

Target `net10.0` for portable libraries and tests. Target the app at `net10.0-windows10.0.26100.0`, with Windows build 26100 as the minimum supported baseline for now. Build x64 only during V0.1. Use the stable Windows App SDK 2.4.0 as an unpackaged, self-contained dependency for development.

## Alternatives

- .NET 8 was available at runtime but its SDK was not installed, and choosing it would add tooling work without a demonstrated compatibility benefit.
- A packaged MSIX app is appropriate for distribution later, but signing, identity, update, and installer behavior need a dedicated release decision.
- WPF would build from an installed template but violates the chosen WinUI 3 product direction.

## Consequences

The command-line solution builds now and portable tests remain simple. Output is larger because the Windows App SDK runtime is self-contained. Visual Studio interactive deployment may require adding the Windows application development workload and Windows 11 SDK. Packaging, ARM64, signing, and final minimum Windows support remain later decisions.
