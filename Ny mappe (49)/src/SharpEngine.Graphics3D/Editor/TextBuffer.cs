using System.Text;

namespace SharpEngine.Graphics3D.Editor;

/// <summary>
/// The text of one file as lines, with a caret, a selection and undo. All editing rules of the code editor live here
/// (no drawing, no input), so they can be tested without a window.
/// </summary>
public sealed class TextBuffer
{
    public const int TabSize = 4;

    private readonly List<string> _lines = new() { "" };
    private readonly List<Snapshot> _undo = new();
    private readonly List<Snapshot> _redo = new();
    private string _lastEdit = "";
    private double _lastEditTime;

    private readonly record struct Snapshot(string Text, int Line, int Col);

    public int Line { get; private set; }
    public int Col { get; private set; }
    public int AnchorLine { get; private set; }
    public int AnchorCol { get; private set; }

    /// <summary>Changes with every edit (cached syntax colours check it).</summary>
    public int Version { get; private set; }

    /// <summary>True when the text differs from what was last saved.</summary>
    public bool Dirty { get; private set; }

    public int LineCount => _lines.Count;
    public string GetLine(int i) => _lines[i];

    /// <summary>Seconds clock used to join typing into one undo step (tests replace it).</summary>
    public Func<double> Clock { get; set; } = () => Environment.TickCount64 / 1000.0;

    public string Text
    {
        get => string.Join("\n", _lines);
        set
        {
            _lines.Clear();
            _lines.AddRange(value.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'));
            Line = Col = AnchorLine = AnchorCol = 0;
            _undo.Clear(); _redo.Clear();
            Dirty = false;
            Version++;
        }
    }

    public void MarkSaved() => Dirty = false;

    public bool HasSelection => Line != AnchorLine || Col != AnchorCol;

    public ((int Line, int Col) Start, (int Line, int Col) End) Selection
    {
        get
        {
            var a = (AnchorLine, AnchorCol); var b = (Line, Col);
            return a.Item1 < b.Item1 || (a.Item1 == b.Item1 && a.Item2 <= b.Item2) ? (a, b) : (b, a);
        }
    }

    public string SelectedText
    {
        get
        {
            if (!HasSelection) return "";
            var (s, e) = Selection;
            if (s.Line == e.Line) return _lines[s.Line][s.Col..e.Col];
            var sb = new StringBuilder(_lines[s.Line][s.Col..]);
            for (int i = s.Line + 1; i < e.Line; i++) sb.Append('\n').Append(_lines[i]);
            sb.Append('\n').Append(_lines[e.Line][..e.Col]);
            return sb.ToString();
        }
    }

    // ---- undo ---------------------------------------------------------------------------------

    private Snapshot Take() => new(Text, Line, Col);

    /// <summary>Call before an edit. Typing and deleting one character at a time are joined for about a second.</summary>
    private void Begin(string kind)
    {
        double now = Clock();
        bool join = kind == _lastEdit && now - _lastEditTime < 1.0 && _undo.Count > 0 && (kind == "type" || kind == "back");
        if (!join) { _undo.Add(Take()); if (_undo.Count > 200) _undo.RemoveAt(0); }
        _lastEdit = kind; _lastEditTime = now;
        _redo.Clear();
        Dirty = true; Version++;
    }

    private void Restore(Snapshot s)
    {
        _lines.Clear();
        _lines.AddRange(s.Text.Split('\n'));
        Line = Math.Min(s.Line, _lines.Count - 1);
        Col = Math.Min(s.Col, _lines[Line].Length);
        AnchorLine = Line; AnchorCol = Col;
        Dirty = true; Version++; _lastEdit = "";
    }

    public bool Undo()
    {
        if (_undo.Count == 0) return false;
        _redo.Add(Take());
        var s = _undo[^1]; _undo.RemoveAt(_undo.Count - 1);
        Restore(s);
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0) return false;
        _undo.Add(Take());
        var s = _redo[^1]; _redo.RemoveAt(_redo.Count - 1);
        Restore(s);
        return true;
    }

    // ---- moving -------------------------------------------------------------------------------

