namespace Terminal3270.Screen;

/// <summary>
/// Single place to change the keyboard layout. On Windows, Console.ReadKey goes
/// through ReadConsoleInput, so modifier state is reported for function keys —
/// Shift+F3 arrives as (F3, Shift), which is what makes PF13–PF24 reachable.
/// </summary>
public static class ConsoleKeyMap
{
    public static TerminalAction Map(ConsoleKeyInfo k)
    {
        var shift = (k.Modifiers & ConsoleModifiers.Shift) != 0;
        var ctrl  = (k.Modifiers & ConsoleModifiers.Control) != 0;
        var alt   = (k.Modifiers & ConsoleModifiers.Alt) != 0;

        // Alt+1/2/3 stand in for the PA keys, which have no PC equivalent.
        if (alt && k.Key is ConsoleKey.D1 or ConsoleKey.D2 or ConsoleKey.D3)
            return new TerminalAction.Aid(k.Key switch
            {
                ConsoleKey.D1 => AidKey.PA1,
                ConsoleKey.D2 => AidKey.PA2,
                _ => AidKey.PA3
            });

        if (ctrl && k.Key == ConsoleKey.Q) return new TerminalAction.Quit();

        // Note: the console API reports no left/right distinction, so the classic
        // "right Ctrl is Enter" 3270 binding isn't reachable here. Enter only.

        // F1–F12 give PF1–PF12; shifted give PF13–PF24.
        if (k.Key is >= ConsoleKey.F1 and <= ConsoleKey.F12)
        {
            var n = k.Key - ConsoleKey.F1;
            return new TerminalAction.Aid((AidKey)((int)AidKey.PF1 + n + (shift ? 12 : 0)));
        }

        switch (k.Key)
        {
            case ConsoleKey.Enter:     return new TerminalAction.Aid(AidKey.Enter);
            case ConsoleKey.Escape:    return new TerminalAction.Aid(AidKey.Reset);
            case ConsoleKey.Pause:     return new TerminalAction.Aid(AidKey.Clear);

            case ConsoleKey.Tab:       return new TerminalAction.Edit(shift ? EditKind.BackTab : EditKind.Tab);
            case ConsoleKey.Home:      return new TerminalAction.Edit(EditKind.Home);
            case ConsoleKey.End:       return new TerminalAction.Edit(EditKind.EraseEof);
            case ConsoleKey.Backspace: return new TerminalAction.Edit(EditKind.Backspace);
            case ConsoleKey.Delete:    return new TerminalAction.Edit(EditKind.Delete);
            case ConsoleKey.Insert:    return new TerminalAction.Edit(EditKind.ToggleInsert);

            case ConsoleKey.UpArrow:    return new TerminalAction.Move(-1, 0);
            case ConsoleKey.DownArrow:  return new TerminalAction.Move(1, 0);
            case ConsoleKey.LeftArrow:  return new TerminalAction.Move(0, -1);
            case ConsoleKey.RightArrow: return new TerminalAction.Move(0, 1);
        }

        // Anything printable is data. KeyChar is already layout-aware, so this
        // works on non-US keyboards without a lookup table.
        if (!ctrl && !alt && k.KeyChar != '\0' && !char.IsControl(k.KeyChar))
            return new TerminalAction.Character(k.KeyChar);

        return TerminalAction.Nothing;
    }
}
