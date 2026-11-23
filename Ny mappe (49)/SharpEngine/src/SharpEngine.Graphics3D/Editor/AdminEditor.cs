using System.Numerics;
using System.Reflection;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace SharpEngine.Graphics3D.Editor;

/// <summary>
/// Static in-game admin editor (like a mini Unity Editor): toolbar, hierarchy, inspector, scene
/// picking, move gizmo, play/stop with restore, and save/load to scene.json.
/// Call <see cref="Attach"/> once, then <see cref="Update"/> and <see cref="DrawGui"/> every frame
/// (App3D.Run does this for you).
/// </summary>
public static class AdminEditor
{
    private const float ToolbarH = 38f, StatusH = 26f, HierarchyW = 230f, InspectorW = 340f, LabelW = 112f;

    private static World? _world;
    private static Action? _pending;
    private static string? _snapshot;
    private static string _snapshotScene = "Untitled";
    private static int _snapshotSelection = -1;

    // unsaved-changes tracking
    private static string _clean = "";
    private static bool _dirty;
    private static double _nextDirtyCheck;

    // "Save changes?" dialog
    private sealed record Confirm(string Message, Action Proceed);
    private static Confirm? _confirm;
    private static string _status = "Edit mode. Press Play (F5) to run the scripts.";
    private static double _statusUntil;

    private static float _hierScroll, _hierHeight = 200;
    private static float _inspScroll, _inspHeight = 400;
    private static bool _addMenu;

    // dragging a component (from an inspector header or the Add Component list) onto an object
    private static Type? _dragType;
    private static Component? _dragSource;
    private static Vector2 _dragFrom;
    private static bool _dragActive, _dragArmed, _dragSwallowClick;
    private static GameObject? _dropRow;      // hierarchy row under the mouse this frame
    private static double _nextTextureRefresh;

    // gizmo
    private static int _hoverAxis = -1;
    private static int _dragAxis = -1;
    private static float _dragStartParam;
    private static Vector3 _dragStartPos;

    public static bool Visible { get; set; } = true;
    public static GameObject? Selected { get; set; }

    public static bool MouseOverUi { get; private set; }

    /// <summary>Hooks the editor into a world. Starts in edit mode (scripts paused).</summary>
    public static void Attach(World world)
    {
        if (_world is not null) _world.Render3D -= OnRender3D;
        _world = world;
        Gui.LoadPrefs();
        world.Render3D += OnRender3D;
        world.PlayMode = false;
        Selected = null;
        _clean = SceneSerializer.Save(world);
        _dirty = false;
    }

    /// <summary>True while a dialog covers the editor.</summary>
    public static bool ModalOpen => BuildSettingsWindow.IsOpen || WorldMapWindow.IsOpen || _confirm is not null;

    // ================================================================================ update ==

    public static void Update(World world)
    {
        Gui.BeginFrame();

        var action = _pending;
        _pending = null;
        action?.Invoke();

        if (Selected is { Destroyed: true }) Selected = null;
        MouseOverUi = Visible && (ModalOpen || (Layout(out var tb, out var hi, out var ins, out var st) &&
                      (Hit(tb) || Hit(hi) || Hit(ins) || Hit(st))));

        Gui.KeyboardCaptured = ModalOpen;
        if (!world.PlayMode && Rl.GetTime() >= _nextDirtyCheck)
        {
            _nextDirtyCheck = Rl.GetTime() + 0.5;
            _dirty = SceneSerializer.Save(world) != _clean;
        }

        HandleDroppedFiles(world);
        if (!world.PlayMode && !Gui.WantsKeyboard && Visible && world.TryGetCamera(out var pcam))
            foreach (var go in world.Objects)
                if (go.Active && !go.Destroyed && go.GetComponent<ScenePortal>() is { RequireInteract: true } portal
                    && Vector3.Distance(pcam.Position, go.Transform.Position) <= portal.Radius && Rl.IsKeyPressed(portal.Key))
                {
                    Say("Portals only work in Play mode. Press Play (F5), then walk up to the portal and press " + portal.Key + ".");
                    break;
                }
        if (!world.PlayMode && Rl.GetTime() >= _nextTextureRefresh)
        {
            _nextTextureRefresh = Rl.GetTime() + 1.0;
            TextureCache.Refresh();
        }

        if (!Gui.WantsKeyboard) HandleShortcuts(world);
        if (Visible && !ModalOpen && !Gui.Busy && !Rl.IsMouseButtonDown(MouseButton.Right)) HandleScene(world);
    }

    private static void HandleShortcuts(World world)
    {
        if (ModalOpen) return;
        bool ctrl = Rl.IsKeyDown(KeyboardKey.LeftControl);
        bool shift = Rl.IsKeyDown(KeyboardKey.LeftShift);
        if (Rl.IsKeyPressed(KeyboardKey.F1)) Visible = !Visible;
        if (Rl.IsKeyPressed(KeyboardKey.F5)) _pending = () => TogglePlay(world);
        if (ctrl && !shift && Rl.IsKeyPressed(KeyboardKey.S)) _pending = () => SaveScene(world);
        if (Visible && ctrl && shift && Rl.IsKeyPressed(KeyboardKey.B)) BuildSettingsWindow.Open();
        if (Visible && ctrl && !shift && Rl.IsKeyPressed(KeyboardKey.M)) WorldMapWindow.Open();
        if (ctrl && !shift)
        {
            if (Rl.IsKeyPressed(KeyboardKey.Equal) || Rl.IsKeyPressed(KeyboardKey.KpAdd)) SetScale(Gui.Scale + 0.1f);
            else if (Rl.IsKeyPressed(KeyboardKey.Minus) || Rl.IsKeyPressed(KeyboardKey.KpSubtract)) SetScale(Gui.Scale - 0.1f);
            else if (Rl.IsKeyPressed(KeyboardKey.Zero) || Rl.IsKeyPressed(KeyboardKey.Kp0)) SetScale(1f);
        }
        if (!Visible) return;
        if (Rl.IsKeyPressed(KeyboardKey.Delete)) DeleteSelected(world);
        if (ctrl && Rl.IsKeyPressed(KeyboardKey.D)) DuplicateSelected(world);
    }