    private void SetCaret(int line, int col, bool select)
    {
        Line = Math.Clamp(line, 0, _lines.Count - 1);
        Col = Math.Clamp(col, 0, _lines[Line].Length);
        if (!select) { AnchorLine = Line; AnchorCol = Col; }
        _lastEdit = "";
    }

    public void MoveTo(int line, int col, bool select = false) => SetCaret(line, col, select);

    public void Left(bool select = false, bool word = false)
    {
        if (!select && HasSelection) { var (s, _) = Selection; SetCaret(s.Line, s.Col, false); return; }
        if (word) { WordLeft(select); return; }
        if (Col > 0) SetCaret(Line, Col - 1, select);
        else if (Line > 0) SetCaret(Line - 1, _lines[Line - 1].Length, select);
    }

    public void Right(bool select = false, bool word = false)
    {
        if (!select && HasSelection) { var (_, e) = Selection; SetCaret(e.Line, e.Col, false); return; }
        if (word) { WordRight(select); return; }
        if (Col < _lines[Line].Length) SetCaret(Line, Col + 1, select);
        else if (Line < _lines.Count - 1) SetCaret(Line + 1, 0, select);
    }

    public void Up(bool select = false, int lines = 1) => SetCaret(Line - lines, Col, select);
    public void Down(bool select = false, int lines = 1) => SetCaret(Line + lines, Col, select);

    /// <summary>First press goes to the first non-blank character, the next to column 0.</summary>
    public void Home(bool select = false)
    {
        int first = _lines[Line].Length - _lines[Line].TrimStart().Length;
        SetCaret(Line, Col == first ? 0 : first, select);
    }

    public void End(bool select = false) => SetCaret(Line, _lines[Line].Length, select);
    public void DocumentStart(bool select = false) => SetCaret(0, 0, select);
    public void DocumentEnd(bool select = false) => SetCaret(_lines.Count - 1, _lines[^1].Length, select);

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private void WordLeft(bool select)
    {
        int l = Line, c = Col;
        if (c == 0) { if (l > 0) SetCaret(l - 1, _lines[l - 1].Length, select); return; }
        var s = _lines[l];
        while (c > 0 && !IsWordChar(s[c - 1])) c--;
        while (c > 0 && IsWordChar(s[c - 1])) c--;
        SetCaret(l, c, select);
    }

    private void WordRight(bool select)
    {
        int l = Line, c = Col;
        var s = _lines[l];
        if (c >= s.Length) { if (l < _lines.Count - 1) SetCaret(l + 1, 0, select); return; }
        while (c < s.Length && !IsWordChar(s[c])) c++;
        while (c < s.Length && IsWordChar(s[c])) c++;
        SetCaret(l, c, select);
    }

    public void SelectAll()
    {
        AnchorLine = 0; AnchorCol = 0;
        Line = _lines.Count - 1; Col = _lines[^1].Length;
    }

    public void SelectWordAt(int line, int col)
    {
        line = Math.Clamp(line, 0, _lines.Count - 1);
        var s = _lines[line];
        col = Math.Clamp(col, 0, s.Length);
        int a = col, b = col;
        while (a > 0 && IsWordChar(s[a - 1])) a--;
        while (b < s.Length && IsWordChar(s[b])) b++;
        AnchorLine = line; AnchorCol = a; Line = line; Col = b;
    }

    // ---- editing ------------------------------------------------------------------------------

    private void DeleteSelection()
    {
        if (!HasSelection) return;
        var (s, e) = Selection;
        string head = _lines[s.Line][..s.Col], tail = _lines[e.Line][e.Col..];
        _lines.RemoveRange(s.Line, e.Line - s.Line + 1);
        _lines.Insert(s.Line, head + tail);
        Line = AnchorLine = s.Line; Col = AnchorCol = s.Col;
    }

