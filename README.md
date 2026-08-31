# Terminal3270

A character-cell terminal screen — configurable rows and columns, 3270-style
fields and AID keys — split into a reusable library and a demo host.

Plain `net10.0` throughout. No OS-specific target framework, no third-party
packages, nothing outside the BCL.

```
Terminal3270.slnx
src/
  Terminal3270.Screen/        class library — the whole thing
    Screen/ScreenBuffer.cs        rows x cols model, fields, cursor, dirty rows
    Screen/ScreenCell.cs          CellAttribute and the colour enum
    Screen/TerminalAction.cs      AidKey / EditKind / TerminalAction, no UI types
    Screen/ConsoleVtSession.cs    console mode setup and teardown
    Screen/Vt.cs                  escape sequences and palette
    Screen/ConsoleRenderer.cs     dirty-row painting, letterboxing, resize
    Screen/ConsoleKeyMap.cs       ConsoleKeyInfo -> TerminalAction
    Screen/TerminalHost.cs        the run loop — the public entry point
  Terminal3270.Demo/          console app — echo test
    Program.cs
```

## Build and run

```bash
dotnet build                                  # works from macOS
dotnet run --project src/Terminal3270.Demo    # Windows only — needs a real console
```

`.slnx` needs SDK 9.0.200 or later. On anything older, delete it and use:

```bash
dotnet new sln -n Terminal3270
dotnet sln add src/Terminal3270.Screen src/Terminal3270.Demo
```

## What the demo does

Opens a full-screen console window with a typing area. Characters echo where the
cursor is; arrows move; Insert toggles between overtype and insert; End erases to
the end of the field. Row 20 shows the last mapped key, so you can watch
interception work — press F5 and `PF5 (AID)` appears, Shift+F5 gives `PF17 (AID)`.

Ctrl+Q exits. Ctrl+C does not — it's captured as a mappable key, as it would be
on a real terminal.

## Wiring it into your own app

```csharp
var screen = new ScreenBuffer(rows: 24, cols: 80);
var host = new TerminalHost(screen);

screen.AidPressed += key => { /* send to your session */ };
host.KeyPressed  += action => { /* optional: diagnostics, macros */ };

host.Run(cancellationToken);
```

`Run` blocks until Ctrl+Q, `Stop()`, or cancellation. From a background thread,
mutate through `host.Post(s => s.Write(...))` — it takes the same lock the render
loop holds, so you never paint a half-updated row.

The `AidPressed` handler must call `screen.Unlock()` when the host replies. A
buffer left locked looks exactly like a hung session, because that's what it is.
The demo unlocks immediately since there's no host behind it yet.

## Things that will bite you

- **`<OutputType>Exe</OutputType>`, not `WinExe`.** `WinExe` detaches from the
  console and `ConsoleVtSession.Begin()` throws with a message saying so.
- **QuickEdit mode is disabled deliberately.** Leave it on and a stray click
  selects text and freezes all output until Escape — indistinguishable from a hang.
- **`ENABLE_VIRTUAL_TERMINAL_INPUT` is deliberately not set.** It breaks
  `Console.ReadKey`, which wants the classic input records. VT is output-only here.
- **You don't own the font size.** That's the user's Windows Terminal profile.
  The grid centres in the window rather than scaling; undersized windows get a
  message stating the minimum.
- **No resize event exists on `System.Console`.** The loop polls `WindowWidth`
  every 16 ms — that's why there's a loop rather than a blocking `ReadKey`.
- **Right-Ctrl as Enter is unreachable.** The console API reports no left/right
  distinction.

## Keyboard map

Everything lives in `ConsoleKeyMap.Map` — one method, one switch.

| Key | Action |
| --- | --- |
| Enter | Enter (AID) |
| F1–F12 | PF1–PF12 |
| Shift+F1–F12 | PF13–PF24 |
| Alt+1/2/3 | PA1/PA2/PA3 |
| Escape | Reset |
| Pause | Clear |
| Tab / Shift+Tab | next / previous field |
| Home | first field |
| End | Erase EOF |
| Insert | toggle insert mode |
| Ctrl+Q | quit |
