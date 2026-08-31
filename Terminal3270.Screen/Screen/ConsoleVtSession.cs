using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Terminal3270.Screen;

/// <summary>
/// Turns the Windows console into a VT terminal and puts it back afterwards.
///
/// This is the whole reason the project stays on a plain net10.0 TFM: a P/Invoke
/// into kernel32 is just a DllImport, it doesn't pull in a Windows-targeted
/// reference assembly the way WinForms or WPF would. The OS check keeps the
/// platform analyser quiet and lets the same binary load on any runtime.
/// </summary>
public sealed class ConsoleVtSession : IDisposable
{
    private const int STD_OUTPUT_HANDLE = -11;
    private const int STD_INPUT_HANDLE = -10;

    private const uint ENABLE_PROCESSED_OUTPUT = 0x0001;
    private const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;
    private const uint DISABLE_NEWLINE_AUTO_RETURN = 0x0008;

    private const uint ENABLE_PROCESSED_INPUT = 0x0001;
    private const uint ENABLE_LINE_INPUT = 0x0002;
    private const uint ENABLE_ECHO_INPUT = 0x0004;
    private const uint ENABLE_QUICK_EDIT_MODE = 0x0040;
    private const uint ENABLE_EXTENDED_FLAGS = 0x0080;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr handle, out uint mode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(IntPtr handle, uint mode);

    private readonly bool _active;
    private readonly IntPtr _out, _in;
    private readonly uint _originalOut, _originalIn;
    private readonly bool _originalCtrlC;
    private bool _disposed;

    private ConsoleVtSession(bool active, IntPtr o, IntPtr i, uint oo, uint oi, bool ctrlC)
        => (_active, _out, _in, _originalOut, _originalIn, _originalCtrlC) = (active, o, i, oo, oi, ctrlC);

    public static ConsoleVtSession Begin()
    {
        if (!OperatingSystem.IsWindows())
            return new ConsoleVtSession(false, default, default, 0, 0, false);

        return BeginWindows();
    }

    [SupportedOSPlatform("windows")]
    private static ConsoleVtSession BeginWindows()
    {
        var stdout = GetStdHandle(STD_OUTPUT_HANDLE);
        var stdin = GetStdHandle(STD_INPUT_HANDLE);

        if (!GetConsoleMode(stdout, out var oldOut) || !GetConsoleMode(stdin, out var oldIn))
            throw new InvalidOperationException(
                "Not attached to a console. Set <OutputType>Exe</OutputType>, not WinExe.");

        // Output: interpret escape sequences, and stop the console wrapping when
        // we write to the last column of the last row.
        SetConsoleMode(stdout, oldOut
            | ENABLE_PROCESSED_OUTPUT
            | ENABLE_VIRTUAL_TERMINAL_PROCESSING
            | DISABLE_NEWLINE_AUTO_RETURN);

        // Input: no line buffering, no echo, and crucially no QuickEdit — with
        // QuickEdit on, a stray click selects text and freezes all output until
        // the user presses Escape. Note we do NOT set ENABLE_VIRTUAL_TERMINAL_INPUT;
        // that would break Console.ReadKey, which wants the classic input records.
        var newIn = (oldIn | ENABLE_EXTENDED_FLAGS)
                    & ~(ENABLE_LINE_INPUT | ENABLE_ECHO_INPUT | ENABLE_QUICK_EDIT_MODE | ENABLE_PROCESSED_INPUT);
        SetConsoleMode(stdin, newIn);

        var ctrlC = Console.TreatControlCAsInput;
        Console.TreatControlCAsInput = true;      // Ctrl+C is a 3270 key, not a kill signal
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var session = new ConsoleVtSession(true, stdout, stdin, oldOut, oldIn, ctrlC);

        // Belt and braces: a crash shouldn't leave the user's shell in raw mode.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => session.Dispose();

        return session;
    }

    public void Dispose()
    {
        if (_disposed || !_active) { _disposed = true; return; }
        _disposed = true;

        Console.Write(Vt.ExitAltBuffer + Vt.ShowCursor + Vt.Reset);
        Console.Out.Flush();

        if (OperatingSystem.IsWindows())
        {
            SetConsoleMode(_out, _originalOut);
            SetConsoleMode(_in, _originalIn);
            Console.TreatControlCAsInput = _originalCtrlC;
        }
    }
}
