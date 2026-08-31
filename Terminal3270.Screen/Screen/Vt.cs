using System.Collections.Concurrent;

namespace Terminal3270.Screen;

/// <summary>Escape sequences, in one place so the renderer stays readable.</summary>
public static class Vt
{
    public const string Esc = "\x1b";

    public const string EnterAltBuffer = Esc + "[?1049h";
    public const string ExitAltBuffer  = Esc + "[?1049l";
    public const string ClearScreen    = Esc + "[2J";
    public const string HideCursor     = Esc + "[?25l";
    public const string ShowCursor     = Esc + "[?25h";
    public const string Reset          = Esc + "[0m";

    // DECSCUSR — this is how the insert-mode cursor shape gets done without
    // drawing it yourself. The console blinks it for you.
    public const string CursorBlock     = Esc + "[1 q";
    public const string CursorUnderline = Esc + "[3 q";

    /// <summary>1-based cursor positioning.</summary>
    public static string MoveTo(int row, int col) => $"{Esc}[{row + 1};{col + 1}H";

    /// <summary>Erase from the cursor to the end of the line.</summary>
    public const string ClearToEol = Esc + "[K";

    // Same palette as the CSS version, as 24-bit colour. Windows Terminal renders
    // these exactly; legacy conhost quantises to its 16-colour table, which still
    // looks right because the hues are well separated.
    private static readonly (int R, int G, int B)[] Palette =
    {
        (0x33, 0xFF, 0x66),   // Default (green)
        (0x6A, 0xB8, 0xFF),   // Blue
        (0xFF, 0x5F, 0x56),   // Red
        (0xFF, 0x7F, 0xD0),   // Pink
        (0x33, 0xFF, 0x66),   // Green
        (0x4F, 0xE3, 0xE3),   // Turquoise
        (0xFF, 0xD7, 0x5F),   // Yellow
        (0xE8, 0xF2, 0xEC),   // Neutral
    };

    private static readonly ConcurrentDictionary<CellAttribute, string> SgrCache = new();

    /// <summary>The full SGR run for an attribute, reset-prefixed so it's absolute.</summary>
    public static string Sgr(CellAttribute a) => SgrCache.GetOrAdd(a, static attr =>
    {
        var (r, g, b) = Palette[(int)attr.Color];
        var sb = new System.Text.StringBuilder(Esc).Append("[0");
        if (attr.Intensified) sb.Append(";1");
        if (attr.Underline) sb.Append(";4");
        sb.Append(";38;2;").Append(r).Append(';').Append(g).Append(';').Append(b).Append('m');
        return sb.ToString();
    });
}