    private static float _scalePreview = -1f;

    private static void SetScale(float value)
    {
        Gui.Scale = Math.Clamp(MathF.Round(value * 20f) / 20f, Gui.MinScale, Gui.MaxScale);
        Gui.SavePrefs();
    }

    private static void HandleScene(World world)
    {
        if (!world.TryGetCamera(out var camera)) return;

        var ray = Rl.GetScreenToWorldRay(Gui.RawMouse, camera);
        var dir = Vector3.Normalize(ray.Direction);

        // dragging a gizmo handle
        if (_dragAxis >= 0)
        {
            if (!Rl.IsMouseButtonDown(MouseButton.Left) || Selected is null)
            {
                _dragAxis = -1;
                return;
            }
            var axis = AxisVector(_dragAxis);
            if (AxisParam(ray.Position, dir, _dragStartPos, axis, out float s, out _))
                Selected.Transform.Position = _dragStartPos + axis * (s - _dragStartParam);
            return;
        }

        _hoverAxis = -1;
        if (MouseOverUi) return;

        if (Selected is not null)
        {
            var pos = Selected.Transform.Position;
            float len = HandleLength(camera, pos);
            float best = len * 0.09f;
            for (int i = 0; i < 3; i++)
            {
                var axis = AxisVector(i);
                if (!AxisParam(ray.Position, dir, pos, axis, out float s, out _)) continue;
                var nearest = pos + axis * Math.Clamp(s, 0f, len);
                float d = PointRayDistance(nearest, ray.Position, dir);
                if (d < best) { best = d; _hoverAxis = i; }
            }
        }

        if (!Rl.IsMouseButtonPressed(MouseButton.Left)) return;

        if (_hoverAxis >= 0 && Selected is not null)
        {
            _dragAxis = _hoverAxis;
            _dragStartPos = Selected.Transform.Position;
            AxisParam(ray.Position, dir, _dragStartPos, AxisVector(_dragAxis), out _dragStartParam, out _);
        }
        else
        {
            Selected = world.Raycast(ray, out _);
        }
    }

    // ================================================================================ actions ==

    private static void Say(string message)
    {
        _status = message;
        _statusUntil = Rl.GetTime() + 6;
    }

    private static Vector3 SpawnPoint(World world)
    {
        if (!world.TryGetCamera(out var cam)) return Vector3.Zero;
        var p = cam.Position + Vector3.Normalize(cam.Target - cam.Position) * 6f;
        return new Vector3(MathF.Round(p.X), MathF.Max(0.5f, MathF.Round(p.Y)), MathF.Round(p.Z));
    }

    private static void AddPrimitive(World world, string name)
    {
        var go = world.CreatePrimitive(name, new Color(200, 200, 205, 255), SpawnPoint(world));
        Selected = go;
        Say($"Created {name}");
    }

    private static void AddEmpty(World world)
    {
        Selected = world.Instantiate("GameObject", SpawnPoint(world));
        Say("Created empty GameObject");
    }

    private static void DeleteSelected(World world)
    {
        if (Selected is null) return;
        var name = Selected.Name;
        world.Destroy(Selected);
        Selected = null;
        Say($"Deleted {name}");
    }

    private static void DuplicateSelected(World world)
    {
        if (Selected is null) return;
        Selected = SceneSerializer.Clone(world, Selected);
        Say($"Duplicated into {Selected.Name}");
    }

    private static void TogglePlay(World world)
    {
        if (!world.PlayMode)
        {
            _snapshot = SceneSerializer.Save(world);
            _snapshotScene = SceneManager.CurrentName;
            _snapshotSelection = IndexOf(world, Selected);
            world.PlayMode = true;
            Say("Playing. Stop restores the scene as it was before Play.");
        }
        else
        {
            world.PlayMode = false;
            if (_snapshot is not null)
            {
                SceneSerializer.Load(world, _snapshot);
                SceneManager.CurrentName = _snapshotScene;
                Selected = _snapshotSelection >= 0 && _snapshotSelection < world.Objects.Count ? world.Objects[_snapshotSelection] : null;
            }
            Say("Stopped. Scene restored.");
        }
    }

    private static void SaveScene(World world) => SaveCurrentScene(out _);

