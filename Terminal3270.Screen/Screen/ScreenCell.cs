namespace Terminal3270.Screen;

/// <summary>Extended-colour set as used by a 3279/3179 display.</summary>
public enum ScreenColor
{
    Default = 0,   // green
    Blue,
    Red,
    Pink,
    Green,
    Turquoise,
    Yellow,
    Neutral        // white
}

/// <summary>
/// Per-cell presentation state. A value type so the buffer stays one flat array
/// with no allocation per cell.
/// </summary>
public readonly record struct CellAttribute(
    ScreenColor Color = ScreenColor.Default,
    bool Protected = false,
    bool Intensified = false,
    bool Hidden = false,
    bool Underline = false)
{
    public static readonly CellAttribute Default = new();
    public static readonly CellAttribute Label = new(ScreenColor.Turquoise, Protected: true);
    public static readonly CellAttribute Heading = new(ScreenColor.Neutral, Protected: true, Intensified: true);
    public static readonly CellAttribute Input = new(ScreenColor.Green);
    public static readonly CellAttribute Password = new(ScreenColor.Green, Hidden: true);
}