    /// <summary>Types or pastes text at the caret (replacing the selection). Line breaks in the text start new lines.</summary>
    public void Insert(string text, bool typing = true)
    {
        if (text.Length == 0) return;
        Begin(typing && !text.Contains('\n') ? "type" : "paste");
        DeleteSelection();
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var parts = text.Split('\n');
        string head = _lines[Line][..Col], tail = _lines[Line][Col..];
        if (parts.Length == 1)
        {
            _lines[Line] = head + parts[0] + tail;
            Col += parts[0].Length;
        }
        else
        {
            _lines[Line] = head + parts[0];
            for (int i = 1; i < parts.Length - 1; i++) _lines.Insert(Line + i, parts[i]);
            _lines.Insert(Line + parts.Length - 1, parts[^1] + tail);
            Line += parts.Length - 1;
            Col = parts[^1].Length;
        }
        AnchorLine = Line; AnchorCol = Col;
    }

    /// <summary>Types one character. A "}" typed on a line that only holds indentation steps back one indent level first.</summary>
    public void Type(char c)
    {
        if (c == '}' && !HasSelection && Col >= TabSize && _lines[Line][..Col].All(ch => ch == ' '))
        {
            Begin("type");
            _lines[Line] = _lines[Line][TabSize..];
            Col -= TabSize; AnchorLine = Line; AnchorCol = Col;
            Insert("}");
            return;
        }
        Insert(c.ToString());
    }

    /// <summary>Enter: keeps the indent of the line, adds one level after "{", and puts a closing "}" on its own line when between braces.</summary>
    public void NewLine()
    {
        Begin("enter");
        DeleteSelection();
        string cur = _lines[Line];
        string head = cur[..Col], tail = cur[Col..];
        string indent = new(head.TakeWhile(c => c == ' ' || c == '\t').ToArray());
        bool open = head.TrimEnd().EndsWith('{');
        bool between = open && tail.TrimStart().StartsWith('}');
        string next = indent + (open ? new string(' ', TabSize) : "");
        _lines[Line] = head;
        if (between)
        {
            _lines.Insert(Line + 1, next);
            _lines.Insert(Line + 2, indent + tail.TrimStart());
        }
        else _lines.Insert(Line + 1, next + tail.TrimStart());
        Line++; Col = next.Length;
        AnchorLine = Line; AnchorCol = Col;
    }

    public void Backspace(bool word = false)
    {
        if (HasSelection) { Begin("delete"); DeleteSelection(); return; }
        if (Col == 0 && Line == 0) return;
        Begin("back");
        if (Col == 0)
        {
            Col = _lines[Line - 1].Length;
            _lines[Line - 1] += _lines[Line];
            _lines.RemoveAt(Line);
            Line--;
        }
        else if (word)
        {
            int c = Col; var s = _lines[Line];
            while (c > 0 && !IsWordChar(s[c - 1])) c--;
            while (c > 0 && IsWordChar(s[c - 1])) c--;
            _lines[Line] = s[..c] + s[Col..];
            Col = c;
        }
        else
        {
            // in leading spaces, one press removes back to the previous tab stop
            var s = _lines[Line];
            int n = 1;
            if (s[..Col].All(ch => ch == ' ') && Col >= TabSize && Col % TabSize == 0) n = TabSize;
            _lines[Line] = s[..(Col - n)] + s[Col..];
            Col -= n;
        }
        AnchorLine = Line; AnchorCol = Col;
    }

    public void Delete(bool word = false)
    {
        if (HasSelection) { Begin("delete"); DeleteSelection(); return; }
        if (Line == _lines.Count - 1 && Col >= _lines[Line].Length) return;
        Begin("delete");
        if (Col >= _lines[Line].Length)
        {
            _lines[Line] += _lines[Line + 1];
            _lines.RemoveAt(Line + 1);
        }
        else if (word)
        {
            int c = Col; var s = _lines[Line];
            while (c < s.Length && !IsWordChar(s[c])) c++;
            while (c < s.Length && IsWordChar(s[c])) c++;
            _lines[Line] = s[..Col] + s[c..];
        }
        else _lines[Line] = _lines[Line][..Col] + _lines[Line][(Col + 1)..];
    }