    /// <summary>Writes the open scene to Scenes/&lt;name&gt;.json (used by Save and by "Add Open Scenes").</summary>
    internal static bool SaveCurrentScene(out string name)
    {
        name = SceneManager.CurrentName;
        if (_world is null) return false;
        try
        {
            SceneManager.Save(_world, name);
            _clean = SceneSerializer.Save(_world);
            _dirty = false;
            Say($"Saved {Path.GetFullPath(SceneManager.FileFor(name))}");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Say($"Save failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>Checks the editor is in a state where a player can be built.</summary>
    internal static bool PrepareForBuild(out string problem)
    {
        problem = "";
        if (_world is { PlayMode: true })
        {
            problem = "Stop play mode (F5) before building.";
            return false;
        }
        if (_dirty)
        {
            // keep the file on disk in sync with what the user sees
            if (!SaveCurrentScene(out _)) { problem = "Could not save the open scene."; return false; }
        }
        return true;
    }

    /// <summary>Runs <paramref name="change"/> now, or after asking what to do with unsaved changes.</summary>
    private static void AskAboutUnsaved(World world, Action change)
    {
        if (world.PlayMode)
        {
            Say("Stop play mode (F5) first.");
            return;
        }
        if (!_dirty) { change(); return; }
        _confirm = new Confirm($"Scene \"{SceneManager.CurrentName}\" has unsaved changes.", change);
    }

    internal static void OpenSceneFromMap(World world, string name) => OpenScene(world, name);

    private static void OpenScene(World world, string name)
    {
        AskAboutUnsaved(world, () =>
        {
            try
            {
                SceneManager.Open(world, name);
                Selected = null;
                _clean = SceneSerializer.Save(world);
                _dirty = false;
                Say($"Opened {name}");
            }
            catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
            {
                Say($"Could not open {name}: {ex.Message}");
            }
        });
    }

    private static void NewScene(World world)
    {
        AskAboutUnsaved(world, () =>
        {
            string name = SceneManager.UniqueName("NewScene");
            SceneManager.CreateEmpty(world, name);
            Selected = null;
            _clean = SceneSerializer.Save(world);
            _dirty = false;
            Say($"Created {name}. Press Save to write it to the Scenes folder.");
        });
    }

    private static int IndexOf(World world, GameObject? go)
    {
        if (go is null) return -1;
        for (int i = 0; i < world.Objects.Count; i++)
            if (ReferenceEquals(world.Objects[i], go)) return i;
        return -1;
    }

    // ================================================================================== draw ==

    public static void DrawGui(World world)
    {
        Gui.BeginUi();
        try { DrawGuiScaled(world); }
        finally { Gui.EndUi(); }
    }

    private static void DrawGuiScaled(World world)
    {
        if (_scalePreview > 0f && !Gui.LeftDown)   // apply the slider value when it is released
        {
            SetScale(_scalePreview);
            _scalePreview = -1f;
        }

        if (!Visible)
        {
            Gui.Text(10, 10, "F1: show editor   F5: play/stop", new Color(255, 255, 255, 200));
            return;
        }

        Layout(out var tb, out var hi, out var ins, out var st);

        DrawPortalLabels(world);
        Gui.Disabled = ModalOpen;        // the panels behind a dialog ignore the mouse
        DrawToolbar(world, tb);
        DrawHierarchy(world, hi);
        DrawInspector(world, ins);
        DrawStatus(world, st);
        Gui.Disabled = false;

        UpdateDrag(world);
        BuildSettingsWindow.Draw(world);
        WorldMapWindow.Draw(world);
        DrawConfirm(world);
        Gui.EndFrame();                  // open drop-down lists are drawn last, on top
    }

    private static void DrawConfirm(World world)
    {
        if (_confirm is not { } c) return;

        float sw = Gui.ScreenWidth, sh = Gui.ScreenHeight;
        var d = new Rectangle((sw - 440) / 2f, (sh - 150) / 2f, 440, 150);
        Rl.DrawRectangle(0, 0, (int)sw, (int)sh, new Color(0, 0, 0, 120));
        Rl.DrawRectangleRec(d, Gui.PanelBg);
        Rl.DrawRectangleLinesEx(d, 1f, Gui.Border);
        Rl.DrawRectangleRec(new Rectangle(d.X, d.Y, d.Width, 30), new Color(242, 242, 242, 255));
        Gui.TextSized(d.X + 10, d.Y + 5, "Unsaved Changes", 20, new Color(100, 100, 100, 255));
        Gui.TextSized(d.X + 16, d.Y + 48, c.Message, 17, Gui.TextCol);
        Gui.TextSized(d.X + 16, d.Y + 72, "Do you want to save them first?", 17, Gui.TextDim);

        Gui.SetScope(8000);
        float by = d.Y + d.Height - 40;
        if (Gui.Button(new Rectangle(d.X + 16, by, 130, 28), "Save"))
        {
            _confirm = null;
            if (SaveCurrentScene(out _)) _pending = c.Proceed;
        }
        else if (Gui.Button(new Rectangle(d.X + 156, by, 130, 28), "Don't Save"))
        {
            _confirm = null;
            _dirty = false;
            _pending = c.Proceed;
        }
        else if (Gui.Button(new Rectangle(d.X + 296, by, 128, 28), "Cancel") || Rl.IsKeyPressed(KeyboardKey.Escape))
        {
            _confirm = null;
        }
    }

    private static bool Layout(out Rectangle toolbar, out Rectangle hierarchy, out Rectangle inspector, out Rectangle status)
    {
        float sw = Gui.ScreenWidth, sh = Gui.ScreenHeight;
        float mid = MathF.Max(60f, sh - ToolbarH - StatusH);
        toolbar = new Rectangle(0, 0, sw, ToolbarH);
        hierarchy = new Rectangle(0, ToolbarH, HierarchyW, mid);
        inspector = new Rectangle(sw - InspectorW, ToolbarH, InspectorW, mid);
        status = new Rectangle(0, sh - StatusH, sw, StatusH);
        return true;
    }

    private static bool Hit(Rectangle r) => Rl.CheckCollisionPointRec(Gui.Mouse, r);

    private static readonly string[] CreateItems = { "Empty", "Cube", "Sphere", "Cylinder", "Pyramid", "Plane", "Triangle" };

    private static void DrawToolbar(World world, Rectangle bar)
    {
        Rl.DrawRectangleRec(bar, Gui.PanelHeader);
        float x = 8;
        Rectangle Next(float w)
        {
            var r = new Rectangle(x, 6, w, 26);
            x += w + 6;
            return r;
        }
        void Gap() => x += 10;

        Gui.SetScope(1);
        if (Gui.Button(Next(86), world.PlayMode ? "Stop (F5)" : "Play (F5)", world.PlayMode)) _pending = () => TogglePlay(world);
        Gap();
        if (Gui.Button(Next(48), "Save")) _pending = () => SaveScene(world);
        if (Gui.Button(Next(92), "New Scene")) _pending = () => NewScene(world);

        var scenes = SceneManager.AvailableScenes().ToArray();
        int opened = Gui.Menu(Next(84), "scenes", "Scenes", scenes.Length > 0 ? scenes : new[] { "(no saved scenes)" });
        if (opened >= 0 && opened < scenes.Length)
        {
            string name = scenes[opened];
            _pending = () => OpenScene(world, name);
        }

        if (Gui.Button(Next(100), "World Map")) WorldMapWindow.Open();
        if (Gui.Button(Next(132), "Build Settings...")) BuildSettingsWindow.Open();
        Gap();

        int created = Gui.Menu(Next(84), "create", "Create", CreateItems);
        if (created == 0) _pending = () => AddEmpty(world);
        else if (created > 0)
        {
            string name = CreateItems[created];
            _pending = () => AddPrimitive(world, name);
        }
        Gap();
        if (Gui.Button(Next(76), "Duplicate")) _pending = () => DuplicateSelected(world);
        if (Gui.Button(Next(56), "Delete")) _pending = () => DeleteSelected(world);
    }

    private static void DrawHierarchy(World world, Rectangle panel)
    {
        Gui.Panel(panel, $"Hierarchy ({world.Objects.Count})");
        var view = new Rectangle(panel.X + 1, panel.Y + Gui.Row + 4, panel.Width - 2, panel.Height - Gui.Row - 6);
        Gui.SetScope(2);
        Gui.BeginScroll(view, ref _hierScroll, _hierHeight);

        float y = view.Y - _hierScroll;
        float top = y;
        int n = 0;
        _dropRow = null;
        foreach (var go in world.Objects)
        {
            if (go.Destroyed) continue;
            var row = new Rectangle(view.X + 2, y, view.Width - 8, Gui.Row);
            bool selected = ReferenceEquals(go, Selected);
            bool hover = Gui.IsHot(row);
            bool overRow = Rl.CheckCollisionPointRec(Gui.Mouse, row) && Rl.CheckCollisionPointRec(Gui.Mouse, view);
            if (overRow) _dropRow = go;
            if (selected) Rl.DrawRectangleRec(row, Gui.Accent);
            else if (hover) Rl.DrawRectangleRec(row, Gui.Hover);
            if (_dragActive && overRow) Rl.DrawRectangleLinesEx(row, 2f, new Color(255, 210, 90, 255));

            string icon = go.GetComponent<CameraComponent>() is not null ? "[cam] " :
                          go.GetComponent<MeshRenderer>() is not null ? "" : "[ ] ";
            Gui.TextLeft(row, icon + go.Name, go.Active ? Gui.TextCol : Gui.TextDim);
            if (hover && Gui.LeftPressed && !Gui.Busy && !_dragActive) Selected = go;

            y += Gui.Row;
            n++;
        }
        _hierHeight = y - top + 8;
        Gui.EndScroll(view);
        if (n == 0) Gui.Text(panel.X + 10, panel.Y + 34, "Scene is empty", Gui.TextDim);
    }

    private static void DrawInspector(World world, Rectangle panel)
    {
        Gui.Panel(panel, "Inspector");
        var view = new Rectangle(panel.X + 1, panel.Y + Gui.Row + 4, panel.Width - 2, panel.Height - Gui.Row - 6);
        Gui.BeginScroll(view, ref _inspScroll, _inspHeight);

        float x = view.X + 8;
        float w = view.Width - 22;
        float y = view.Y - _inspScroll + 2;
        float top = y;

        if (Selected is null || Selected.Destroyed)
            DrawSceneSettings(world, x, ref y, w);
        else
            DrawObject(world, Selected, x, ref y, w);

        _inspHeight = y - top + 12;
        Gui.EndScroll(view);
    }

    private static void SectionHeader(float x, ref float y, float w, string title)
    {
        var r = new Rectangle(x - 4, y, w + 8, Gui.Row);
        Rl.DrawRectangleRec(r, Gui.PanelHeader);
        Gui.TextLeft(r, title, Gui.TextCol, 6);
        y += Gui.Row + 4;
    }

    private static bool Row(float x, ref float y, float w, string label, out Rectangle field)
    {
        Gui.Text(x, y + (Gui.Row - Gui.FontSize) / 2f, label, Gui.TextDim);
        field = new Rectangle(x + LabelW, y, w - LabelW, Gui.Row);
        y += Gui.Row + 3;
        return true;
    }

    private static void DrawSceneSettings(World world, float x, ref float y, float w)
    {
        Gui.SetScope(100);
        Gui.Text(x, y, "Nothing selected - scene settings", Gui.TextDim);
        y += 26;
        SectionHeader(x, ref y, w, "Scene");

        Row(x, ref y, w, "Scene Name", out var nameField);
        string sceneName = SceneManager.CurrentName;
        if (Gui.TextField(nameField, "scenename", ref sceneName) && SceneManager.SanitizeName(sceneName) is { } clean)
            SceneManager.CurrentName = clean;
        Gui.Text(x, y, "Saved as Scenes/" + SceneManager.CurrentName + ".json", Gui.TextDim);
        y += 26;

        SectionHeader(x, ref y, w, "Lighting");

        Row(x, ref y, w, "Sun Direction", out var r);
        var dir = world.Sun.Direction;
        if (Gui.Vec3Field(r, "sundir", ref dir, 0.02f) && dir.LengthSquared() > 1e-6f) world.Sun.Direction = Vector3.Normalize(dir);

        Row(x, ref y, w, "Sun Color", out r);
        var sc = world.Sun.Color;
        if (Gui.ColorField(r, "suncol", ref sc)) world.Sun.Color = sc;

        Row(x, ref y, w, "Intensity", out r);
        float inten = world.Sun.Intensity;
        if (Gui.FloatField(r, "inten", ref inten, 0.01f)) world.Sun.Intensity = MathF.Max(0f, inten);

        Row(x, ref y, w, "Ambient", out r);
        float amb = world.Sun.Ambient;
        if (Gui.FloatField(r, "amb", ref amb, 0.01f)) world.Sun.Ambient = Math.Clamp(amb, 0f, 1f);

        y += 6;
        SectionHeader(x, ref y, w, "World Size");
        Row(x, ref y, w, "Width (X)", out r);
        float sizeX = world.SizeX;
        if (Gui.FloatField(r, "sizex", ref sizeX, 1f)) world.SizeX = sizeX;
        Row(x, ref y, w, "Depth (Z)", out r);
        float sizeZ = world.SizeZ;
        if (Gui.FloatField(r, "sizez", ref sizeZ, 1f)) world.SizeZ = sizeZ;
        bool showBounds = world.ShowBounds;
        Row(x, ref y, w, "Show Bounds", out r);
        if (Gui.Checkbox(r, "showbounds", "", ref showBounds)) world.ShowBounds = showBounds;
        bool clampCam = world.ClampCamera;
        Row(x, ref y, w, "Clamp Camera", out r);
        if (Gui.Checkbox(r, "clampcam", "", ref clampCam)) world.ClampCamera = clampCam;
        if (Gui.Button(new Rectangle(x, y, (w - 6) / 2f, 24), "Fit Ground"))
        {
            var ground = world.Find("Ground");
            if (ground is null) Say("This scene has no object named Ground.");
            else { ground.Transform.Scale = new Vector3(world.SizeX, ground.Transform.Scale.Y, world.SizeZ); Say("Ground resized to the scene size."); }
        }
        if (Gui.Button(new Rectangle(x + (w + 6) / 2f, y, (w - 6) / 2f, 24), "World Map...")) WorldMapWindow.Open();
        y += 32;

        SectionHeader(x, ref y, w, "Sky & Helpers");
        Row(x, ref y, w, "Sky Top", out r);
        var top = world.SkyTop;
        if (Gui.ColorField(r, "skytop", ref top)) world.SkyTop = top;
        Row(x, ref y, w, "Sky Bottom", out r);
        var bottom = world.SkyBottom;
        if (Gui.ColorField(r, "skybottom", ref bottom)) world.SkyBottom = bottom;

        bool grid = world.ShowGrid;
        Row(x, ref y, w, "Show Grid", out r);
        if (Gui.Checkbox(r, "grid", "", ref grid)) world.ShowGrid = grid;
        bool axes = world.ShowAxes;
        Row(x, ref y, w, "Show Axes", out r);
        if (Gui.Checkbox(r, "axes", "", ref axes)) world.ShowAxes = axes;

        y += 10;
        Gui.Text(x, y, "Click an object in the scene or the", Gui.TextDim);
        Gui.Text(x, y + 18, "hierarchy to edit it.", Gui.TextDim);
        y += 40;
    }

    private static void DrawObject(World world, GameObject go, float x, ref float y, float w)
    {
        Gui.SetScope(200);
        Row(x, ref y, w, "Name", out var r);
        string name = go.Name;
        if (Gui.TextField(r, "name", ref name) && name.Length > 0) go.Name = name;

        bool active = go.Active;
        Row(x, ref y, w, "Active", out r);
        if (Gui.Checkbox(r, "active", "", ref active)) go.Active = active;

        y += 4;
        SectionHeader(x, ref y, w, "Transform");
        var t = go.Transform;

        Row(x, ref y, w, "Position", out r);
        var pos = t.Position;
        if (Gui.Vec3Field(r, "pos", ref pos, 0.05f)) t.Position = pos;

        Row(x, ref y, w, "Rotation", out r);
        var rot = t.EulerAngles;
        if (Gui.Vec3Field(r, "rot", ref rot, 0.5f)) t.EulerAngles = rot;

        Row(x, ref y, w, "Scale", out r);
        var scale = t.Scale;
        if (Gui.Vec3Field(r, "scale", ref scale, 0.02f)) t.Scale = scale;

        var components = go.Components.ToList();
        for (int ci = 0; ci < components.Count; ci++)
        {
            var c = components[ci];
            y += 6;
            Gui.SetScope(300 + ci);

            var head = new Rectangle(x - 4, y, w + 8, Gui.Row);
            Rl.DrawRectangleRec(head, Gui.PanelHeader);
            Gui.TextLeft(head, "::  " + c.GetType().Name, Gui.TextCol, 6);
            ArmDrag(new Rectangle(head.X, head.Y, head.Width - 28, head.Height), c.GetType(), c);
            if (Gui.Button(new Rectangle(head.X + head.Width - 24, head.Y + 1, 22, Gui.Row - 2), "x"))
            {
                var target = c;
                _pending = () => { go.RemoveComponent(target); Say($"Removed {target.GetType().Name}"); };
            }
            y += Gui.Row + 4;

            foreach (var p in ComponentRegistry.Properties(c.GetType()))
                DrawProperty(x, ref y, w, p, c);
        }

        y += 10;
        Gui.SetScope(900);
        if (Gui.Button(new Rectangle(x, y, w, 26), _addMenu ? "Add Component  (close)" : "Add Component")) _addMenu = !_addMenu;
        y += 30;
        if (_addMenu)
        {
            foreach (var type in ComponentRegistry.Types)
            {
                var itemRect = new Rectangle(x + 12, y, w - 12, Gui.Row);
                ArmDrag(itemRect, type, null);
                if (Gui.Button(itemRect, type.Name) && !_dragSwallowClick)
                {
                    var chosen = type;
                    _pending = () => { go.AddComponent(chosen); Say($"Added {chosen.Name}"); };
                    _addMenu = false;
                }
                y += Gui.Row + 2;
            }
        }
    }

    // ---- component drag and drop ----------------------------------------------------------

    private static void ArmDrag(Rectangle area, Type type, Component? source)
    {
        if (_dragActive || ModalOpen) return;
        if (Gui.IsHot(area) && Gui.LeftPressed && !Gui.Busy)
        {
            _dragArmed = true;
            _dragType = type;
            _dragSource = source;
            _dragFrom = Gui.Mouse;
        }
    }

    /// <summary>Call once per UI frame after the panels are drawn: runs the drag, draws its ghost, performs the drop.</summary>
    private static void UpdateDrag(World world)
    {
        if (_dragArmed && !_dragActive && Gui.LeftDown && Vector2.Distance(Gui.Mouse, _dragFrom) > 6f)
            _dragActive = true;

        if (!_dragActive && !_dragArmed) { _dragSwallowClick = false; return; }

        if (_dragActive)
        {
            var label = _dragType?.Name ?? "";
            int w = Rl.MeasureText(label, Gui.FontSize) + 16;
            var ghost = new Rectangle(Gui.Mouse.X + 12, Gui.Mouse.Y + 8, w, Gui.Row);
            Rl.DrawRectangleRec(ghost, Gui.Accent with { A = 220 });
            Rl.DrawRectangleLinesEx(ghost, 1f, Color.White);
            Gui.TextLeft(ghost, label, Color.White, 8);
        }

        if (Gui.LeftReleased || !Gui.LeftDown)
        {
            if (_dragActive && _dragType is not null)
            {
                GameObject? target = _dropRow;
                if (target is null && !MouseOverUi && world.TryGetCamera(out var cam))
                    target = world.Raycast(Rl.GetScreenToWorldRay(Gui.RawMouse, cam), out _);

                if (target is null) Say("Drop the component on an object (Hierarchy row or in the scene).");
                else if (ReferenceEquals(target, Selected) && _dragSource is not null && Selected.Components.Contains(_dragSource))
                    Say("That component is already on this object.");
                else
                {
                    var type = _dragType;
                    var source = _dragSource;
                    var dest = target;
                    _pending = () => { CopyComponent(dest, type, source); Selected = dest; Say($"Added {type.Name} to {dest.Name}"); };
                }
            }
            _dragSwallowClick = _dragActive;
            _dragActive = _dragArmed = false;
            _dragType = null; _dragSource = null;
        }
    }

    private static void CopyComponent(GameObject target, Type type, Component? source)
    {
        var copy = target.AddComponent(type);
        if (source is null) return;
        foreach (var p in ComponentRegistry.Properties(type))
        {
            try
            {
                var v = p.GetValue(source);
                if (v is not null) p.SetValue(copy, v);
            }
            catch (Exception ex) when (ex is TargetInvocationException or ArgumentException)
            {
                Say($"{p.Name}: could not copy ({ex.InnerException?.Message ?? ex.Message})");
            }
        }
    }

    // ---- files dropped on the window ------------------------------------------------------

    private static void HandleDroppedFiles(World world)
    {
        if (!Rl.IsFileDropped()) return;
        var files = Rl.GetDroppedFiles();
        if (ModalOpen || !Visible) return;

        GameObject? under = _dropRow;
        if (under is null && !MouseOverUi && world.TryGetCamera(out var cam))
            under = world.Raycast(Rl.GetScreenToWorldRay(Gui.RawMouse, cam), out _);

        foreach (var file in files)
        {
            try
            {
                if (Assets.IsTexture(file)) DropTexture(file, under ?? Selected);
                else if (Assets.IsModel(file)) DropModel(world, file);
                else Say($"Cannot import {Path.GetFileName(file)}: use .png .jpg .bmp .tga (textures) or .fbx .obj (models).");
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or UnauthorizedAccessException or InvalidOperationException)
            {
                Say($"Import failed ({Path.GetFileName(file)}): {ex.Message}");
            }
        }
    }

    private static void DropTexture(string file, GameObject? target)
    {
        string rel = Assets.Import(file, "Textures");
        TextureCache.Refresh();
        if (target?.GetComponent<MeshRenderer>() is { } mr)
        {
            mr.TexturePath = rel;
            Selected = target;
            Say($"Texture {rel} applied to {target.Name}");
        }
        else Say($"Imported {rel}. Drop it on an object, or type it into Texture Path.");
    }

    private static void DropModel(World world, string file)
    {
        var (model, texture) = ModelLoader.Import(file);
        var go = world.Instantiate(Path.GetFileNameWithoutExtension(file), SpawnPoint(world));
        var mr = new MeshRenderer { ModelPath = model, Specular = 0.1f };
        if (texture is not null) mr.TexturePath = texture;
        go.AddComponent(mr);
        Selected = go;
        Say(texture is null ? $"Imported {model}" : $"Imported {model} with texture {texture}");
    }

    private static readonly string[] NoAssets = { "(no files in Assets)" };

    private static void DrawProperty(float x, ref float y, float w, PropertyInfo p, object target)
    {
        object? value;
        try { value = p.GetValue(target); }
        catch (TargetInvocationException) { return; }

        Row(x, ref y, w, ComponentRegistry.Pretty(p.Name), out var r);
        string key = p.Name;
        object? updated = null;

        switch (value)
        {
            case float f:
                if (Gui.FloatField(r, key, ref f, 0.05f)) updated = f;
                break;
            case int i:
            {
                float tmp = i;
                if (Gui.FloatField(r, key, ref tmp, 0.2f, integer: true)) updated = (int)tmp;
                break;
            }
            case bool b:
                if (Gui.Checkbox(r, key, "", ref b)) updated = b;
                break;
            case Vector3 v:
                if (Gui.Vec3Field(r, key, ref v, 0.05f)) updated = v;
                break;
            case Color c:
                if (Gui.ColorField(r, key, ref c)) updated = c;
                break;
            case string s when IsAssetProperty(p):
                updated = AssetField(r, key, p, s);
                break;
            case string s:
                if (Gui.TextField(r, key, ref s)) updated = s;
                break;
            case null when IsAssetProperty(p):
                updated = AssetField(r, key, p, "");
                break;
            case null when p.PropertyType == typeof(string):
            {
                string empty = "";
                if (Gui.TextField(r, key, ref empty)) updated = empty;
                break;
            }
        }

        if (updated is not null)
        {
            try { p.SetValue(target, updated); }
            catch (TargetInvocationException ex) { Say($"{p.Name}: {ex.InnerException?.Message ?? ex.Message}"); }
        }
    }

    private static bool IsAssetProperty(PropertyInfo p) => p.Name is "TexturePath" or "ModelPath";

    /// <summary>Text field plus a "..." menu that lists the matching files in the Assets folder.</summary>
    private static string? AssetField(Rectangle r, string key, PropertyInfo p, string current)
    {
        string? result = null;
        var field = new Rectangle(r.X, r.Y, r.Width - 30, r.Height);
        var pick = new Rectangle(r.X + r.Width - 28, r.Y, 28, r.Height);
        if (Gui.TextField(field, key, ref current, "(none)")) result = current;

        bool models = p.Name == "ModelPath";
        var assets = Assets.List(textures: !models, models: models);
        var items = assets.Count > 0 ? new[] { "(none)" }.Concat(assets).ToArray() : NoAssets;
        int chosen = Gui.Menu(pick, key + ".pick", "...", items);
        if (chosen == 0 && assets.Count > 0) result = "";
        else if (chosen > 0 && chosen <= assets.Count) result = assets[chosen - 1];
        return result;
    }

    private static void DrawStatus(World world, Rectangle bar)
    {
        Rl.DrawRectangleRec(bar, Gui.PanelHeader);
        string mode = world.PlayMode ? "PLAYING" : "EDIT MODE";
        Gui.Text(bar.X + 10, bar.Y + 5, mode, world.PlayMode ? new Color(110, 220, 120, 255) : new Color(240, 200, 80, 255));
        string scene = $"Scene: {SceneManager.CurrentName}{(_dirty && !world.PlayMode ? "*" : "")}";
        Gui.Text(bar.X + 110, bar.Y + 5, scene, new Color(150, 200, 255, 255));
        float sceneW = Rl.MeasureText(scene, Gui.FontSize);
        float statusX = bar.X + 124 + sceneW;
        float sliderX = bar.Width - 340f;
        Gui.Text(statusX, bar.Y + 5, Gui.FitText(_status, sliderX - statusX - 12f), Gui.TextCol);
        string help = "RMB+WASD fly | LMB select/drag | Del | Ctrl+D | Ctrl+S | Ctrl+Shift+B build | F1 hide";
        int hw = Rl.MeasureText(help, Gui.FontSize);
        float sx = bar.Width - 340f;
        if (bar.Width > hw + 1080) Gui.Text(sx - hw - 14, bar.Y + 5, help, Gui.TextDim);

        // interface scale slider (applied when released so the layout does not move under the mouse)
        Gui.SetScope(9);
        Gui.Text(sx, bar.Y + 5, "UI Scale", Gui.TextDim);
        var slider = new Rectangle(sx + 68, bar.Y + 2, 120, bar.Height - 4);
        float shown = _scalePreview > 0f ? _scalePreview : Gui.Scale;
        if (Gui.Slider(slider, "uiscale", ref shown, Gui.MinScale, Gui.MaxScale))
            _scalePreview = Math.Clamp(MathF.Round(shown * 20f) / 20f, Gui.MinScale, Gui.MaxScale);
        string pct = $"{(int)MathF.Round(shown * 100f)}%";
        if (Gui.EffectiveScale + 0.01f < Gui.Scale && _scalePreview < 0f) pct += $" (fits {(int)MathF.Round(Gui.EffectiveScale * 100f)}%)";
        Gui.Text(sx + 196, bar.Y + 5, pct, Gui.TextCol);
    }

    // ============================================================================ 3D overlays ==

    /// <summary>The scene's boundary: a rectangle on the ground plus corner posts.</summary>
    private static void DrawSceneBounds(World world)
    {
        if (!world.ShowBounds) return;
        float hx = world.SizeX / 2f, hz = world.SizeZ / 2f;
        var col = new Color(80, 220, 230, 255);
        var a = new Vector3(-hx, 0.02f, -hz); var b = new Vector3(hx, 0.02f, -hz);
        var c = new Vector3(hx, 0.02f, hz); var d = new Vector3(-hx, 0.02f, hz);
        Rl.DrawLine3D(a, b, col); Rl.DrawLine3D(b, c, col); Rl.DrawLine3D(c, d, col); Rl.DrawLine3D(d, a, col);
        foreach (var p in new[] { a, b, c, d }) Rl.DrawLine3D(p, p + Vector3.UnitY * 4f, col);
    }

    private static Color PortalColor(ScenePortal portal, float distance) =>
        distance <= portal.Radius ? new Color(90, 255, 130, 255)
        : portal.RequireInteract ? new Color(255, 170, 60, 255) : new Color(255, 230, 80, 255);

    /// <summary>Debug view of every ScenePortal: trigger sphere (3 rings), a beam, and a line toward the camera.</summary>
    private static void DrawPortalGizmos(World world, Camera3D camera)
    {
        foreach (var go in world.Objects)
        {
            if (!go.Active || go.Destroyed || go.GetComponent<ScenePortal>() is not { ShowDebug: true } portal) continue;
            var p = go.Transform.Position;
            float dist = Vector3.Distance(camera.Position, p);
            var col = PortalColor(portal, dist);
            float r = MathF.Max(0.05f, portal.Radius);

            Rl.DrawCircle3D(p, r, Vector3.UnitX, 90f, col);      // horizontal ring
            Rl.DrawCircle3D(p, r, Vector3.UnitY, 0f, col);       // ring in the XY plane
            Rl.DrawCircle3D(p, r, Vector3.UnitY, 90f, col);      // ring in the ZY plane
            Rl.DrawLine3D(p, p + Vector3.UnitY * (r + 0.6f), col);                       // beam so it can be seen from far away
            Rl.DrawLine3D(p - Vector3.UnitX * 0.3f, p + Vector3.UnitX * 0.3f, col);
            Rl.DrawLine3D(p - Vector3.UnitZ * 0.3f, p + Vector3.UnitZ * 0.3f, col);

            // line from the portal toward the player's eye (slightly below and ahead so it is visible)
            var eye = camera.Position + Vector3.Normalize(camera.Target - camera.Position) * 0.8f - Vector3.UnitY * 0.3f;
            Rl.DrawLine3D(p, eye, dist <= portal.Radius ? col : col with { A = 110 });
        }
    }

    /// <summary>Text above each ScenePortal: target scene, radius, current distance and the key to press.</summary>
    private static void DrawPortalLabels(World world)
    {
        if (!world.TryGetCamera(out var camera)) return;
        foreach (var go in world.Objects)
        {
            if (!go.Active || go.Destroyed || go.GetComponent<ScenePortal>() is not { ShowDebug: true } portal) continue;
            var p = go.Transform.Position + Vector3.UnitY * (portal.Radius + 0.8f);
            var toPoint = p - camera.Position;
            if (Vector3.Dot(toPoint, camera.Target - camera.Position) <= 0f) continue;     // behind the camera

            var s = Rl.GetWorldToScreen(p, camera) / Gui.EffectiveScale;
            float dist = Vector3.Distance(camera.Position, go.Transform.Position);
            string target = portal.TargetScene.Length > 0 ? SceneManager.NameOf(portal.TargetScene) : "(no target)";
            string how = portal.RequireInteract ? $"[{portal.Key}]" : "auto";
            string text = $"Portal -> {target}   r={portal.Radius:0.#}  d={dist:0.0}  {how}{(dist <= portal.Radius ? "  IN RANGE" : "")}";
            if (!world.PlayMode && dist <= portal.Radius) text += "  (press Play F5 to use it)";
            int w = Rl.MeasureText(text, Gui.FontSize);
            var box = new Rectangle(s.X - w / 2f - 6, s.Y - 12, w + 12, 22);
            Rl.DrawRectangleRec(box, new Color(0, 0, 0, 170));
            Rl.DrawRectangleLinesEx(box, 1f, PortalColor(portal, dist));
            Gui.Text(box.X + 6, box.Y + 3, text, Color.White);
        }
    }

    private static void OnRender3D(World world, Camera3D camera)
    {
        if (!Visible) return;

        // markers for objects that have no mesh (cameras, empties)
        foreach (var go in world.Objects)
        {
            if (!go.Active || go.Destroyed || go.GetComponent<MeshRenderer>() is not null) continue;
            var p = go.Transform.Position;
            var col = go.GetComponent<CameraComponent>() is not null ? Color.SkyBlue : Color.Gray;
            Rl.DrawLine3D(p - Vector3.UnitX * 0.25f, p + Vector3.UnitX * 0.25f, col);
            Rl.DrawLine3D(p - Vector3.UnitY * 0.25f, p + Vector3.UnitY * 0.25f, col);
            Rl.DrawLine3D(p - Vector3.UnitZ * 0.25f, p + Vector3.UnitZ * 0.25f, col);
            if (go.GetComponent<CameraComponent>() is not null)
                Rl.DrawLine3D(p, p + go.Transform.Forward * 0.9f, col);
        }

        DrawPortalGizmos(world, camera);
        DrawSceneBounds(world);

        if (Selected is not { Destroyed: false } sel) return;

        var (min, max) = World.WorldBounds(sel);
        DrawBox(min, max, new Color(255, 220, 60, 255));

        // gizmo drawn on top of everything
        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableDepthTest();

        var pos = sel.Transform.Position;
        float len = HandleLength(camera, pos);
        for (int i = 0; i < 3; i++)
        {
            var axis = AxisVector(i);
            bool lit = i == _hoverAxis || i == _dragAxis;
            var col = lit ? Color.Yellow : i == 0 ? new Color(230, 70, 70, 255) : i == 1 ? new Color(80, 210, 90, 255) : new Color(80, 130, 245, 255);
            var tip = pos + axis * len;
            var coneStart = pos + axis * (len * 0.82f);
            Rl.DrawCylinderEx(pos, coneStart, len * 0.012f, len * 0.012f, 8, col);
            Rl.DrawCylinderEx(coneStart, tip, len * 0.045f, 0f, 12, col);
        }

        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableDepthTest();
    }

    private static void DrawBox(Vector3 min, Vector3 max, Color c)
    {
        Vector3 P(int i) => new((i & 1) == 0 ? min.X : max.X, (i & 2) == 0 ? min.Y : max.Y, (i & 4) == 0 ? min.Z : max.Z);
        int[,] edges =
        {
            { 0, 1 }, { 2, 3 }, { 4, 5 }, { 6, 7 }, { 0, 2 }, { 1, 3 },
            { 4, 6 }, { 5, 7 }, { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 }
        };
        for (int e = 0; e < 12; e++) Rl.DrawLine3D(P(edges[e, 0]), P(edges[e, 1]), c);
    }

    // ================================================================================ gizmo math ==

    private static Vector3 AxisVector(int i) => i == 0 ? Vector3.UnitX : i == 1 ? Vector3.UnitY : Vector3.UnitZ;

    /// <summary>Gizmo arrows keep roughly the same size on screen.</summary>
    private static float HandleLength(Camera3D camera, Vector3 position) =>
        MathF.Max(0.8f, Vector3.Distance(camera.Position, position) * 0.17f);

    private static float PointRayDistance(Vector3 point, Vector3 origin, Vector3 dir) =>
        Vector3.Cross(dir, point - origin).Length();

    /// <summary>
    /// Closest point between a (normalized) mouse ray and an axis line: returns the parameter along the
    /// axis (<paramref name="s"/>) and the distance between the two lines. False if they are parallel.
    /// </summary>
    private static bool AxisParam(Vector3 rayOrigin, Vector3 rayDir, Vector3 axisOrigin, Vector3 axis, out float s, out float dist)
    {
        var w0 = axisOrigin - rayOrigin;
        float b = Vector3.Dot(axis, rayDir);
        float c = Vector3.Dot(axis, w0);
        float e = Vector3.Dot(rayDir, w0);
        float den = 1f - b * b;
        s = 0;
        dist = float.MaxValue;
        if (den < 1e-5f) return false;

        s = (b * e - c) / den;
        float u = e + s * b;
        if (u < 0f) return false;
        dist = Vector3.Distance(axisOrigin + axis * s, rayOrigin + rayDir * u);
        return true;
    }
}
