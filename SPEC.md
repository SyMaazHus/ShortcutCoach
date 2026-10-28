# ShortcutCoach — Product Spec

## Problem
People lose time reaching for the mouse for actions that have keyboard
shortcuts, and some actions have no shortcut at all. Existing tools either
only list shortcuts (cheat sheets) or only work in one app (e.g. Key Promoter X
for JetBrains IDEs).

## Solution
A lightweight Windows tray app that:
1. **Coaches**: watches mouse clicks system-wide, identifies what was clicked,
   and suggests the keyboard shortcut for it.
2. **Replaces**: for elements with no shortcut, offers a hint mode — a hotkey
   overlays letter labels on clickable elements; typing a label clicks it.

Goal: a user can go a full working day without touching the mouse.

## Non-goals (v1)
- macOS / Linux support
- Cloud sync or accounts
- Recording or analysing keystrokes

## Privacy (hard requirements)
- Never record keystrokes.
- Store only: app name, element name, shortcut, click counts.
- All data stays local (`%AppData%\ShortcutCoach`). No network calls.
- User can pause the app and wipe all data from the tray menu.

## Architecture

```
MouseHook ──click(x,y)──▶ ElementResolver ──element──▶ ShortcutResolver
 (Win32 WH_MOUSE_LL)        (UI Automation)               (AcceleratorKey,
                                                           then fallback DB)
                                                              │
                                                              ▼
 Notifier ◀──suggestion── SuggestionEngine ◀──────────── StatsStore
 (toast overlay)           (thresholds, cooldowns,         (local JSON)
                            "learned" detection)

 HintMode (global hotkey → overlay labels on clickable UIA elements)
 TrayApp  (pause, settings, stats, wipe data, exit)
```

### Projects
- `src/ShortcutCoach` — WPF app: hook, UIA, overlays, tray (Windows-only code)
- `src/ShortcutCoach.Core` — pure logic: ShortcutResolver fallback DB,
  SuggestionEngine, StatsStore models. No Windows APIs → fully unit-testable.
- `tests/ShortcutCoach.Core.Tests` — xUnit tests for Core

## Milestones & acceptance criteria

### M1 — Click logger
- [ ] Global mouse hook captures left clicks in any app
- [ ] Each click resolves to element name, control type, and process name
- [ ] Results shown live in a debug window
- [ ] App runs 30+ minutes without crashing or slowing the mouse
- **Done when:** clicking Bold in Word logs `Bold (Button) — WINWORD`

### M2 — Shortcut suggestions
- [ ] Reads `AcceleratorKey` from the clicked element
- [ ] Fallback JSON shortcut DB for common apps (Explorer, Chrome, VS Code, Word)
- [ ] Toast appears near the cursor, auto-dismisses, never steals focus
- **Done when:** clicking Copy in Notepad's menu shows "Copy → Ctrl+C"

### M3 — Smart coaching
- [ ] Suggest only after N clicks of the same element (default 3)
- [ ] Per-element cooldown so it never nags
- [ ] Detect when a shortcut is "learned" (no mouse clicks on it for X days)
- [ ] Stats persisted to local JSON; survives restart
- [ ] Unit tests for SuggestionEngine
- **Done when:** tests pass and a day of real use feels helpful, not annoying

### M4 — Hint mode
- [ ] Global hotkey (default `Ctrl+;`) shows labels on clickable elements of
      the foreground window
- [ ] Typing a label clicks the element; `Esc` cancels
- [ ] Appears in under 300 ms on typical windows
- **Done when:** can navigate File Explorer with no mouse

### M5 — Ship it
- [ ] Tray icon with pause / stats / settings / wipe data / exit
- [ ] Start with Windows (optional setting)
- [ ] README with GIF demo, architecture, privacy section, dev log
- [ ] GitHub release with installer or zip

## Known risks
- UIA data is patchy in some apps (Electron, games) → fallback DB, graceful skip
- Can't inspect elevated (admin) windows from a non-elevated app → skip silently
- Hook callback must return fast or Windows silently removes the hook →
  do UIA work off the hook thread
- Multi-monitor / DPI scaling can skew coordinates → make app per-monitor DPI aware
