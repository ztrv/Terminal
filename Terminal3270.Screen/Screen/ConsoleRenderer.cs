using System.Text;

namespace Terminal3270.Screen;

/// <summary>
/// Paints the buffer as VT escape sequences. Same dirty-row discipline as the
/// Blazor version: a keystroke repaints one line, not the screen.
/// </summary>
public sealed class ConsoleRenderer
{
    private readonly TextWriter _out;
    private readonly StringBuilder _frame = new(8192);

    private int _lastWidth, _lastHeight;
    private int _offsetX, _offsetY;
    private bool _forceFull = true;
    private bool _tooSmall;
    private bool _lastInsert;

    public ConsoleRenderer()
    {
        // One buffered writer, one flush per frame. Writing straight to
        // Console.Out unbuffered is the classic source of visible tearing.
        // The encoding must be BOM-less: StreamWriter emits Encoding.UTF8's
        // preamble on first write, which lands on screen as stray characters.
        _out = new StreamWriter(Console.OpenStandardOutput(),
                                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                                16384)
        {
            AutoFlush = false
        };
        Console.Write(Vt.EnterAltBuffer + Vt.HideCursor + Vt.ClearScreen);
    }

    /// <summary>Repaint everything on the next frame — after a resize, or on startup.</summary>
    public void Invalidate() => _forceFull = true;

    /// <summary>Returns true if the window changed size since the last check.</summary>
    public bool PollResize()
    {
        int w, h;
        try { w = Console.WindowWidth; h = Console.WindowHeight; }
        catch (IOException) { return false; }          // window minimised or detached

        if (w == _lastWidth && h == _lastHeight) return false;
        _lastWidth = w;
        _lastHeight = h;
        Invalidate();
        return true;
    }

    public void Paint(ScreenBuffer buffer)
    {
        PollResize();
        _frame.Clear();

        // The one thing a console host can't do: change the font size. So instead
        // of scaling the grid to the window, centre it and say so when it won't fit.
        var needH = buffer.Rows + 1;                   // +1 for the OIA line
        _tooSmall = _lastWidth < buffer.Cols || _lastHeight < needH;

        if (_tooSmall)
        {
            PaintTooSmall(buffer);
            Flush();
            return;
        }

        _offsetX = (_lastWidth - buffer.Cols) / 2;
        _offsetY = (_lastHeight - needH) / 2;

        if (_forceFull) _frame.Append(Vt.Reset).Append(Vt.ClearScreen);

        foreach (var row in buffer.RowList)
        {
            if (!_forceFull && !row.Dirty) continue;

            _frame.Append(Vt.MoveTo(_offsetY + row.Index, _offsetX));
            foreach (var run in row.Runs())
                _frame.Append(Vt.Sgr(run.Attribute)).Append(run.Text);
        }

        PaintOia(buffer);
        PaintCursor(buffer);

        _frame.Append(Vt.Reset);
        Flush();

        buffer.ClearDirty();
        _forceFull = false;
    }

    private void PaintCursor(ScreenBuffer buffer)
    {
        // Use the console's own cursor rather than drawing a block: it blinks for
        // free, and it's what screen readers and the IME follow.
        if (buffer.InsertMode != _lastInsert || _forceFull)
        {
            _frame.Append(buffer.InsertMode ? Vt.CursorUnderline : Vt.CursorBlock);
            _lastInsert = buffer.InsertMode;
        }

        _frame.Append(Vt.MoveTo(_offsetY + buffer.CursorRow, _offsetX + buffer.CursorCol))
              .Append(Vt.ShowCursor);
    }

    private void PaintOia(ScreenBuffer buffer)
    {
        var left = $"4 {(buffer.KeyboardLocked ? "X" : "A")}  {buffer.StatusMessage}";
        var right = $"{(buffer.InsertMode ? "^" : " ")} {buffer.CursorRow + 1:00}/{buffer.CursorCol + 1:000}";
        var pad = Math.Max(1, buffer.Cols - left.Length - right.Length);

        _frame.Append(Vt.MoveTo(_offsetY + buffer.Rows, _offsetX))
              .Append(Vt.Sgr(new CellAttribute(ScreenColor.Turquoise)))
              .Append(left).Append(new string(' ', pad)).Append(right)
              .Append(Vt.ClearToEol);
    }

    private void PaintTooSmall(ScreenBuffer buffer)
    {
        var msg = $"Window is {_lastWidth}x{_lastHeight}. Needs at least {buffer.Cols}x{buffer.Rows + 1}.";
        _frame.Append(Vt.Reset).Append(Vt.ClearScreen)
              .Append(Vt.MoveTo(Math.Max(0, _lastHeight / 2), Math.Max(0, (_lastWidth - msg.Length) / 2)))
              .Append(Vt.Sgr(new CellAttribute(ScreenColor.Yellow)))
              .Append(msg.Length <= _lastWidth ? msg : msg[.._lastWidth])
              .Append(Vt.Reset)
              .Append(Vt.HideCursor);
        _forceFull = true;                             // full repaint once it grows back
    }

    private void Flush()
    {
        _out.Write(_frame);
        _out.Flush();
    }
}
