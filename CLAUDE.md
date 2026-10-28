# ShortcutCoach

Windows tray app that suggests keyboard shortcuts for things the user clicks,
plus a hint mode for keyboard-clicking elements with no shortcut.
**Read SPEC.md before starting any milestone.** It has scope, architecture,
and acceptance criteria.

## Stack
- C# / .NET 10, WPF
- UI Automation via FlaUI (`FlaUI.UIA3` NuGet package)
- Win32 interop via P/Invoke (mouse hook, hotkeys)
- xUnit for tests
- Local JSON persistence (System.Text.Json)

## Layout
- `src/ShortcutCoach/` — WPF app; all Windows-specific code lives here
- `src/ShortcutCoach.Core/` — pure logic, no Windows APIs, must stay testable
- `tests/ShortcutCoach.Core.Tests/` — tests for Core

## Commands
- Build: `dotnet build`
- Test: `dotnet test`
- Run: `dotnet run --project src/ShortcutCoach`

## Workflow rules
1. Plan before coding. For each task, propose a short plan and wait for approval.
2. Small, focused changes. One concern per change. Don't touch unrelated files.
3. After every change: `dotnet build` must pass; `dotnet test` must pass if Core changed.
4. Any new logic in Core gets unit tests in the same change.
5. Suggest a commit message (Conventional Commits, e.g. `feat(hook): ...`)
   at the end of each completed task.
6. If something in SPEC.md looks wrong or ambiguous, ask — don't guess.

## About the owner
I know C well but I'm new to C#, .NET and WPF. When you introduce a C# feature
I probably haven't seen (delegates, events, async/await, LINQ, records,
dependency injection), add a one-line comparison to C. Keep explanations short.

## Technical gotchas (must follow)
- Store the low-level hook delegate in a field. A local delegate gets garbage
  collected and the app crashes.
- The hook callback must return immediately. Never call UI Automation inside it.
  Queue the click point to a background worker (e.g. `Channel<T>`) instead.
- UI Automation calls happen on a background thread; marshal to the UI thread
  (`Dispatcher`) only to update WPF controls.
- Overlay/toast windows must not steal focus (`WS_EX_NOACTIVATE`, topmost).
- App must be per-monitor DPI aware (app.manifest) so click coordinates match.
- Elevated windows can't be inspected: catch the exception and skip quietly.

## Privacy (non-negotiable)
- Never hook or record the keyboard except the single hint-mode hotkey.
- No network calls of any kind.
- Only store: process name, element name, control type, shortcut, counts, timestamps.
