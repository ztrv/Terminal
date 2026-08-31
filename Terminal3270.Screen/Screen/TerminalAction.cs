namespace Terminal3270.Screen;

public enum AidKey
{
    None, Enter, Clear, Reset,
    PA1, PA2, PA3,
    PF1, PF2, PF3, PF4, PF5, PF6, PF7, PF8, PF9, PF10, PF11, PF12,
    PF13, PF14, PF15, PF16, PF17, PF18, PF19, PF20, PF21, PF22, PF23, PF24
}

public enum EditKind { Tab, BackTab, Home, Backspace, Delete, EraseEof, ToggleInsert }

/// <summary>
/// What a keystroke means, independent of where it came from. Nothing downstream
/// of here knows about ConsoleKeyInfo — swap the keymap, keep everything else.
/// </summary>
public abstract record TerminalAction
{
    public sealed record Character(char Value) : TerminalAction;
    public sealed record Move(int DRow, int DCol) : TerminalAction;
    public sealed record Edit(EditKind Kind) : TerminalAction;
    public sealed record Aid(AidKey Key) : TerminalAction;
    public sealed record Quit : TerminalAction;
    public sealed record Ignore : TerminalAction;

    public static readonly TerminalAction Nothing = new Ignore();
}

public static class TerminalActionExtensions
{
    /// <summary>Applies a mapped action to the buffer.</summary>
    public static void Apply(this ScreenBuffer buffer, TerminalAction action)
    {
        switch (action)
        {
            case TerminalAction.Character c:
                if (buffer.KeyboardLocked) { buffer.Lock("X -f"); break; }
                buffer.TypeChar(c.Value);
                break;

            case TerminalAction.Move m:
                buffer.MoveCursor(m.DRow, m.DCol);
                break;

            case TerminalAction.Edit ed:
                if (buffer.KeyboardLocked && ed.Kind != EditKind.ToggleInsert) { buffer.Lock("X -f"); break; }
                switch (ed.Kind)
                {
                    case EditKind.Tab: buffer.Tab(); break;
                    case EditKind.BackTab: buffer.BackTab(); break;
                    case EditKind.Home: buffer.Home(); break;
                    case EditKind.Backspace: buffer.Backspace(); break;
                    case EditKind.Delete: buffer.Delete(); break;
                    case EditKind.EraseEof: buffer.EraseEof(); break;
                    case EditKind.ToggleInsert: buffer.ToggleInsert(); break;
                }
                break;

            case TerminalAction.Aid a:
                buffer.RaiseAid(a.Key);
                break;
        }
    }
}
