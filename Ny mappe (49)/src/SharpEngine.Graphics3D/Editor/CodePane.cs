using Raylib_cs;
using SharpEngine.Graphics3D.Scripting;
using Rl = Raylib_cs.Raylib;

namespace SharpEngine.Graphics3D.Editor;

/// <summary>
/// The engine's code editor as a pane inside another window (Manage Scenes, World Map): a list of every script of the project
/// on the left and the C# 15 editor on the chosen one. Ctrl+S saves every changed script and compiles them.
/// </summary>
public sealed class CodePane
{
    private const float ListW = 200f;
    private readonly int _scope;
    private string? _file;
    private float _scroll;
    private List<string> _files = new();
    private double _filesAt;

    /// <param name="scope">A widget scope number that no other part of the host window uses (the pane uses scope and scope + 1).</param>
    public CodePane(int scope) { _scope = scope; }

    /// <summary>True while the pane is shown.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>The script the editor shows.</summary>
    public string? Current => _file;

    /// <summary>Shows the pane on <paramref name="preferred"/> when that file exists, otherwise on the script used last (or the first one).</summary>
    public void Show(string? preferred = null)
    {
        IsOpen = true;
        Refresh();
        if (preferred is not null && File.Exists(preferred)) _file = preferred;
        else if (_file is null || !File.Exists(_file)) _file = _files.FirstOrDefault();
    }

    /// <summary>Shows the pane on one script.</summary>
    public void Select(string path) { _file = path; IsOpen = true; Refresh(); }

    public void Hide() { IsOpen = false; CodeEditorWindow.ReleaseFocus(); }

    public void Refresh() { _files = ScriptManager.Files().ToList(); _filesAt = Rl.GetTime(); }

    /// <param name="pane">Where to draw.</param>
    /// <param name="highlight">A script to show in blue in the list (for example the open scene's code), or null.</param>
    /// <param name="hint">Short text before the file name in the header.</param>
    public void Draw(Rectangle pane, string? highlight, string hint)
    {
        if (!IsOpen) return;
        if (Rl.GetTime() - _filesAt > 1.0) Refresh();
        if (_file is not null && !File.Exists(_file)) _file = null;

        string title = _file is null ? "Scripts" : System.IO.Path.GetRelativePath(ScriptManager.Directory, _file) + (CodeEditorWindow.IsDirty(_file) ? " *" : "");
        Gui.SetScope(_scope);
        Gui.Text(pane.X, pane.Y + 4, Gui.FitText(hint + "  -  " + title + "   (Ctrl+S saves and compiles)", pane.Width - 100), Gui.TextDim);
        if (Gui.Button(new Rectangle(pane.X + pane.Width - 90, pane.Y, 90, 24), "Close code")) { Hide(); return; }

        // every script of the project
        var files = new Rectangle(pane.X, pane.Y + 32, ListW, pane.Height - 32);
        Rl.DrawRectangleRec(files, new Color(26, 27, 31, 255));
        Rl.DrawRectangleLinesEx(files, 1f, new Color(18, 19, 22, 255));
        Gui.SetScope(_scope + 1);
        var view = new Rectangle(files.X + 1, files.Y + 1, files.Width - 2, files.Height - 2);
        Gui.BeginScroll(view, ref _scroll, MathF.Max(1, _files.Count) * 24f + 6);
        float fy = view.Y - _scroll + 2;
        if (_files.Count == 0) Gui.Text(view.X + 8, fy + 4, "No scripts yet.", Gui.TextDim);
        foreach (var f in _files)
        {
            var row = new Rectangle(view.X + 3, fy, view.Width - 10, 22);
            bool sel = string.Equals(f, _file, StringComparison.OrdinalIgnoreCase);
            bool hot = Gui.IsHot(row);
            Rl.DrawRectangleRec(row, sel ? Gui.Accent : hot ? Gui.Hover : new Color(26, 27, 31, 255));
            string label = System.IO.Path.GetRelativePath(ScriptManager.Directory, f) + (CodeEditorWindow.IsDirty(f) ? " *" : "");
            Gui.TextLeft(row, Gui.FitText(label, row.Width - 8), highlight is not null && string.Equals(f, highlight, StringComparison.OrdinalIgnoreCase) ? new Color(150, 200, 255, 255) : Color.White);
            if (hot && Gui.LeftPressed && !Gui.Busy) _file = f;
            fy += 24;
        }
        Gui.EndScroll(view);

        var area = new Rectangle(files.X + files.Width + 8, pane.Y + 32, pane.Width - ListW - 8, pane.Height - 32);
        if (_file is null)
        {
            Rl.DrawRectangleRec(area, new Color(26, 27, 31, 255));
            Gui.Text(area.X + 20, area.Y + 20, Gui.FitText("Pick a script on the left.", area.Width - 40), Gui.TextDim);
            return;
        }
        if (!string.Equals(CodeEditorWindow.CurrentFile, _file, StringComparison.OrdinalIgnoreCase)) CodeEditorWindow.OpenEmbedded(_file);
        CodeEditorWindow.DrawEmbedded(area);
    }
}