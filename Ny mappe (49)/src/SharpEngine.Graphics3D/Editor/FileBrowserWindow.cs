using System.Numerics;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace SharpEngine.Graphics3D.Editor;

/// <summary>
/// The in-editor file system: browse the computer's folders and import models (.fbx .obj), textures and whole
/// directories into the scene without dragging. Open with the toolbar's Import... button or Ctrl+I.
/// </summary>
public static class FileBrowserWindow
{
    private sealed record Entry(string Path, string Name, bool IsDir, long Size);

    private static string _dir = "";
    private static string _pathEdit = "";
    private static List<Entry> _entries = new();
    private static int _hidden;
    private static readonly HashSet<string> _selected = new(StringComparer.OrdinalIgnoreCase);
    private static string? _lastClicked;
    private static double _lastClickTime;
    private static float _scroll, _placesScroll;
    private static string _info = "";
    private static string _error = "";

    public static bool IsOpen { get; private set; }

    /// <summary>Opens the browser (at the last folder, or the Assets folder the first time).</summary>
    public static void Open()
    {
        IsOpen = true;
        _info = _error = "";
        if (_dir.Length == 0 || !Directory.Exists(_dir)) Navigate(StartFolder());
        else Navigate(_dir);
    }

    public static void Close() => IsOpen = false;

