# Development Environment Baseline

Recorded on 2026-09-07 for milestone V0.1.

## Detected

- Windows build reported by .NET: `10.0.26200`, x64.
- .NET SDK: `10.0.400`; MSBuild bundled with the SDK: `18.9.6`.
- Installed runtimes: .NET 8, 9, and 10 families; Windows Desktop runtimes are present.
- Git: `2.52.0.windows.1`.
- Visual Studio Community 2026: `18.9.12105.275`.
- No standalone `nuget.exe`; package restore uses the NuGet client included with `dotnet`.
- No .NET workloads reported and no WinUI project template was installed.
- No standalone Windows SDK installation was found under Windows Kits, and Visual Studio did not report the Windows 11 SDK component.

## Selected Baseline

- Portable projects: `net10.0`.
- WinUI app: `net10.0-windows10.0.26100.0` with minimum Windows version `10.0.26100.0`.
- Windows App SDK: stable `2.4.0`.
- Architecture: x64 for V0.1.
- Packaging: unpackaged and Windows App SDK self-contained for development.
- Test platform: xUnit v3 on Microsoft Testing Platform selected through `global.json`.

## Verification and Limitation

The complete solution, including XAML compilation, builds successfully from `dotnet` because the pinned Windows App SDK package supplies its build assets. Automated tests also run from the CLI. Interactive launch and Visual Studio deployment were not manually verified in V0.1. Install the Visual Studio Windows application development workload and Windows 11 SDK 10.0.26100 or later before relying on the full IDE workflow.