    /// <summary>Tab: inserts spaces up to the next tab stop, or indents every selected line.</summary>
    public void Indent()
    {
        if (HasSelection && Selection.Start.Line != Selection.End.Line)
        {
            Begin("indent");
            var (s, e) = Selection;
            int last = e.Col == 0 ? e.Line - 1 : e.Line;
            for (int i = s.Line; i <= last; i++) _lines[i] = new string(' ', TabSize) + _lines[i];
            ShiftSelection(s.Line, last, TabSize);
            return;
        }
        Begin("indent");
        DeleteSelection();
        int add = TabSize - Col % TabSize;
        _lines[Line] = _lines[Line][..Col] + new string(' ', add) + _lines[Line][Col..];
        Col += add; AnchorLine = Line; AnchorCol = Col;
    }

    /// <summary>Shift+Tab: removes one indent level from the caret line or every selected line.</summary>
    public void Outdent()
    {
        var (s, e) = Selection;
        int last = HasSelection && e.Col == 0 && e.Line > s.Line ? e.Line - 1 : e.Line;
        Begin("outdent");
        for (int i = s.Line; i <= last; i++)
        {
            int remove = Math.Min(TabSize, _lines[i].Length - _lines[i].TrimStart(' ').Length);
            if (remove == 0) continue;
            _lines[i] = _lines[i][remove..];
            if (i == Line) Col = Math.Max(0, Col - remove);
            if (i == AnchorLine) AnchorCol = Math.Max(0, AnchorCol - remove);
        }
    }

    private void ShiftSelection(int from, int to, int by)
    {
        if (Line >= from && Line <= to) Col += by;
        if (AnchorLine >= from && AnchorLine <= to) AnchorCol += by;
    }

    /// <summary>Ctrl+/ : comments or uncomments the caret line or every selected line.</summary>
    public void ToggleComment()
    {
        var (s, e) = Selection;
        int last = HasSelection && e.Col == 0 && e.Line > s.Line ? e.Line - 1 : e.Line;
        bool allCommented = Enumerable.Range(s.Line, last - s.Line + 1).All(i => _lines[i].TrimStart().StartsWith("//") || _lines[i].Trim().Length == 0);
        Begin("comment");
        for (int i = s.Line; i <= last; i++)
        {
            var l = _lines[i];
            if (l.Trim().Length == 0) continue;
            int at = l.Length - l.TrimStart().Length;
            if (allCommented)
            {
                int len = l[at..].StartsWith("// ") ? 3 : 2;
                _lines[i] = l[..at] + l[(at + len)..];
                if (i == Line) Col = Math.Max(at, Col - len);
                if (i == AnchorLine) AnchorCol = Math.Max(at, AnchorCol - len);
            }
            else
            {
                _lines[i] = l[..at] + "// " + l[at..];
                if (i == Line) Col += 3;
                if (i == AnchorLine) AnchorCol += 3;
            }
        }
    }

    /// <summary>Ctrl+D: copies the caret line below itself.</summary>
    public void DuplicateLine()
    {
        Begin("dup");
        _lines.Insert(Line + 1, _lines[Line]);
        Line++; AnchorLine = Line;
    }

    /// <summary>Alt+Up/Down: moves the caret line.</summary>
    public void MoveLine(int delta)
    {
        int to = Line + delta;
        if (to < 0 || to >= _lines.Count) return;
        Begin("move");
        (_lines[Line], _lines[to]) = (_lines[to], _lines[Line]);
        Line = to; AnchorLine = to;
    }

    /// <summary>Ctrl+X without a selection cuts the whole line; returns the text that was removed.</summary>
    public string Cut()
    {
        if (!HasSelection)
        {
            Begin("delete");
            string line = _lines[Line] + "\n";
            if (_lines.Count > 1) { _lines.RemoveAt(Line); if (Line >= _lines.Count) Line = _lines.Count - 1; }
            else _lines[0] = "";
            Col = Math.Min(Col, _lines[Line].Length); AnchorLine = Line; AnchorCol = Col;
            return line;
        }
        string t = SelectedText;
        Begin("delete");
        DeleteSelection();
        return t;
    }

    /// <summary>Ctrl+C without a selection copies the whole line.</summary>
    public string Copy() => HasSelection ? SelectedText : _lines[Line] + "\n";
}
