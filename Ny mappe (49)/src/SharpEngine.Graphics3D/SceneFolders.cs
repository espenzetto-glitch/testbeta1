using System.Numerics;

namespace SharpEngine.Graphics3D;

/// <summary>
/// One row of the hierarchy tree: either an object or a folder. A closed hierarchy, so a <c>switch</c> over it is exhaustive
/// in spirit (this is what a C# 15 <c>union HierarchyItem(ObjectItem, FolderItem)</c> expresses; plain records are used so the engine builds on every SDK).
/// </summary>
public abstract record HierarchyItem
{
    private protected HierarchyItem() { }
}

/// <summary>An object in the hierarchy.</summary>
public sealed record ObjectItem(GameObject Object) : HierarchyItem;

/// <summary>A folder in the hierarchy. <see cref="Path"/> is like "Town/Houses".</summary>
public sealed record FolderItem(string Path, string Name, int Depth, int ObjectCount) : HierarchyItem;

/// <summary>
/// The folders and sub-folders that organise a scene's objects. Folders only exist for organising: they have no transform.
/// Paths use '/' ("Town/Houses"). An object sits in the folder named by <see cref="GameObject.Folder"/> ("" = scene root).
/// </summary>
public sealed class SceneFolders
{
    private readonly World _world;
    private readonly HashSet<string> _empty = new(StringComparer.OrdinalIgnoreCase);

    internal SceneFolders(World world) => _world = world;

