using System.Numerics;
using Raylib_cs;
using SharpEngine.Graphics3D.Scripting;
using Rl = Raylib_cs.Raylib;

namespace SharpEngine.Graphics3D.Editor;

/// <summary>
/// The in-game code editor. Lists the project's Scripts/*.cs files, edits them with syntax colours, and compiles them
/// (Ctrl+S) so the classes show up as components you can add to an object or pin to a scene.
/// </summary>
public static class CodeEditorWindow
{
    private const int FontPx = 16;
    private const float LineH = 20f;

    private static readonly Color Body = new(40, 42, 48, 255);
    private static readonly Color Well = new(26, 27, 31, 255);
    private static readonly Color Gutter = new(32, 33, 38, 255);
    private static readonly Color Line = new(18, 19, 22, 255);
    private static readonly Color CurrentLine = new(36, 38, 46, 255);
    private static readonly Color SelectionCol = new(52, 100, 170, 160);
    private static readonly Color Bad = new(240, 120, 120, 255);
    private static readonly Color Good = new(120, 215, 130, 255);
    private static readonly Color Warn = new(235, 200, 100, 255);

    private static readonly Color[] TokenColors =
    {
        new(224, 226, 230, 255),   // plain
        new(86, 156, 214, 255),    // keyword
        new(78, 201, 176, 255),    // type
        new(214, 157, 133, 255),   // string
        new(106, 153, 85, 255),    // comment
        new(181, 206, 168, 255),   // number
        new(180, 184, 192, 255),   // punctuation
    };

    private sealed class Doc
    {
        public string Path = "";
        public TextBuffer Buffer = new();
        public Token[][] Kinds = Array.Empty<Token[]>();
        public int KindsVersion = -1;
    }

    private static readonly Dictionary<string, Doc> Docs = new(StringComparer.OrdinalIgnoreCase);
    private static List<string> _files = new();
    private static Doc? _doc;
    private static string _newName = "";
    private static string _info = "";
    private static Color _infoColor = Good;
    private static string? _confirmDelete;
    private static bool _focus;
    private static float _scroll, _scrollX, _listScroll, _errScroll;
    private static float _cw = 9f;
    private static bool _followCaret;
    private static bool _selecting;
    private static double _lastClick;
    private static int _lastClickLine = -1;

    // completion popup
    private static CompletionResult? _comp;
    private static bool _compOpen;
    private static int _compSel, _compTop;
    private static Rectangle _compRect;
    private const int CompRows = 10;

    // the "Objects" panel: every scene, object and component of the project
    private sealed record ObjRow(int Depth, string Key, string Label, string Detail, string Insert, bool Expandable, bool Open, Color Col);
    private static bool _showObjects = true;
    private static float _objScroll;
    private static string _objFilter = "";
    private static readonly HashSet<string> OpenNodes = new();
    private static List<ObjRow> _objRows = new();
    private static double _objRowsAt;
    private static string _objRowsFilter = "\0";
    private static int _objRowsVersion = -1, _objVersion;
    private const float ObjPanelW = 280f;

    // debug log panel
    private static bool _showLog;
    private static float _logScroll;
    private static int _logSeen = -1;
    private static bool _logInfo = true, _logWarn = true, _logErr = true, _logFollow = true;
    private static LogEntry? _logSelected;

    public static bool IsOpen { get; private set; }

    /// <summary>Set by the editor: saves, compiles and applies the scripts. Returns a message and whether it worked.</summary>
    public static Func<(string Message, bool Ok)>? Compile { get; set; }

    public static void Open(string? file = null)
    {
        IsOpen = true;
        _info = ""; _confirmDelete = null; _selecting = false;
        Refresh();
        if (file is not null) OpenFile(file);
        else if (_doc is null && _files.Count > 0) OpenFile(_files[0]);
        _focus = true;
    }

    /// <summary>Opens Scripts/World/WorldCode.cs (created from a template the first time): code for the whole world.</summary>
    public static void OpenWorld()
    {
        try { Open(ScriptManager.EnsureWorldCode()); _showObjects = true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Open(); Info("Could not create the world code file: " + ex.Message, true); }
    }

    /// <summary>Opens the code file of a scene (created from a template the first time).</summary>
    public static void OpenScene(string sceneName)
    {
        try { Open(ScriptManager.EnsureSceneCode(sceneName)); _showObjects = true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Open(); Info("Could not create the scene code file: " + ex.Message, true); }
    }

    // ---- embedded use (the code pane of Manage Scenes) ---------------------------------------

    /// <summary>True while the suggestion list is open (a host window must not treat Esc as "close" then).</summary>
    public static bool CompletionOpen => _compOpen;

    /// <summary>True while the text has the keyboard focus.</summary>
    public static bool HasFocus => _focus;

    /// <summary>The file the editor has open.</summary>
    public static string? CurrentFile => _doc?.Path;

    /// <summary>True when the file has unsaved changes in the editor.</summary>
    public static bool IsDirty(string path) => Docs.TryGetValue(path, out var d) && d.Buffer.Dirty;

    public static void ReleaseFocus() { _focus = false; _compOpen = false; _selecting = false; }

    /// <summary>Opens a file for a host window that draws the editor itself with <see cref="DrawEmbedded"/>.</summary>
    public static void OpenEmbedded(string file)
    {
        _info = ""; _confirmDelete = null; _selecting = false;
        Refresh();
        OpenFile(file);
        _focus = true;
    }

    /// <summary>Draws the editor (text, errors or log, status line, suggestions) into <paramref name="area"/> of another window.</summary>
    public static void DrawEmbedded(Rectangle area)
    {
        try
        {
            // a click anywhere outside the editor takes the keyboard back, so typing next to it does not edit the code
            if (Gui.LeftPressed && !Rl.CheckCollisionPointRec(Gui.Mouse, area) && !(_compOpen && Rl.CheckCollisionPointRec(Gui.Mouse, _compRect))) ReleaseFocus();

            var diag = ScriptManager.Last;
            int diagCount = diag?.Diagnostics.Count ?? 0;
            float errH = _showLog ? 150f : diagCount > 0 || ScriptManager.RuntimeError is not null ? 110f : 0f;
            var status = new Rectangle(area.X, area.Y + area.Height - 26, area.Width, 26);
            float panelW = _showObjects && area.Width >= 900f ? ObjPanelW + 10f : 0f;   // the Objects panel only when there is room
            var errors = new Rectangle(area.X, status.Y - errH - (errH > 0 ? 6 : 0), area.Width - panelW, errH);
            var editor = new Rectangle(area.X, area.Y, area.Width - panelW, errors.Y - area.Y - 6);

            Gui.SetScope(7600);
            DrawEditor(editor);
            if (errH > 0) { if (_showLog) DrawLog(errors); else DrawErrors(errors); }
            if (panelW > 0f) DrawObjects(new Rectangle(editor.X + editor.Width + 10, area.Y, ObjPanelW, status.Y - 6 - area.Y));
            DrawStatus(status);
            DrawCompletion(editor);
        }
        catch (Exception ex) when (ErrorLog.IsRecoverable(ex))
        {
            ErrorLog.Report("code editor", ex);
            Rl.EndScissorMode();
            ReleaseFocus();
        }
    }