    private static string StartFolder()
    {
        string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        return Directory.Exists(downloads) ? downloads : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) is { Length: > 0 } h && Directory.Exists(h) ? h : Directory.GetCurrentDirectory();
    }

    private static IEnumerable<(string Label, string Path)> Places()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (Directory.Exists(Assets.Folder)) yield return ("Project Assets", Path.GetFullPath(Assets.Folder));
        if (home.Length > 0)
        {
            yield return ("Home", home);
            foreach (var n in new[] { "Desktop", "Documents", "Downloads" })
                if (Directory.Exists(Path.Combine(home, n))) yield return (n, Path.Combine(home, n));
        }
        string[] roots;
        try { roots = OperatingSystem.IsWindows()
                ? DriveInfo.GetDrives().Where(d => { try { return d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable or DriveType.Network; } catch (IOException) { return false; } }).Select(d => d.RootDirectory.FullName).ToArray()
                : new[] { "/" }; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { roots = Array.Empty<string>(); }
        foreach (var r in roots) yield return (r, r);
    }

    private static void Navigate(string dir)
    {
        try
        {
            dir = Path.GetFullPath(dir);
            var list = new List<Entry>();
            int hidden = 0;
            foreach (var d in new DirectoryInfo(dir).EnumerateDirectories().OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
            {
                if ((d.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                list.Add(new Entry(d.FullName, d.Name, true, 0));
            }
            foreach (var f in new DirectoryInfo(dir).EnumerateFiles().OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (Assets.IsModel(f.Name) || Assets.IsTexture(f.Name)) list.Add(new Entry(f.FullName, f.Name, false, f.Length));
                else hidden++;
                if (list.Count >= 3000) break;
            }
            _dir = dir; _pathEdit = dir; _entries = list; _hidden = hidden;
            _selected.Clear(); _scroll = 0; _error = "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            _error = ex.Message;
            _pathEdit = _dir;
        }
    }

    private static bool Clicked(Rectangle r) => Gui.IsHot(r) && Gui.LeftPressed;

    public static void Draw(World world)
    {
        if (!IsOpen) return;
        float sw = Gui.ScreenWidth, sh = Gui.ScreenHeight;
        float w = MathF.Min(sw - 40f, 920f), h = MathF.Min(sh - 40f, 620f);
        var d = new Rectangle((sw - w) / 2f, (sh - h) / 2f, w, h);

        Rl.DrawRectangle(0, 0, (int)sw, (int)sh, new Color(0, 0, 0, 130));
        Rl.DrawRectangleRec(d, new Color(44, 46, 52, 255));
        Rl.DrawRectangleLinesEx(d, 1f, new Color(20, 20, 24, 255));
        Gui.SetScope(9000);

        // title + close
        Rl.DrawRectangleRec(new Rectangle(d.X, d.Y, d.Width, 34), new Color(32, 34, 40, 255));
        Gui.Text(d.X + 10, d.Y + 7, "Import  -  browse files and folders", Gui.TextCol);
        if (Gui.Button(new Rectangle(d.X + d.Width - 30, d.Y + 5, 24, 24), "x")) { Close(); return; }

        // path bar
        float y = d.Y + 42;
        if (Gui.Button(new Rectangle(d.X + 8, y, 54, 26), "Up"))
        {
            var parent = Directory.GetParent(_dir);
            if (parent is not null) Navigate(parent.FullName);
        }
        var field = new Rectangle(d.X + 68, y, d.Width - 76, 26);
        string edit = _pathEdit;
        if (Gui.TextField(field, "fb.path", ref edit))
        {
            _pathEdit = edit;
            if (Directory.Exists(edit) && !string.Equals(Path.GetFullPath(edit), _dir, StringComparison.OrdinalIgnoreCase)) Navigate(edit);
        }

        // places
        y += 34;
        float bottom = d.Y + d.Height - 78;
        var places = new Rectangle(d.X + 8, y, 170, bottom - y);
        Rl.DrawRectangleRec(places, new Color(36, 38, 44, 255));
        var pl = Places().ToList();
        Gui.SetScope(9100);
        Gui.BeginScroll(places, ref _placesScroll, pl.Count * 24f + 4);
        float py = places.Y + 2 - _placesScroll;
        foreach (var (label, path) in pl)
        {
            var row = new Rectangle(places.X + 2, py, places.Width - 10, 24);
            bool current = string.Equals(path, _dir, StringComparison.OrdinalIgnoreCase);
            if (current) Rl.DrawRectangleRec(row, Gui.Accent); else if (Gui.IsHot(row)) Rl.DrawRectangleRec(row, Gui.Hover);
            Gui.TextLeft(row, Gui.FitText(label, row.Width - 8), Gui.TextCol);
            if (Clicked(row)) { string p = path; Navigate(p); }
            py += 24;
        }
        Gui.EndScroll(places);

        // file list
        var list = new Rectangle(places.X + places.Width + 8, y, d.X + d.Width - 8 - (places.X + places.Width + 8), bottom - y);
        Rl.DrawRectangleRec(list, new Color(36, 38, 44, 255));
        Gui.SetScope(9200);
        const float rowH = 24;
        Gui.BeginScroll(list, ref _scroll, _entries.Count * rowH + 4);
        float ry = list.Y + 2 - _scroll;
        bool ctrl = Rl.IsKeyDown(KeyboardKey.LeftControl) || Rl.IsKeyDown(KeyboardKey.RightControl);
        string? enter = null;
        string? importNow = null;
        foreach (var e in _entries)
        {
            if (ry + rowH >= list.Y && ry <= list.Y + list.Height)
            {
                var row = new Rectangle(list.X + 2, ry, list.Width - 12, rowH);
                bool sel = _selected.Contains(e.Path);
                if (sel) Rl.DrawRectangleRec(row, Gui.Accent); else if (Gui.IsHot(row)) Rl.DrawRectangleRec(row, Gui.Hover);
                string kind = e.IsDir ? "[folder] " : Assets.IsModel(e.Name) ? "[model]  " : "[image]  ";
                var col = e.IsDir ? new Color(255, 214, 120, 255) : Assets.IsModel(e.Name) ? new Color(150, 220, 255, 255) : Gui.TextCol;
                Gui.TextLeft(row, Gui.FitText(kind + e.Name, row.Width - 90), col);
                if (!e.IsDir) Gui.TextLeft(new Rectangle(row.X + row.Width - 84, row.Y, 84, row.Height), FormatSize(e.Size), Gui.TextDim);
                if (Clicked(row))
                {
                    double now = Rl.GetTime();
                    bool dbl = _lastClicked == e.Path && now - _lastClickTime < 0.45;
                    _lastClicked = e.Path; _lastClickTime = now;
                    if (dbl) { if (e.IsDir) enter = e.Path; else importNow = e.Path; }
                    else if (ctrl) { if (!_selected.Remove(e.Path)) _selected.Add(e.Path); }
                    else { _selected.Clear(); _selected.Add(e.Path); }
                }
            }
            ry += rowH;
        }
        if (_entries.Count == 0) Gui.Text(list.X + 10, list.Y + 8, "Nothing to import in this folder. Open a sub-folder or press Up.", Gui.TextDim);
        Gui.EndScroll(list);
        if (enter is not null) Navigate(enter);

        // bottom bar
        float by = d.Y + d.Height - 68;
        string status = _error.Length > 0 ? _error
            : $"{_entries.Count(e => e.IsDir)} folders, {_entries.Count(e => !e.IsDir)} importable files" + (_hidden > 0 ? $" ({_hidden} other files hidden)" : "") + "   |   click = select, Ctrl+click = add, double-click = open / import";
        Gui.Text(d.X + 10, by, Gui.FitText(status, d.Width - 20), _error.Length > 0 ? new Color(240, 120, 120, 255) : Gui.TextDim);
        if (_info.Length > 0) Gui.Text(d.X + 10, by + 18, Gui.FitText(_info, d.Width - 20), new Color(120, 215, 130, 255));

        float bx = d.X + 8, byy = d.Y + d.Height - 34;
        bool any = _selected.Count > 0;
        if (Gui.Button(new Rectangle(bx, byy, 200, 26), $"Import Selected ({_selected.Count})", false, any) && any)
            DoImport(world, _selected.ToList());
        if (Gui.Button(new Rectangle(bx + 208, byy, 230, 26), "Import This Folder + Subfolders"))
            DoImport(world, new List<string> { _dir });
        if (Gui.Button(new Rectangle(d.X + d.Width - 88, byy, 80, 26), "Close")) Close();

        if (importNow is not null) DoImport(world, new List<string> { importNow });

        if (Rl.IsKeyPressed(KeyboardKey.Escape) && !Gui.PopupOpen && !Gui.IsEditing) Close();
    }

    private static void DoImport(World world, List<string> paths)
    {
        try
        {
            string result = AdminEditor.ImportPaths(world, paths);
            _info = result;
            _selected.Clear();
            Close();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or UnauthorizedAccessException or InvalidOperationException)
        {
            _error = ex.Message;
        }
    }

    private static string FormatSize(long bytes) =>
        bytes >= 1 << 20 ? $"{bytes / 1048576.0:0.#} MB" : bytes >= 1024 ? $"{bytes / 1024.0:0.#} KB" : $"{bytes} B";
}