    /// <summary>Cleans a path: '/' separators, no empty parts, no surrounding spaces.</summary>
    public static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        var parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join('/', parts);
    }

    public static string NameOf(string path) => path[(path.LastIndexOf('/') + 1)..];
    public static string ParentOf(string path) { int i = path.LastIndexOf('/'); return i < 0 ? "" : path[..i]; }
    public static bool IsInside(string path, string folder) =>
        folder.Length == 0 || path.Equals(folder, StringComparison.OrdinalIgnoreCase) || path.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase);

    /// <summary>Every folder path (including parents of used folders), sorted.</summary>
    public IReadOnlyList<string> All
    {
        get
        {
            var set = new HashSet<string>(_empty, StringComparer.OrdinalIgnoreCase);
            foreach (var o in _world.Objects) if (!o.IsScene && o.Folder.Length > 0) set.Add(o.Folder);
            foreach (var p in set.ToList())
                for (var up = ParentOf(p); up.Length > 0; up = ParentOf(up)) set.Add(up);
            return set.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    public bool Exists(string path) => All.Contains(Normalize(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates a folder (and its parents). Returns the cleaned path.</summary>
    public string Create(string path)
    {
        path = Normalize(path);
        if (path.Length == 0) throw new ArgumentException("A folder needs a name.", nameof(path));
        for (var p = path; p.Length > 0; p = ParentOf(p)) _empty.Add(p);
        return path;
    }

    /// <summary>A name like "Folder", "Folder 2" that is free inside <paramref name="parent"/>.</summary>
    public string UniquePath(string parent, string baseName = "Folder")
    {
        parent = Normalize(parent);
        var all = All;
        string Make(string n) => parent.Length == 0 ? n : parent + "/" + n;
        string name = baseName;
        for (int i = 2; all.Contains(Make(name), StringComparer.OrdinalIgnoreCase); i++) name = $"{baseName} {i}";
        return Make(name);
    }

    /// <summary>Moves/renames a folder (with everything inside it) to a new path. Returns the new path.</summary>
    public string Move(string from, string to)
    {
        from = Normalize(from); to = Normalize(to);
        if (from.Length == 0 || to.Length == 0) throw new ArgumentException("Folder path is empty.");
        if (from.Equals(to, StringComparison.OrdinalIgnoreCase)) return from;
        if (IsInside(to, from)) throw new InvalidOperationException("A folder cannot be moved into itself.");
        if (Exists(to)) throw new InvalidOperationException($"Folder '{to}' already exists.");

        string Re(string p) => p.Length == from.Length ? to : to + p[from.Length..];
        foreach (var o in _world.Objects) if (!o.IsScene && IsInside(o.Folder, from)) o.Folder = Re(o.Folder);
        foreach (var p in _empty.Where(p => IsInside(p, from)).ToList()) { _empty.Remove(p); _empty.Add(Re(p)); }
        Create(to);
        return to;
    }

    public string Rename(string path, string newName) =>
        Move(path, (ParentOf(Normalize(path)) is { Length: > 0 } up ? up + "/" : "") + Normalize(newName).Replace("/", "_"));

    /// <summary>Deletes a folder. Its objects (and sub-folders) move up to the parent folder, or are destroyed when <paramref name="destroyObjects"/>.</summary>
    public void Delete(string path, bool destroyObjects = false)
    {
        path = Normalize(path);
        if (path.Length == 0) return;
        string parent = ParentOf(path);
        foreach (var o in _world.Objects.ToList())
        {
            if (o.IsScene || !IsInside(o.Folder, path)) continue;
            if (destroyObjects) o.Destroy();
            else o.Folder = o.Folder.Equals(path, StringComparison.OrdinalIgnoreCase) ? parent : parent.Length == 0 ? o.Folder[(path.Length + 1)..] : parent + "/" + o.Folder[(path.Length + 1)..];
        }
        foreach (var p in _empty.Where(p => IsInside(p, path)).ToList()) _empty.Remove(p);
        // sub-folders that held objects were re-pathed above; empty ones inside are gone
    }

    /// <summary>Puts an object in a folder (created when missing). "" = scene root.</summary>
    public void Put(GameObject go, string folder)
    {
        folder = Normalize(folder);
        if (folder.Length > 0) Create(folder);
        go.Folder = folder;
    }

    /// <summary>Objects in a folder; with <paramref name="recursive"/> also those in its sub-folders.</summary>
    public IEnumerable<GameObject> Objects(string folder, bool recursive = true)
    {
        folder = Normalize(folder);
        foreach (var o in _world.Objects)
        {
            if (o.Destroyed || o.IsScene) continue;
            if (recursive ? IsInside(o.Folder, folder) : o.Folder.Equals(folder, StringComparison.OrdinalIgnoreCase)) yield return o;
        }
    }

    /// <summary>The box around everything in a folder (and its sub-folders). False when it is empty.</summary>
    public bool TryGetBounds(string folder, out Vector3 min, out Vector3 max)
    {
        min = new Vector3(float.MaxValue); max = new Vector3(float.MinValue);
        bool any = false;
        foreach (var o in Objects(folder))
        {
            var (lo, hi) = World.WorldBounds(o);
            min = Vector3.Min(min, lo); max = Vector3.Max(max, hi);
            any = true;
        }
        return any;
    }

    /// <summary>Switches a folder's objects on or off.</summary>
    public void SetActive(string folder, bool active)
    {
        foreach (var o in Objects(folder)) o.Active = active;
    }

    /// <summary>The tree as a flat list in drawing order: folders first, then objects, children indented by Depth. Collapsed folders hide their contents.</summary>
    public List<(HierarchyItem Item, int Depth)> Tree(Func<string, bool> isCollapsed)
    {
        var result = new List<(HierarchyItem, int)>();
        var all = All;
        void Walk(string parent, int depth)
        {
            foreach (var f in all.Where(f => ParentOf(f).Equals(parent, StringComparison.OrdinalIgnoreCase)))
            {
                result.Add((new FolderItem(f, NameOf(f), depth, Objects(f).Count()), depth));
                if (!isCollapsed(f)) Walk(f, depth + 1);
            }
            foreach (var o in _world.Objects)
                if (!o.Destroyed && !o.IsScene && o.Folder.Equals(parent, StringComparison.OrdinalIgnoreCase))
                    result.Add((new ObjectItem(o), depth));
        }
        Walk("", 0);
        return result;
    }

    internal void Reset() => _empty.Clear();
    internal IEnumerable<string> EmptyFolders => _empty;
    internal void AddEmpty(string path) { if (Normalize(path) is { Length: > 0 } p) Create(p); }
}
