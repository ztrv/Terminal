using System.Text;

namespace Terminal3270.Screen;

/// <summary>One coalesced span of identically-attributed characters within a row.</summary>
public readonly record struct Run(string Text, CellAttribute Attribute);

/// <summary>
/// The screen. Pure C#, no UI dependency — this is the part that survives if you
/// ever swap the renderer, or drive it from a real TN3270 data stream.
/// </summary>
public sealed class ScreenBuffer
{
    private readonly char[] _chars;
    private readonly CellAttribute[] _attrs;
    private readonly bool[] _fieldStart;
    private readonly bool[] _dirty;
    private readonly Row[] _rows;

    public int Rows { get; }
    public int Cols { get; }
    public int CursorRow { get; private set; }
    public int CursorCol { get; private set; }
    public bool InsertMode { get; private set; }
    public bool KeyboardLocked { get; private set; }
    public string StatusMessage { get; private set; } = "";

    /// <summary>Raised when an AID key is pressed — wire this to your host session.</summary>
    public event Action<AidKey>? AidPressed;

    public ScreenBuffer(int rows = 24, int cols = 80)
    {
        if (rows < 1 || cols < 1) throw new ArgumentOutOfRangeException(nameof(rows));
        Rows = rows;
        Cols = cols;
        _chars = new char[rows * cols];
        _attrs = new CellAttribute[rows * cols];
        _fieldStart = new bool[rows * cols];
        _dirty = new bool[rows];
        _rows = new Row[rows];
        for (var i = 0; i < rows; i++) _rows[i] = new Row(this, i);
        Clear();
    }

    public IReadOnlyList<Row> RowList => _rows;

    // ---------------------------------------------------------------- writing

    public void Clear()
    {
        Array.Fill(_chars, ' ');
        Array.Fill(_attrs, CellAttribute.Default);
        Array.Fill(_fieldStart, false);
        Array.Fill(_dirty, true);
        CursorRow = CursorCol = 0;
        InsertMode = false;
        Unlock();
    }

