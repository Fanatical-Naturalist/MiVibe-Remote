# Offline Codex navigation regression

Run from the repository root:

```powershell
& .\tools\Test-CodexNavigation.ps1
```

The tool uses the pinned SDK at `.local/dotnet/dotnet.exe`. Optional arguments:
`-Configuration Debug` and `-DotNetPath <path-to-dotnet.exe>`.

This standalone console project source-links the production `TypelessShortcut.cs`.
Every navigation submission and cleanup uses a simulated sender. A DLL import
resolver rejects all native calls from the test assembly before they execute;
the harness never installs a hook, activates a window, or sends desktop input.
The project has no external package dependencies and disables NuGet sources.

Coverage includes both directions, the native x64 INPUT layout and exact four-event
Ctrl+PageUp/PageDown batch, every accepted prefix from 0 through 4, reverse-order
cleanup of only still-held keys, bounded retries, continuing to release modifiers
after another release fails, and cancellation before/during submission. The public
entry point is also checked to reject cancellation before calling native guards.

Foreground/package validation and physical modifier rejection remain outside the
sender seam and are not claimed as dynamically tested here. Successful offline
tests establish input construction and cleanup, not acceptance by the Codex UI.