    public static void Close() { IsOpen = false; _focus = false; _compOpen = false; }

    private static void Refresh() => _files = ScriptManager.Files().ToList();

    private static bool IsWorldFile(string path) =>
        System.IO.Path.GetRelativePath(ScriptManager.Directory, path).Replace('\\', '/').StartsWith(ScriptManager.WorldFolderName + "/", StringComparison.OrdinalIgnoreCase);

    private static void Info(string text, bool error = false, bool warn = false)
    {
        _info = text;
        _infoColor = error ? Bad : warn ? Warn : Good;
    }

    // ---- files -------------------------------------------------------------------------------

    private static void OpenFile(string path)
    {
        if (!Docs.TryGetValue(path, out var d))
        {
            d = new Doc { Path = path };
            try { d.Buffer.Text = File.ReadAllText(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Info("Could not read the file: " + ex.Message, true); return; }
            Docs[path] = d;
        }
        else if (!d.Buffer.Dirty && File.Exists(path))
        {
            // pick up changes made outside the editor
            try { var disk = File.ReadAllText(path); if (disk.Replace("\r\n", "\n") != d.Buffer.Text) d.Buffer.Text = disk; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        _doc = d;
        _scroll = _scrollX = 0f;
        _confirmDelete = null;
    }

    private static bool Save(Doc d)
    {
        try
        {
            File.WriteAllText(d.Path, d.Buffer.Text);
            d.Buffer.MarkSaved();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Info("Could not save " + System.IO.Path.GetFileName(d.Path) + ": " + ex.Message, true);
            return false;
        }
    }

    private static void SaveAndCompile()
    {
        foreach (var d in Docs.Values.Where(d => d.Buffer.Dirty).ToList())
            if (!Save(d)) return;
        if (Compile is null) { Info("Saved.", false); return; }
        try
        {
            var (message, ok) = Compile();
            Info(message, !ok);
        }
        catch (Exception ex) when (ErrorLog.IsRecoverable(ex))
        {
            ErrorLog.Report("compiling", ex);
            Info("Compile failed: " + ErrorLog.Unwrap(ex).Message + " (see Logs/errors.log)", true);
        }
    }

    private static void CreateScript()
    {
        var clean = ScriptManager.SanitizeName(_newName);
        if (clean is null) { Info("Type a name first (letters, digits, underscore).", true); return; }
        try
        {
            var path = ScriptManager.CreateScript(clean);
            _newName = "";
            Refresh();
            OpenFile(path);
            _focus = true;
            Info($"Created {clean}.cs. Press Ctrl+S to compile it.");
        }
        catch (Exception ex) when (ErrorLog.IsRecoverable(ex)) { Info(ErrorLog.Unwrap(ex).Message, true); }
    }

    private static void CreateMeshScript()
    {
        var clean = ScriptManager.SanitizeName(_newName);
        if (clean is null) { Info("Type a name first (letters, digits, underscore).", true); return; }
        try
        {
            var path = ScriptManager.CreateMeshScript(clean);
            _newName = "";
            Refresh();
            OpenFile(path);
            _focus = true;
            Info($"Created {clean}.cs (mesh code). Press Ctrl+S to compile, then add it to an object.");
        }
        catch (Exception ex) when (ErrorLog.IsRecoverable(ex)) { Info(ErrorLog.Unwrap(ex).Message, true); }
    }

    private static void CreateEditorTool()
    {
        var clean = ScriptManager.SanitizeName(_newName);
        if (clean is null) { Info("Type a name first (letters, digits, underscore).", true); return; }
        try
        {
            var path = ScriptManager.CreateEditorScript(clean);
            _newName = "";
            Refresh();
            OpenFile(path);
            _focus = true;
            Info($"Created Editor/{clean}.cs. Press Ctrl+S: its buttons, menu commands and panels appear in the editor right away.");
        }
        catch (Exception ex) when (ErrorLog.IsRecoverable(ex)) { Info(ErrorLog.Unwrap(ex).Message, true); }
    }

    private static void CreateWorldScript()
    {
        var clean = ScriptManager.SanitizeName(_newName);
        if (clean is null) { Info("Type a name first (letters, digits, underscore).", true); return; }
        try
        {
            var path = ScriptManager.CreateScript(clean, world: true);
            _newName = "";
            Refresh();
            OpenFile(path);
            _focus = true;
            Info($"Created World/{clean}.cs. Press Ctrl+S to compile it.");
        }
        catch (Exception ex) when (ErrorLog.IsRecoverable(ex)) { Info(ErrorLog.Unwrap(ex).Message, true); }
    }

    private static void DeleteFile(string path)
    {
        try
        {
            File.Delete(path);
            Docs.Remove(path);
            if (_doc?.Path == path) _doc = null;
            Refresh();
            if (_doc is null && _files.Count > 0) OpenFile(_files[0]);
            Info("Deleted " + System.IO.Path.GetFileName(path) + ". Press Ctrl+S to compile without it.", warn: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Info(ex.Message, true); }
    }

    // ---- window ------------------------------------------------------------------------------

    public static void Draw(World world)
    {
        if (!IsOpen) return;
        bool editingAtStart = Gui.IsEditing;   // Esc that cancels typing in the name field must not also close the window
        try { DrawWindow(world, editingAtStart); }
        catch (Exception ex) when (ErrorLog.IsRecoverable(ex))
        {
            // a problem in the editor window closes only the window (unsaved text stays in memory and is kept when it is reopened)
            ErrorLog.Report("code editor", ex);
            Rl.EndScissorMode();
            IsOpen = false;
            _focus = false;
        }
    }

    private static void DrawWindow(World world, bool editingAtStart)
    {
        float sw = Gui.ScreenWidth, sh = Gui.ScreenHeight;
        float w = MathF.Min(sw - 30f, 1180f), h = MathF.Min(sh - 30f, 760f);
        var d = new Rectangle((sw - w) / 2f, (sh - h) / 2f, w, h);

        Rl.DrawRectangle(0, 0, (int)sw, (int)sh, new Color(0, 0, 0, 130));
        Rl.DrawRectangleRec(new Rectangle(d.X + 5, d.Y + 6, d.Width, d.Height), new Color(0, 0, 0, 100));
        Rl.DrawRectangleRec(d, Body);
        Rl.DrawRectangleLinesEx(d, 1f, Line);

        Rl.DrawRectangleRec(new Rectangle(d.X, d.Y, d.Width, 40), new Color(242, 242, 242, 255));
        string title = (_doc is not null && IsWorldFile(_doc.Path) ? "World Code" : "Code") + (_doc is not null ? "  -  " + System.IO.Path.GetFileName(_doc.Path) + (_doc.Buffer.Dirty ? " *" : "") : "");
        Gui.TextSized(d.X + 12, d.Y + 8, Gui.FitText(title, d.Width - 80), 26, new Color(100, 100, 100, 255));
        var close = new Rectangle(d.X + d.Width - 40, d.Y + 6, 28, 28);
        bool closeHover = Gui.IsHot(close);
        Rl.DrawRectangleRec(close, closeHover ? new Color(205, 205, 205, 255) : new Color(226, 226, 226, 255));
        var xc = new Color(150, 150, 150, 255);
        Rl.DrawLineEx(new Vector2(close.X + 8, close.Y + 8), new Vector2(close.X + 20, close.Y + 20), 2f, xc);
        Rl.DrawLineEx(new Vector2(close.X + 20, close.Y + 8), new Vector2(close.X + 8, close.Y + 20), 2f, xc);
        if (closeHover && Gui.LeftPressed) Close();

        float listW = 210f;
        var list = new Rectangle(d.X + 10, d.Y + 50, listW, d.Height - 60);
        var diag = ScriptManager.Last;
        int diagCount = diag?.Diagnostics.Count ?? 0;
        float errH = _showLog ? 190f : diagCount > 0 || ScriptManager.RuntimeError is not null ? 128f : 0f;
        var status = new Rectangle(d.X + 10 + listW + 10, d.Y + d.Height - 34, d.Width - listW - 30, 26);
        float panelW = _showObjects ? ObjPanelW + 10f : 0f;
        var errors = new Rectangle(status.X, status.Y - errH - (errH > 0 ? 6 : 0), status.Width - panelW, errH);
        var editor = new Rectangle(status.X, d.Y + 50, status.Width - panelW, errors.Y - (d.Y + 50) - (errH > 0 ? 6 : 6));
        bool compAtStart = _compOpen;

        DrawList(list);
        DrawEditor(editor);
        if (errH > 0) { if (_showLog) DrawLog(errors); else DrawErrors(errors); }
        if (_showObjects) DrawObjects(new Rectangle(editor.X + editor.Width + 10, d.Y + 50, ObjPanelW, status.Y - 6 - (d.Y + 50)));
        DrawStatus(status);
        DrawCompletion(editor);

        // keyboard
        if (Rl.IsKeyPressed(KeyboardKey.Escape) && !Gui.PopupOpen && !editingAtStart && !Gui.IsEditing && !compAtStart) Close();
    }

    // ---- file list ---------------------------------------------------------------------------

    private static void DrawList(Rectangle r)
    {
        Rl.DrawRectangleRec(r, Well);
        Rl.DrawRectangleLinesEx(r, 1f, Line);
        Gui.SetScope(7600);
        Gui.TextSized(r.X + 10, r.Y + 8, "Scripts", 18, Color.White, bold: true);

        var view = new Rectangle(r.X + 1, r.Y + 34, r.Width - 2, r.Height - 34 - 156);
        Gui.BeginScroll(view, ref _listScroll, _files.Count * 24f + 6);
        float y = view.Y - _listScroll + 2;
        if (_files.Count == 0)
            Gui.Text(view.X + 10, y + 4, "No scripts yet.", Gui.TextDim);
        foreach (var f in _files.ToList())
        {
            var row = new Rectangle(view.X + 3, y, view.Width - 10, 22);
            bool sel = _doc?.Path == f;
            bool hot = Gui.IsHot(row);
            Rl.DrawRectangleRec(row, sel ? Gui.Accent : hot ? Gui.Hover : Well);
            bool dirty = Docs.TryGetValue(f, out var dd) && dd.Buffer.Dirty;
            var name = System.IO.Path.GetRelativePath(ScriptManager.Directory, f);
            Gui.TextLeft(row, name + (dirty ? " *" : ""), Color.White);
            if (hot && Gui.LeftPressed && !Gui.Busy) { OpenFile(f); _focus = true; }
            y += 24;
        }
        Gui.EndScroll(view);

        float by = r.Y + r.Height - 180;
        Gui.SetScope(7601);
        Gui.Text(r.X + 10, by, "New script", Gui.TextDim);
        Gui.TextField(new Rectangle(r.X + 10, by + 20, r.Width - 20, 24), "newname", ref _newName, "Name, e.g. Spin");
        if (Gui.Button(new Rectangle(r.X + 10, by + 50, r.Width - 20, 26), "Create Script")) CreateScript();
        if (Gui.Button(new Rectangle(r.X + 10, by + 80, r.Width - 20, 26), "Create World Script")) CreateWorldScript();
        if (Gui.Button(new Rectangle(r.X + 10, by + 110, r.Width - 20, 26), "Create Mesh Script")) CreateMeshScript();
        if (Gui.Button(new Rectangle(r.X + 10, by + 140, r.Width - 20, 26), "Create Editor Tool")) CreateEditorTool();
        if (_doc is not null)
        {
            bool confirm = _confirmDelete == _doc.Path;
            if (Gui.Button(new Rectangle(r.X + 10, by - 30, r.Width - 20, 24), confirm ? "Click again to delete" : "Delete This Script"))
            {
                if (confirm) DeleteFile(_doc.Path); else _confirmDelete = _doc.Path;
            }
        }
    }

    // ---- the text ----------------------------------------------------------------------------

    private static void EnsureKinds(Doc d)
    {
        if (d.KindsVersion == d.Buffer.Version && d.Kinds.Length == d.Buffer.LineCount) return;
        d.Kinds = new Token[d.Buffer.LineCount][];
        bool block = false;
        for (int i = 0; i < d.Buffer.LineCount; i++)
        {
            var line = d.Buffer.GetLine(i);
            d.Kinds[i] = new Token[line.Length];
            block = CSharpHighlighter.Classify(line, block, d.Kinds[i]);
        }
        d.KindsVersion = d.Buffer.Version;
    }

    private static void DrawEditor(Rectangle r)
    {
        Rl.DrawRectangleRec(r, Well);
        Rl.DrawRectangleLinesEx(r, 1f, Line);
        _cw = MathF.Max(7f, MathF.Ceiling(Rl.MeasureText("0123456789abcdefghijklmnopqrstuvwxyz", FontPx) / 36f));

        if (_doc is null)
        {
            Gui.Text(r.X + 20, r.Y + 20, "Create a script on the left, or pick one.", Gui.TextDim);
            Gui.Text(r.X + 20, r.Y + 46, "A script is a C# class that derives from Component. After Ctrl+S it appears in Add Component", Gui.TextDim);
            Gui.Text(r.X + 20, r.Y + 68, "and can be added to an object or pinned to a scene.", Gui.TextDim);
            return;
        }

        var d = _doc;
        var buf = d.Buffer;
        EnsureKinds(d);

        int digits = Math.Max(3, buf.LineCount.ToString().Length);
        float gutterW = digits * _cw + 18f;
        var textArea = new Rectangle(r.X + gutterW, r.Y + 1, r.Width - gutterW - 1, r.Height - 2);
        bool hover = Gui.IsHot(textArea);

        HandleInput(buf, textArea, hover);
        EnsureKinds(d);   // the input above may have changed the text

        // keep the caret on screen after it moved
        if (_followCaret)
        {
            float cy = buf.Line * LineH, cx = buf.Col * _cw;
            if (cy < _scroll) _scroll = cy;
            if (cy + LineH > _scroll + textArea.Height - 8) _scroll = cy + LineH - textArea.Height + 8;
            if (cx < _scrollX) _scrollX = MathF.Max(0, cx - 40);
            if (cx + 24 > _scrollX + textArea.Width - 20) _scrollX = cx + 24 - textArea.Width + 20;
            _followCaret = false;
        }

        // gutter
        Rl.DrawRectangleRec(new Rectangle(r.X + 1, r.Y + 1, gutterW, r.Height - 2), Gutter);

        float contentH = buf.LineCount * LineH + 24f;
        Gui.BeginScroll(textArea, ref _scroll, contentH);

        var errLines = new HashSet<int>();
        if (ScriptManager.Last is { } last)
            foreach (var e in last.Diagnostics)
                if (e.IsError && string.Equals(Path.GetFileName(d.Path), e.File, StringComparison.OrdinalIgnoreCase)) errLines.Add(e.Line - 1);

        int first = Math.Max(0, (int)(_scroll / LineH));
        int lastLine = Math.Min(buf.LineCount - 1, first + (int)(textArea.Height / LineH) + 1);
        var (ss, se) = buf.Selection;
        for (int i = first; i <= lastLine; i++)
        {
            float y = textArea.Y - _scroll + i * LineH + 2;
            float x0 = textArea.X + 6 - _scrollX;
            var line = buf.GetLine(i);

            if (i == buf.Line && !buf.HasSelection) Rl.DrawRectangle((int)textArea.X, (int)y - 2, (int)textArea.Width, (int)LineH, CurrentLine);
            if (errLines.Contains(i)) Rl.DrawRectangle((int)textArea.X, (int)y - 2, (int)textArea.Width, (int)LineH, new Color(120, 40, 40, 70));

            if (buf.HasSelection && i >= ss.Line && i <= se.Line)
            {
                int a = i == ss.Line ? ss.Col : 0;
                int b = i == se.Line ? se.Col : line.Length + 1;   // +1 shows the selected line break
                Rl.DrawRectangle((int)(x0 + a * _cw), (int)y - 2, (int)MathF.Max(2f, (b - a) * _cw), (int)LineH, SelectionCol);
            }

            var kinds = d.Kinds[i];
            var font = Rl.GetFontDefault();
            int from = Math.Max(0, (int)(_scrollX / _cw) - 1);
            int to = Math.Min(line.Length, from + (int)(textArea.Width / _cw) + 3);
            for (int c = from; c < to; c++)
            {
                char ch = line[c];
                if (ch == ' ') continue;
                Rl.DrawTextCodepoint(font, ch, new Vector2(x0 + c * _cw, y), FontPx, TokenColors[c < kinds.Length ? (int)kinds[c] : 0]);
            }
        }

        if (_focus && !Gui.IsEditing && (int)(Rl.GetTime() * 2) % 2 == 0)
        {
            float cx = textArea.X + 6 - _scrollX + buf.Col * _cw;
            float cy = textArea.Y - _scroll + buf.Line * LineH + 1;
            Rl.DrawRectangle((int)cx, (int)cy, 2, (int)LineH - 2, Color.White);
        }
        Gui.EndScroll(textArea);

        // line numbers (drawn after the text so they are not clipped by the text area)
        Rl.BeginScissorMode((int)((r.X + 1) * Gui.EffectiveScale), (int)((r.Y + 1) * Gui.EffectiveScale), (int)(gutterW * Gui.EffectiveScale), (int)((r.Height - 2) * Gui.EffectiveScale));
        for (int i = first; i <= lastLine; i++)
        {
            float y = textArea.Y - _scroll + i * LineH + 2;
            string n = (i + 1).ToString();
            Gui.TextSized(r.X + gutterW - 8 - Rl.MeasureText(n, FontPx), y, n, FontPx, errLines.Contains(i) ? Bad : i == buf.Line ? Color.White : Gui.TextDim);
            if (errLines.Contains(i)) Rl.DrawCircle((int)(r.X + 8), (int)(y + 8), 3f, Bad);
        }
        Rl.EndScissorMode();
    }

    private static (int Line, int Col) PointToCaret(TextBuffer buf, Rectangle area)
    {
        int line = Math.Clamp((int)((Gui.Mouse.Y - area.Y + _scroll - 2) / LineH), 0, buf.LineCount - 1);
        int col = Math.Max(0, (int)MathF.Round((Gui.Mouse.X - area.X - 6 + _scrollX) / _cw));
        return (line, Math.Min(col, buf.GetLine(line).Length));
    }

    private static bool Pressed(KeyboardKey k) => Rl.IsKeyPressed(k) || Rl.IsKeyPressedRepeat(k);

    private static void HandleInput(TextBuffer buf, Rectangle area, bool hover)
    {
        // a click on the suggestion list picks that suggestion
        if (_compOpen && _comp is not null && Gui.LeftPressed && Rl.CheckCollisionPointRec(Gui.Mouse, _compRect))
        {
            int row = (int)((Gui.Mouse.Y - _compRect.Y - 2) / LineH);
            if (row >= 0 && _compTop + row < _comp.Items.Count) { _compSel = _compTop + row; AcceptCompletion(buf); _followCaret = true; }
            return;
        }
        if (_compOpen && Gui.LeftPressed) _compOpen = false;

        // mouse: click places the caret, drag selects, double click selects a word
        if (hover && Gui.LeftPressed && !Gui.Busy)
        {
            _focus = true;
            var (l, c) = PointToCaret(buf, area);
            double now = Rl.GetTime();
            if (now - _lastClick < 0.35 && l == _lastClickLine) buf.SelectWordAt(l, c);
            else buf.MoveTo(l, c, Rl.IsKeyDown(KeyboardKey.LeftShift));
            _lastClick = now; _lastClickLine = l;
            _selecting = true;
            _followCaret = true;
        }
        else if (Gui.LeftPressed && !hover && !Gui.IsHot(area)) { if (!Gui.IsEditing) { /* a click elsewhere keeps focus off the text */ } }

        if (_selecting)
        {
            if (Gui.LeftDown)
            {
                var (l, c) = PointToCaret(buf, area);
                if (l != buf.Line || c != buf.Col) { buf.MoveTo(l, c, select: true); _followCaret = true; }
            }
            else _selecting = false;
        }

        if (hover && Rl.IsKeyDown(KeyboardKey.LeftShift)) _scrollX = MathF.Max(0f, _scrollX - Rl.GetMouseWheelMove() * 60f);

        if (!_focus || Gui.IsEditing || Gui.PopupOpen) return;

        bool ctrl = Rl.IsKeyDown(KeyboardKey.LeftControl) || Rl.IsKeyDown(KeyboardKey.RightControl);
        bool shift = Rl.IsKeyDown(KeyboardKey.LeftShift) || Rl.IsKeyDown(KeyboardKey.RightShift);
        bool alt = Rl.IsKeyDown(KeyboardKey.LeftAlt) || Rl.IsKeyDown(KeyboardKey.RightAlt);
        int rows = Math.Max(1, (int)(area.Height / LineH) - 1);
        bool moved = false;

        // while the suggestion list is open it takes the arrow keys, Enter, Tab and Esc
        if (_compOpen && _comp is not null)
        {
            int n = _comp.Items.Count;
            if (Pressed(KeyboardKey.Down)) { _compSel = Math.Min(n - 1, _compSel + 1); KeepSelectionVisible(); return; }
            if (Pressed(KeyboardKey.Up)) { _compSel = Math.Max(0, _compSel - 1); KeepSelectionVisible(); return; }
            if (Pressed(KeyboardKey.PageDown)) { _compSel = Math.Min(n - 1, _compSel + CompRows); KeepSelectionVisible(); return; }
            if (Pressed(KeyboardKey.PageUp)) { _compSel = Math.Max(0, _compSel - CompRows); KeepSelectionVisible(); return; }
            if (Rl.IsKeyPressed(KeyboardKey.Enter) || Rl.IsKeyPressed(KeyboardKey.KpEnter) || Rl.IsKeyPressed(KeyboardKey.Tab)) { AcceptCompletion(buf); _followCaret = true; return; }
            if (Rl.IsKeyPressed(KeyboardKey.Escape)) { _compOpen = false; return; }
        }
        bool compWasOpen = _compOpen;
        bool keepComp = false;

        if (ctrl && Rl.IsKeyPressed(KeyboardKey.Space)) { RequestCompletion(buf, explicitRequest: true); keepComp = true; }
        else if (ctrl && Rl.IsKeyPressed(KeyboardKey.S)) { SaveAndCompile(); return; }
        else if (ctrl && Rl.IsKeyPressed(KeyboardKey.A)) { buf.SelectAll(); moved = true; }
        else if (ctrl && Rl.IsKeyPressed(KeyboardKey.C)) Rl.SetClipboardText(buf.Copy());
        else if (ctrl && Rl.IsKeyPressed(KeyboardKey.X)) { Rl.SetClipboardText(buf.Cut()); moved = true; }
        else if (ctrl && Rl.IsKeyPressed(KeyboardKey.V)) { buf.Insert(Rl.GetClipboardText_() ?? "", typing: false); moved = true; }
        else if (ctrl && (Pressed(KeyboardKey.Z) && !shift)) { buf.Undo(); moved = true; }
        else if (ctrl && (Pressed(KeyboardKey.Y) || (Pressed(KeyboardKey.Z) && shift))) { buf.Redo(); moved = true; }
        else if (ctrl && Rl.IsKeyPressed(KeyboardKey.Slash)) { buf.ToggleComment(); moved = true; }
        else if (ctrl && Rl.IsKeyPressed(KeyboardKey.D)) { buf.DuplicateLine(); moved = true; }
        else if (alt && Pressed(KeyboardKey.Up)) { buf.MoveLine(-1); moved = true; }
        else if (alt && Pressed(KeyboardKey.Down)) { buf.MoveLine(1); moved = true; }
        else if (Pressed(KeyboardKey.Left)) { buf.Left(shift, ctrl); moved = true; }
        else if (Pressed(KeyboardKey.Right)) { buf.Right(shift, ctrl); moved = true; }
        else if (Pressed(KeyboardKey.Up)) { buf.Up(shift); moved = true; }
        else if (Pressed(KeyboardKey.Down)) { buf.Down(shift); moved = true; }
        else if (Pressed(KeyboardKey.PageUp)) { buf.Up(shift, rows); moved = true; }
        else if (Pressed(KeyboardKey.PageDown)) { buf.Down(shift, rows); moved = true; }
        else if (Rl.IsKeyPressed(KeyboardKey.Home)) { if (ctrl) buf.DocumentStart(shift); else buf.Home(shift); moved = true; }
        else if (Rl.IsKeyPressed(KeyboardKey.End)) { if (ctrl) buf.DocumentEnd(shift); else buf.End(shift); moved = true; }
        else if (Pressed(KeyboardKey.Backspace)) { buf.Backspace(ctrl); moved = true; if (compWasOpen) { RequestCompletion(buf, false); keepComp = true; } }
        else if (Pressed(KeyboardKey.Delete)) { buf.Delete(ctrl); moved = true; }
        else if (Pressed(KeyboardKey.Enter) || Pressed(KeyboardKey.KpEnter)) { buf.NewLine(); moved = true; }
        else if (Pressed(KeyboardKey.Tab)) { if (shift) buf.Outdent(); else buf.Indent(); moved = true; }

        int ch;
        while ((ch = Rl.GetCharPressed()) > 0)
        {
            if (ctrl || ch < 32 || ch == 127) continue;
            buf.Type((char)ch);
            moved = true;
            char typed = (char)ch;
            if (typed is '.' or '"' or '<') { RequestCompletion(buf, false); keepComp = _compOpen; }
            else if ((char.IsLetterOrDigit(typed) || typed == '_') && compWasOpen) { RequestCompletion(buf, false); keepComp = _compOpen; }
            else if (compWasOpen) keepComp = false;
        }
        if (compWasOpen && moved && !keepComp) _compOpen = false;
        if (moved) _followCaret = true;
    }

    // ---- completion ---------------------------------------------------------------------------

    private static void RequestCompletion(TextBuffer buf, bool explicitRequest)
    {
        var r = CompletionEngine.Complete(buf.Text, buf.Line, buf.Col, explicitRequest);
        if (r is null) { _compOpen = false; return; }
        _comp = r;
        _compOpen = true;
        _compSel = 0;
        _compTop = 0;
    }

    private static void KeepSelectionVisible()
    {
        if (_compSel < _compTop) _compTop = _compSel;
        if (_compSel >= _compTop + CompRows) _compTop = _compSel - CompRows + 1;
    }

    private static void AcceptCompletion(TextBuffer buf)
    {
        _compOpen = false;
        if (_comp is null || _compSel < 0 || _compSel >= _comp.Items.Count) return;
        var item = _comp.Items[_compSel];
        int end = buf.Col;
        buf.MoveTo(buf.Line, Math.Min(_comp.ReplaceStart, end), false);
        buf.MoveTo(buf.Line, end, true);
        string insert = item.Insert;
        if (_comp.InString)
        {
            var line = buf.GetLine(buf.Line);
            if (end >= line.Length || line[end] != '"') insert += "\"";
        }
        buf.Insert(insert, typing: false);
        _focus = true;
    }

    private static Color KindColor(CompletionKind k) => k switch
    {
        CompletionKind.Method => new Color(220, 190, 120, 255),
        CompletionKind.Property or CompletionKind.Field => new Color(150, 200, 255, 255),
        CompletionKind.Type or CompletionKind.Component => new Color(78, 201, 176, 255),
        CompletionKind.Variable => new Color(190, 220, 255, 255),
        CompletionKind.Object => new Color(240, 160, 120, 255),
        CompletionKind.Scene or CompletionKind.Group => new Color(200, 140, 255, 255),
        _ => new Color(86, 156, 214, 255)
    };

    private static string KindLetter(CompletionKind k) => k switch
    {
        CompletionKind.Method => "M", CompletionKind.Property => "P", CompletionKind.Field => "F", CompletionKind.Type => "T", CompletionKind.Component => "C",
        CompletionKind.Variable => "v", CompletionKind.Object => "O", CompletionKind.Scene => "S", CompletionKind.Group => "G", _ => "k"
    };

    private static void DrawCompletion(Rectangle editor)
    {
        if (!_compOpen || _comp is null || _doc is null || _comp.Items.Count == 0) { _compRect = default; return; }
        var buf = _doc.Buffer;
        int rows = Math.Min(CompRows, _comp.Items.Count);
        float w = 470f, h = rows * LineH + 6f;
        int digits = Math.Max(3, buf.LineCount.ToString().Length);
        float gutterW = digits * _cw + 18f;
        float x = editor.X + gutterW + 6 - _scrollX + _comp.ReplaceStart * _cw;
        float y = editor.Y + 1 - _scroll + (buf.Line + 1) * LineH + 2;
        if (y + h > editor.Y + editor.Height) y = editor.Y + 1 - _scroll + buf.Line * LineH - h;   // no room below: above the line
        x = Math.Clamp(x, 4f, Gui.ScreenWidth - w - 4f);
        y = Math.Clamp(y, 4f, Gui.ScreenHeight - h - 4f);
        _compRect = new Rectangle(x, y, w, h);

        Rl.DrawRectangleRec(new Rectangle(x + 3, y + 3, w, h), new Color(0, 0, 0, 90));
        Rl.DrawRectangleRec(_compRect, new Color(30, 31, 36, 250));
        Rl.DrawRectangleLinesEx(_compRect, 1f, new Color(90, 94, 108, 255));
        float wheel = Rl.GetMouseWheelMove();
        if (wheel != 0 && Rl.CheckCollisionPointRec(Gui.Mouse, _compRect))
            _compTop = Math.Clamp(_compTop - (int)MathF.Sign(wheel) * 3, 0, Math.Max(0, _comp.Items.Count - CompRows));
        _compTop = Math.Clamp(_compTop, 0, Math.Max(0, _comp.Items.Count - rows));

        for (int i = 0; i < rows; i++)
        {
            int index = _compTop + i;
            var item = _comp.Items[index];
            var row = new Rectangle(x + 2, y + 3 + i * LineH, w - 4, LineH);
            bool sel = index == _compSel;
            bool hot = Rl.CheckCollisionPointRec(Gui.Mouse, row);
            if (sel || hot) Rl.DrawRectangleRec(row, sel ? Gui.Accent : Gui.Hover);
            var kc = KindColor(item.Kind);
            Rl.DrawText(KindLetter(item.Kind), (int)row.X + 5, (int)row.Y + 2, 14, kc);
            string name = Gui.FitText(item.Text, 190);
            Rl.DrawText(name, (int)row.X + 22, (int)row.Y + 2, 16, Color.White);
            string detail = Gui.FitText(item.Detail, row.Width - 22 - Math.Max(110, Rl.MeasureText(name, 16) + 14) - 6);
            Rl.DrawText(detail, (int)(row.X + row.Width - 6 - Rl.MeasureText(detail, 14)), (int)row.Y + 3, 14, sel ? new Color(225, 232, 245, 255) : Gui.TextDim);
        }
        if (_comp.Items.Count > rows)
            Rl.DrawText($"{_compSel + 1}/{_comp.Items.Count}", (int)(x + w - 60), (int)(y + h + 2), 12, Gui.TextDim);
    }

    // ---- the Objects panel --------------------------------------------------------------------

    private static bool Matches(string text) => _objFilter.Length == 0 || text.Contains(_objFilter, StringComparison.OrdinalIgnoreCase);

    private static void BuildObjectRows()
    {
        var rows = new List<ObjRow>();
        bool filtering = _objFilter.Length > 0;
        var sceneCol = new Color(200, 140, 255, 255);
        var objCol = new Color(240, 170, 130, 255);
        var compCol = new Color(78, 201, 176, 255);
        var propCol = new Color(170, 175, 190, 255);

        foreach (var sc in WorldIndex.Scenes)
        {
            var objRows = new List<ObjRow>();
            for (int oi = 0; oi < sc.Objects.Count; oi++)
            {
                var o = sc.Objects[oi];
                string okey = $"O:{sc.Name}/{oi}";
                var compRows = new List<ObjRow>();
                bool anyMatch = Matches(o.Name);
                foreach (var c in o.Components)
                {
                    string ckey = okey + "/" + c.Type;
                    bool cmatch = Matches(c.Type);
                    anyMatch |= cmatch;
                    bool copen = OpenNodes.Contains(ckey) || filtering && cmatch;
                    compRows.Add(new ObjRow(2, ckey, c.Type, c.Properties.Count > 0 ? c.Properties.Count + " props" : "", c.Type, c.Properties.Count > 0, copen, compCol));
                    if (copen)
                        foreach (var (k, v) in c.Properties)
                            compRows.Add(new ObjRow(3, ckey + "." + k, k, "= " + v, k, false, false, propCol));
                }
                if (filtering && !anyMatch) continue;
                bool oopen = OpenNodes.Contains(okey) || filtering;
                objRows.Add(new ObjRow(1, okey, o.Name, o.Components.Count + " comp.", o.Name, o.Components.Count > 0, oopen, o.Active ? objCol : objCol with { A = 140 }));
                if (oopen) objRows.AddRange(compRows);
            }
            if (filtering && objRows.Count == 0 && !Matches(sc.Name)) continue;
            string skey = "S:" + sc.Name;
            bool sopen = OpenNodes.Contains(skey) || filtering && objRows.Count > 0;
            rows.Add(new ObjRow(0, skey, sc.Name, $"{sc.Objects.Count} obj." + (sc.Group is not null ? " [" + sc.Group + "]" : ""), sc.Name, sc.Objects.Count > 0, sopen, sceneCol));
            if (sopen) rows.AddRange(objRows);
        }
        _objRows = rows;
        _objRowsAt = Rl.GetTime();
        _objRowsFilter = _objFilter;
        _objRowsVersion = _objVersion;
    }

    private static void InsertName(string name, int depth)
    {
        if (_doc is null) { Info("Open a script first.", warn: true); return; }
        var buf = _doc.Buffer;
        string before = buf.GetLine(buf.Line)[..Math.Min(buf.Col, buf.GetLine(buf.Line).Length)];
        // scenes and objects are written as text ("Level2"); components, properties are plain names
        string text = depth <= 1 && !CompletionEngine.InsideString(before) ? "\"" + name + "\"" : name;
        buf.Insert(text, typing: false);
        _focus = true; _followCaret = true;
    }

    private static void DrawObjects(Rectangle r)
    {
        Rl.DrawRectangleRec(r, Well);
        Rl.DrawRectangleLinesEx(r, 1f, Line);
        Gui.SetScope(7640);
        Gui.TextSized(r.X + 10, r.Y + 8, "Objects", 18, Color.White, bold: true);
        int sceneCount = WorldIndex.Scenes.Count;
        Gui.TextSized(r.X + 90, r.Y + 11, $"{sceneCount} scene(s)", 14, Gui.TextDim);
        Gui.TextField(new Rectangle(r.X + 8, r.Y + 34, r.Width - 16, 24), "objfilter", ref _objFilter, "Filter objects, components ...");

        if (_objRowsVersion != _objVersion || _objRowsFilter != _objFilter || Rl.GetTime() - _objRowsAt > 1.0) BuildObjectRows();

        var view = new Rectangle(r.X + 1, r.Y + 64, r.Width - 2, r.Height - 64 - 40);
        Gui.BeginScroll(view, ref _objScroll, _objRows.Count * 22f + 6);
        float y = view.Y - _objScroll + 2;
        foreach (var row in _objRows.ToList())
        {
            var rr = new Rectangle(view.X + 3, y, view.Width - 10, 21);
            if (rr.Y + rr.Height >= view.Y && rr.Y <= view.Y + view.Height)
            {
                bool hot = Gui.IsHot(rr);
                if (hot) Rl.DrawRectangleRec(rr, Gui.Hover);
                float ix = rr.X + 4 + row.Depth * 14;
                if (row.Expandable)
                {
                    var arrow = new Rectangle(ix - 2, rr.Y, 16, rr.Height);
                    string a = row.Open ? "v" : ">";
                    Rl.DrawText(a, (int)ix, (int)rr.Y + 3, 14, Gui.TextDim);
                    if (Gui.IsHot(arrow) && Gui.LeftPressed && !Gui.Busy)
                    {
                        if (!OpenNodes.Remove(row.Key)) OpenNodes.Add(row.Key);
                        _objVersion++;
                    }
                    else if (hot && Gui.LeftPressed && !Gui.Busy) InsertName(row.Insert, row.Depth);
                }
                else if (hot && Gui.LeftPressed && !Gui.Busy) InsertName(row.Insert, row.Depth);

                string label = Gui.FitText(row.Label, rr.Width - 24 - row.Depth * 14 - 80);
                Rl.DrawText(label, (int)ix + 16, (int)rr.Y + 3, 15, row.Col);
                string detail = Gui.FitText(row.Detail, 100);
                Rl.DrawText(detail, (int)(rr.X + rr.Width - 6 - Rl.MeasureText(detail, 13)), (int)rr.Y + 4, 13, Gui.TextDim);
            }
            y += 22;
        }
        if (_objRows.Count == 0) Gui.Text(view.X + 10, view.Y + 8, _objFilter.Length > 0 ? "Nothing matches." : "No scenes yet.", Gui.TextDim);
        Gui.EndScroll(view);

        Gui.TextSized(r.X + 10, r.Y + r.Height - 34, Gui.FitText("Click a name to insert it at the cursor.", r.Width - 20), 13, Gui.TextDim);
        Gui.TextSized(r.X + 10, r.Y + r.Height - 18, Gui.FitText("Ctrl+Space or . for suggestions.", r.Width - 20), 13, Gui.TextDim);
    }

    // ---- exporting the log -------------------------------------------------------------------

    private static List<LogEntry> ShownEntries() =>
        DebugLog.Snapshot().Where(e => e.Level switch { LogLevel.Info => _logInfo, LogLevel.Warning => _logWarn, _ => _logErr }).ToList();

    /// <summary>Puts a readable report on the clipboard (only the lines the filters show, or everything).</summary>
    private static void CopyReport(bool onlyShown)
    {
        try
        {
            Rl.SetClipboardText(LogExport.ForClipboardOrFile(onlyShown ? ShownEntries() : null));
            Info("Log copied to the clipboard - paste it into any editor or chat.");
        }
        catch (Exception ex) when (ErrorLog.IsRecoverable(ex)) { Info("Could not copy: " + ex.Message, true); }
    }

    /// <summary>Saves the report to Logs/log-date-time.txt (and Logs/latest.txt).</summary>
    private static void SaveReport()
    {
        try
        {
            string path = LogExport.Save();
            Info("Log saved: " + Path.GetRelativePath(GameProject.Root, path) + " (also Logs/latest.txt)");
        }
        catch (Exception ex) when (ErrorLog.IsRecoverable(ex)) { Info("Could not save the log: " + ex.Message, true); }
    }

    // ---- errors and status -------------------------------------------------------------------

    private static void DrawErrors(Rectangle r)
    {
        Rl.DrawRectangleRec(r, Well);
        Rl.DrawRectangleLinesEx(r, 1f, Line);
        var items = new List<(string Text, Color Col, ScriptDiagnostic? Diag)>();
        if (ScriptManager.RuntimeError is { } rt) items.Add(("Runtime: " + rt, Bad, null));
        if (ScriptManager.Last is { } last)
            foreach (var d in last.Diagnostics.OrderByDescending(x => x.IsError))
                items.Add(($"{d.File}({d.Line},{d.Column})  {d.Id}: {d.Message}", d.IsError ? Bad : Warn, d));

        Gui.SetScope(7610);
        var view = new Rectangle(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2);
        if (items.Count > 0)
        {
            view.Width -= 132;     // room for the export buttons on the right
            if (Gui.Button(new Rectangle(r.X + r.Width - 128, r.Y + 4, 60, 22), "Copy")) CopyReport(onlyShown: false);
            if (Gui.Button(new Rectangle(r.X + r.Width - 64, r.Y + 4, 58, 22), "Save")) SaveReport();
        }
        Gui.BeginScroll(view, ref _errScroll, items.Count * 22f + 6);
        float y = view.Y - _errScroll + 3;
        foreach (var (text, col, diag) in items)
        {
            var row = new Rectangle(view.X + 3, y, view.Width - 12, 21);
            bool hot = Gui.IsHot(row);
            if (hot) Rl.DrawRectangleRec(row, Gui.Hover);
            Gui.TextLeft(row, text, col);
            if (hot && diag is not null && Gui.LeftPressed && !Gui.Busy) JumpTo(diag);
            y += 22;
        }
        Gui.EndScroll(view);
    }

    // ---- debug log ---------------------------------------------------------------------------

    private static void DrawLog(Rectangle r)
    {
        Rl.DrawRectangleRec(r, Well);
        Rl.DrawRectangleLinesEx(r, 1f, Line);
        Gui.SetScope(7630);

        // header: level filters, clear, follow
        float hx = r.X + 6, hy = r.Y + 4;
        bool Toggle(string label, ref bool on, int count)
        {
            var rect = new Rectangle(hx, hy, Rl.MeasureText($"{label} {count}", Gui.FontSize) + 18, 22);
            bool clicked = Gui.Button(rect, $"{label} {count}", selected: on);
            hx += rect.Width + 6;
            if (clicked) on = !on;
            return clicked;
        }
        Toggle("Info", ref _logInfo, DebugLog.Infos);
        Toggle("Warnings", ref _logWarn, DebugLog.Warnings);
        Toggle("Errors", ref _logErr, DebugLog.Errors);
        if (Gui.Button(new Rectangle(hx, hy, 64, 22), "Clear")) { DebugLog.Clear(); _logSelected = null; _logScroll = 0; }
        hx += 70;
        if (Gui.Button(new Rectangle(hx, hy, 58, 22), "Copy")) CopyReport(onlyShown: true);
        hx += 64;
        if (Gui.Button(new Rectangle(hx, hy, 58, 22), "Save")) SaveReport();
        hx += 64;
        if (Gui.Button(new Rectangle(hx, hy, 84, 22), _logFollow ? "Follow: on" : "Follow: off", selected: _logFollow)) _logFollow = !_logFollow;
        hx += 90;
        Gui.Text(hx, hy + 3, Gui.FitText("Debug.Log / LogWarning / LogError or Console.WriteLine in a script. Click a line for details.", r.X + r.Width - hx - 10), Gui.TextDim);

        var entries = DebugLog.Snapshot().Where(e => e.Level switch { LogLevel.Info => _logInfo, LogLevel.Warning => _logWarn, _ => _logErr }).ToList();
        var rows = new List<(string Text, Color Col, LogEntry? Entry, bool Sub)>();
        foreach (var e in entries)
        {
            var col = e.Level == LogLevel.Error ? Bad : e.Level == LogLevel.Warning ? Warn : new Color(215, 218, 224, 255);
            string line = $"{e.Time:HH:mm:ss}  [{e.Source}]  {e.Text}" + (e.Count > 1 ? $"   x{e.Count}" : "");
            rows.Add((line, col, e, false));
            if (ReferenceEquals(e, _logSelected) && e.Detail is { Length: > 0 } detail)
                foreach (var d in detail.Split((char)10))
                    rows.Add(("      " + d.Trim(), Gui.TextDim, e, true));
        }

        var view = new Rectangle(r.X + 1, r.Y + 30, r.Width - 2, r.Height - 31);
        float content = rows.Count * 20f + 6;
        if (DebugLog.Version != _logSeen)
        {
            _logSeen = DebugLog.Version;
            if (_logFollow) _logScroll = content;   // BeginScroll clamps this to the end
        }
        Gui.BeginScroll(view, ref _logScroll, content);
        float y = view.Y - _logScroll + 3;
        if (rows.Count == 0) Gui.Text(view.X + 10, y + 2, "Nothing logged yet.", Gui.TextDim);
        foreach (var (text, col, entry, sub) in rows)
        {
            var row = new Rectangle(view.X + 3, y, view.Width - 12, 19);
            if (y + 19 >= view.Y && y <= view.Y + view.Height)
            {
                bool hot = Gui.IsHot(row);
                if (hot || (!sub && ReferenceEquals(entry, _logSelected))) Rl.DrawRectangleRec(row, Gui.Hover);
                Gui.TextLeft(row, Gui.FitText(text, row.Width - 8), col);
                if (hot && !sub && Gui.LeftPressed && !Gui.Busy && entry is not null)
                {
                    _logSelected = ReferenceEquals(_logSelected, entry) ? null : entry;
                    _logFollow = false;
                    var m = System.Text.RegularExpressions.Regex.Match(entry.Text, @"(\w+\.cs)\((\d+),(\d+)\)");
                    if (m.Success) JumpTo(new ScriptDiagnostic(m.Groups[1].Value, int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value), "", "", false));
                }
            }
            y += 20;
        }
        Gui.EndScroll(view);
    }

    private static void JumpTo(ScriptDiagnostic d)
    {
        var path = _files.FirstOrDefault(f => string.Equals(Path.GetFileName(f), d.File, StringComparison.OrdinalIgnoreCase));
        if (path is null) return;
        OpenFile(path);
        _doc?.Buffer.MoveTo(d.Line - 1, d.Column - 1);
        _focus = true; _followCaret = true;
    }

    private static void DrawStatus(Rectangle r)
    {
        Gui.SetScope(7620);
        float bx = r.X;
        if (Gui.Button(new Rectangle(bx, r.Y, 190, r.Height), "Save & Compile  (Ctrl+S)")) SaveAndCompile();
        bx += 200;
        if (Gui.Button(new Rectangle(bx, r.Y, 100, r.Height), _showObjects ? "Hide Objects" : "Objects")) _showObjects = !_showObjects;
        bx += 110;
        int errs = DebugLog.Errors;
        if (Gui.Button(new Rectangle(bx, r.Y, errs > 0 && !_showLog ? 100 : 70, r.Height), _showLog ? "Hide Log" : errs > 0 ? $"Log ({errs})" : "Log", selected: _showLog)) _showLog = !_showLog;
        bx += (errs > 0 && !_showLog ? 100 : 70) + 10;

        string pos = _doc is null ? "" : $"Ln {_doc.Buffer.Line + 1}, Col {_doc.Buffer.Col + 1}";
        float right = r.X + r.Width;
        float posW = Rl.MeasureText("Ln 9999, Col 999", Gui.FontSize) + 10;
        Gui.Text(right - posW, r.Y + 5, pos, Gui.TextDim);
        string msg = _info.Length > 0 ? _info : ScriptManager.Last is { } l
            ? (l.Success ? $"Compiled {l.Files} file(s) in {l.Milliseconds:0} ms: {(l.TypeNames.Count > 0 ? string.Join(", ", l.TypeNames) : "no components")}" : $"{l.Errors.Count()} error(s)")
            : "Ctrl+S saves and compiles. Components appear in Add Component.";
        Gui.Text(bx, r.Y + 5, Gui.FitText(msg, right - posW - bx - 10), _info.Length > 0 ? _infoColor : Gui.TextDim);
    }
}