    public void Write(int row, int col, string text, CellAttribute attr)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var c = col + i;
            if (c >= Cols) break;
            var p = Index(row, c);
            _chars[p] = text[i];
            _attrs[p] = attr;
        }
        MarkDirty(row);
    }

    /// <summary>Reserve an input field. Length is the number of enterable positions.</summary>
    public void DefineField(int row, int col, int length, CellAttribute attr)
    {
        var start = Index(row, col);
        _fieldStart[start] = true;
        for (var i = 0; i < length; i++)
        {
            var p = start + i;
            if (p >= _chars.Length) break;
            _chars[p] = ' ';
            _attrs[p] = attr;
        }
        MarkDirty(row);
    }

    /// <summary>Contents of every unprotected field, keyed by its start position.</summary>
    public IReadOnlyDictionary<(int Row, int Col), string> ReadModifiedFields()
    {
        var result = new Dictionary<(int, int), string>();
        for (var p = 0; p < _chars.Length; p++)
        {
            if (!_fieldStart[p] || _attrs[p].Protected) continue;
            var sb = new StringBuilder();
            var q = p;
            while (q < _chars.Length && !_attrs[q].Protected && (q == p || !_fieldStart[q]))
                sb.Append(_chars[q++]);
            result[(p / Cols, p % Cols)] = sb.ToString().TrimEnd();
        }
        return result;
    }

    // ---------------------------------------------------------------- editing

    public void TypeChar(char ch)
    {
        var p = Index(CursorRow, CursorCol);
        if (_attrs[p].Protected) { Lock("X -f"); return; }

        if (InsertMode)
        {
            var end = FieldEnd(p);
            if (_chars[end] != ' ') { Lock("X -f"); return; }
            for (var q = end; q > p; q--) _chars[q] = _chars[q - 1];
        }

        _chars[p] = ch;
        MarkDirty(CursorRow);
        AdvanceCursor();
    }

    public void Backspace()
    {
        if (CursorCol == 0 && CursorRow == 0) return;
        MoveCursor(0, -1);
        var p = Index(CursorRow, CursorCol);
        if (_attrs[p].Protected) { Lock("X -f"); return; }
        _chars[p] = ' ';
        MarkDirty(CursorRow);
    }

    public void Delete()
    {
        var p = Index(CursorRow, CursorCol);
        if (_attrs[p].Protected) { Lock("X -f"); return; }
        var end = FieldEnd(p);
        for (var q = p; q < end; q++) _chars[q] = _chars[q + 1];
        _chars[end] = ' ';
        MarkDirty(CursorRow);
    }

    /// <summary>Erase from the cursor to the end of the current field (3270 EraseEOF).</summary>
    public void EraseEof()
    {
        var p = Index(CursorRow, CursorCol);
        if (_attrs[p].Protected) { Lock("X -f"); return; }
        var end = FieldEnd(p);
        for (var q = p; q <= end; q++)
        {
            _chars[q] = ' ';
            MarkDirty(q / Cols);
        }
    }

    public void ToggleInsert() => InsertMode = !InsertMode;

    // ---------------------------------------------------------------- cursor

    public void SetCursor(int row, int col)
    {
        MarkDirty(CursorRow);
        CursorRow = Math.Clamp(row, 0, Rows - 1);
        CursorCol = Math.Clamp(col, 0, Cols - 1);
        MarkDirty(CursorRow);
    }

    public void MoveCursor(int dRow, int dCol)
    {
        var linear = Index(CursorRow, CursorCol) + dRow * Cols + dCol;
        linear = ((linear % _chars.Length) + _chars.Length) % _chars.Length;   // wrap
        SetCursor(linear / Cols, linear % Cols);
    }

    private void AdvanceCursor() => MoveCursor(0, 1);

    public void Tab() => JumpToField(forward: true);
    public void BackTab() => JumpToField(forward: false);
    public void Home() => JumpToField(forward: true, fromStart: true);

    private void JumpToField(bool forward, bool fromStart = false)
    {
        var starts = new List<int>();
        for (var p = 0; p < _chars.Length; p++)
            if (_fieldStart[p] && !_attrs[p].Protected) starts.Add(p);
        if (starts.Count == 0) return;

        var here = Index(CursorRow, CursorCol);
        int target;
        if (fromStart) target = starts[0];
        else if (forward) target = starts.FirstOrDefault(s => s > here, starts[0]);
        else target = starts.LastOrDefault(s => s < here, starts[^1]);

        SetCursor(target / Cols, target % Cols);
    }

    /// <summary>Last enterable position of the field containing <paramref name="p"/>.</summary>
    private int FieldEnd(int p)
    {
        var q = p;
        while (q + 1 < _chars.Length && !_attrs[q + 1].Protected && !_fieldStart[q + 1]) q++;
        return q;
    }

    // ---------------------------------------------------------------- status

    public void RaiseAid(AidKey key)
    {
        if (KeyboardLocked && key != AidKey.Reset) { Lock("X -f"); return; }
        if (key == AidKey.Reset) { Unlock(); return; }
        Lock("X SYSTEM");
        AidPressed?.Invoke(key);
    }

    public void Lock(string message)
    {
        KeyboardLocked = true;
        StatusMessage = message;
    }

    public void Unlock()
    {
        KeyboardLocked = false;
        StatusMessage = "";
    }

    // ---------------------------------------------------------------- render

    private int Index(int row, int col) => row * Cols + col;

    private void MarkDirty(int row)
    {
        if (row >= 0 && row < Rows) _dirty[row] = true;
    }

    /// <summary>Called by the host component once the frame has been painted.</summary>
    public void ClearDirty() => Array.Fill(_dirty, false);

    internal bool IsDirty(int row) => _dirty[row];

    internal List<Run> RunsForRow(int row)
    {
        var runs = new List<Run>(8);
        var start = row * Cols;
        var current = _attrs[start];
        var sb = new StringBuilder(Cols);

        for (var c = 0; c < Cols; c++)
        {
            var a = _attrs[start + c];
            if (c > 0 && !a.Equals(current))
            {
                runs.Add(new Run(sb.ToString(), current));
                sb.Clear();
                current = a;
            }
            var ch = _chars[start + c];
            sb.Append(current.Hidden && ch != ' ' ? ' ' : ch);
        }
        runs.Add(new Run(sb.ToString(), current));
        return runs;
    }

    /// <summary>A row handle the UI can hold a stable reference to.</summary>
    public sealed class Row
    {
        private readonly ScreenBuffer _buffer;
        public int Index { get; }
        internal Row(ScreenBuffer buffer, int index) { _buffer = buffer; Index = index; }
        public bool Dirty => _buffer.IsDirty(Index);
        public List<Run> Runs() => _buffer.RunsForRow(Index);
    }
}
