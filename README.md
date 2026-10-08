# BotPriston

Screen-reading farm bot for Priston Tale (private server that allows automation).

Hard constraints: the bot only **captures the game window** and **simulates keyboard/mouse**.
No process memory access, DLL injection, client hooks or packet manipulation.

## Layout

| Project | Purpose |
|---|---|
| `src/BotPriston.Core` | Platform-independent: config, geometry, vision, brain. Fully testable offline. |
| `src/BotPriston.Platform` | Win32/WinRT: window discovery, capture (Windows.Graphics.Capture + BitBlt), input. |
| `src/BotPriston.App` | Console entry point (`BotPriston.exe`). |
| `tests/BotPriston.Tests` | xUnit tests; vision tests run against screenshots in `samples/`. |

All coordinates are relative to the game's **client area** (expected 1600x900).
All tunables live in `botconfig.json`.

## Commands

Run from the repository root (so `./botconfig.json` and `./samples` are found):

```
dotnet run --project src/BotPriston.App -- windows
dotnet run --project src/BotPriston.App -- info
dotnet run --project src/BotPriston.App -- capture --label town
dotnet run --project src/BotPriston.App -- capture --hotkey
```

- `windows` — lists windows; use it to set `Window.ProcessName` / `Window.TitleContains`.
- `info` — window geometry, DPI, capture backend and timing.
- `capture` — saves lossless PNGs of the client area to `samples/`
  (`--delay`, `--count`, `--interval`, `--hotkey`, `--label`, `--window`).
- `debug` — window with the detections drawn over the live game (or `--source samples`).
- `detect` — runs the detectors over screenshots and prints a table (`--csv`, `--overlay <dir>`).
- `crop` — cuts a template out of a screenshot (`--source`, `--roi x,y,w,h`, `--out`).
- `run` — runs the bot (`--dry-run` logs decisions without sending input).
  F12 pauses/resumes, Ctrl+F12 quits; losing focus pauses automatically.
- `press` / `mouse` — send one key / mouse action to the game to check that input works.

- `layout` — restores the game window to 1600x900 and aligns it to the top-right of the screen
  (room for a terminal on the left). Don't use Windows Snap on the game: it resizes the window.
- `probe` / `find` — hover-sweep calibration / target search (no clicks).

Commands that act on the game refuse to start if the client area isn't 1600x900, and `run`
pauses itself if the window is resized while running.

**The game runs as administrator, so the bot must too** (from an elevated terminal).
Otherwise Windows silently drops every key and click the bot sends; the bot warns about this at startup.

## Vision tests

`samples/labels.json` lists real screenshots with their expected readings (HUD visible,
HP/MP/STM %). `dotnet test` runs the real pipeline on each of them. To cover a new situation,
capture it, add an entry, and run the tests.

Logs go to `logs/botpriston-YYYYMMDD.log` (always at debug level).

## Tests

```
dotnet test
```
