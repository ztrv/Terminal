using Terminal3270.Screen;

// Two lines of wiring is the whole point of the split: the library owns the
// console, the model, and the keyboard; the app owns what goes on the screen.
var screen = new ScreenBuffer(rows: 24, cols: 80);
var host = new TerminalHost(screen);

const int TypingTop = 5;
const int TypingRows = 13;
const int KeyLine = 20;

DrawFrame();

// Echo the mapped action so you can see key interception working — press F5 and
// watch PF5 appear, press Shift+F5 and watch PF17.
host.KeyPressed += action => screen.Write(
    KeyLine, 15, Describe(action).PadRight(60),
    new CellAttribute(ScreenColor.Yellow, Protected: true));

// AID keys lock the keyboard until a host replies. There is no host yet, so
// unlock immediately — otherwise the first Enter freezes input and looks broken.
screen.AidPressed += _ =>
{
    screen.Unlock();
    screen.Home();
};

host.Run();

void DrawFrame()
{
    screen.Clear();

    screen.Write(1, 26, "TERMINAL SCREEN ECHO TEST", CellAttribute.Heading);
    screen.Write(3, 2, "Type anywhere below. Arrows move, Insert toggles, End erases to end of field.",
                 CellAttribute.Label);

    // One field spanning the typing area. Length is linear across the buffer,
    // so it wraps from the end of one row to the start of the next.
    screen.DefineField(TypingTop, 2, TypingRows * screen.Cols - 4, CellAttribute.Input);

    screen.Write(KeyLine, 2, "Last key:", CellAttribute.Label);
    screen.Write(22, 2,
                 "F1-F12=PF1-12   Shift+F=PF13-24   Alt+1/2/3=PA   Esc=Reset   Ctrl+Q=Quit",
                 new CellAttribute(ScreenColor.Blue, Protected: true));

    screen.SetCursor(TypingTop, 2);
}

static string Describe(TerminalAction action) => action switch
{
    TerminalAction.Character c => $"character '{c.Value}'",
    TerminalAction.Aid a => $"{a.Key} (AID)",
    TerminalAction.Edit e => e.Kind.ToString(),
    TerminalAction.Move m => m switch
    {
        { DRow: -1 } => "cursor up",
        { DRow: 1 } => "cursor down",
        { DCol: -1 } => "cursor left",
        _ => "cursor right"
    },
    TerminalAction.Quit => "quit",
    _ => "(unmapped)"
};
