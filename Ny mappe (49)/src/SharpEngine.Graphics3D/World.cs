using System.Numerics;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace SharpEngine.Graphics3D;

/// <summary>The scene: owns all GameObjects, the light and the sky, and draws everything with OpenGL.</summary>
public sealed class World
{
    private readonly List<GameObject> _objects = new();
    private readonly List<GameObject> _added = new();
    private readonly List<GameObject> _removed = new();

    public IReadOnlyList<GameObject> Objects => _objects;
    public DirectionalLight Sun { get; } = new();
    public Color SkyTop { get; set; } = new Color(70, 120, 200, 255);
    public Color SkyBottom { get; set; } = new Color(190, 215, 240, 255);
    public bool ShowGrid { get; set; } = true;
    public bool ShowAxes { get; set; } = true;

    /// <summary>Draw chunk borders, names and the loaded area (F3 in the app).</summary>
    public bool ShowChunkDebug { get; set; }

    public const float MinSize = 4f, MaxSize = 2000f;

    /// <summary>Width (X) of this scene's world in units. The scene is a rectangle centred on the origin.</summary>
    public float SizeX { get => _sizeX; set => _sizeX = Math.Clamp(float.IsFinite(value) ? value : 60f, MinSize, MaxSize); }

    /// <summary>Depth (Z) of this scene's world in units.</summary>
    public float SizeZ { get => _sizeZ; set => _sizeZ = Math.Clamp(float.IsFinite(value) ? value : 60f, MinSize, MaxSize); }

    private float _sizeX = 60f, _sizeZ = 60f;

    /// <summary>Draw the scene boundary in the editor.</summary>
    public bool ShowBounds { get; set; } = true;

    /// <summary>While playing, keep the camera inside the scene boundary.</summary>
    public bool ClampCamera { get; set; } = true;

    /// <summary>The camera used while editing. The game camera takes over in play mode.</summary>
    public EditorCamera EditorView { get; } = new();

    /// <summary>True while the editor camera is the one that draws (edit mode with Scene View on).</summary>
    public bool UsesEditorView => !PlayMode && EditorView.Enabled;

    /// <summary>Gives the editor camera a starting place: the game camera's, or a spot above the origin.</summary>
    public void EnsureEditorView()
    {
        if (EditorView.Initialised) return;
        if (FindCamera() is { } cam)
        {
            EditorView.Set(cam.Transform.Position, cam.Transform.EulerAngles);
            EditorView.FieldOfView = cam.GetComponent<CameraComponent>()!.FieldOfView;
        }
        else EditorView.Set(new Vector3(0, 4, 10), new Vector3(-15, 0, 0));
    }

    /// <summary>Seconds since the world started updating.</summary>
    public float Time { get; private set; }

    public int TrianglesDrawn { get; private set; }

    /// <summary>Triangles skipped this frame because they were outside the camera's view (graphics-card renderer only).</summary>
    public int TrianglesCulled => GpuRenderer.TrianglesCulled;

    /// <summary>When false (editor edit mode) only components with RunInEditMode update, and Time stands still.</summary>
    public bool PlayMode
    {
        get => _playMode;
        set
        {
            if (_playMode && !value)
            {
                Streamer.Reset();   // neighbouring chunks only exist while playing
                _scripts?.Stop();   // world scripts live exactly as long as play
                _scripts = null;
            }
            if (value) Streamer.Failed = false;
            _playMode = value;
        }
    }

    private bool _playMode = true;
    private WorldScriptHost? _scripts;

    /// <summary>The running world scripts (empty when not playing).</summary>
    public IReadOnlyList<WorldScript> WorldScripts => _scripts?.Scripts ?? Array.Empty<WorldScript>();

    internal void NotifySceneLoaded(string scene) => _scripts?.SceneLoaded(scene);
    internal void NotifyMergeChanged(IReadOnlyCollection<string> merged) => _scripts?.MergeChanged(merged);

    /// <summary>Loads the neighbouring scenes of the World Map around the player (see <see cref="WorldStreamer"/>).</summary>
    public WorldStreamer Streamer { get; }

    /// <summary>The folders and sub-folders that organise this scene's objects.</summary>
    public SceneFolders Folders { get; }

    public World() { Streamer = new WorldStreamer(this); Folders = new SceneFolders(this); }

    /// <summary>Raised inside the 3D pass after the meshes are drawn (used by the editor for gizmos).</summary>
    public event Action<World, Camera3D>? Render3D;

    public GameObject Instantiate(string name, Vector3 position = default)
    {
        var go = new GameObject(name, this);
        go.Transform.Position = position;
        _added.Add(go);
        return go;
    }

    /// <summary>Creates a GameObject with a built-in mesh ("Cube", "Sphere", ...).</summary>
    public GameObject CreatePrimitive(string primitive, Color tint, Vector3 position = default)
    {
        if (!Primitives.TryCreate(primitive, out var mesh))
            throw new ArgumentException($"Unknown primitive '{primitive}'.", nameof(primitive));
        return CreateMesh(mesh.Name ?? primitive, mesh, tint, position);
    }

    /// <summary>Shortcut: a GameObject with a MeshRenderer already attached.</summary>
    public GameObject CreateMesh(string name, MeshData mesh, Color tint, Vector3 position = default)
    {
        var go = Instantiate(name, position);
        go.AddComponent(new MeshRenderer { Mesh = mesh, Tint = tint });
        return go;
    }

    public void Destroy(GameObject go)
    {
        if (go.Destroyed) return;
        go.Destroyed = true;
        _removed.Add(go);
    }

    /// <summary>The object that holds this scene's own components, or null when none was created yet.</summary>
    public GameObject? FindSceneObject()
    {
        foreach (var o in _objects) if (o.IsScene && o.Chunk is null && !o.Destroyed) return o;
        foreach (var o in _added) if (o.IsScene && o.Chunk is null && !o.Destroyed) return o;
        return null;
    }

    /// <summary>The scene's own component holder (created on first use). Pin components to the scene by adding them here.</summary>
    public GameObject GetSceneObject()
    {
        if (FindSceneObject() is { } existing) return existing;
        var go = Instantiate("Scene Components");
        go.IsScene = true;
        Flush();
        return go;
    }

    public GameObject? Find(string name)
    {
        foreach (var o in _objects) if (!o.Destroyed && o.Name == name) return o;
        foreach (var o in _added) if (!o.Destroyed && o.Name == name) return o;
        return null;
    }

    public void Update(float dt)
    {
        if (PlayMode) Time += dt;
        Flush();
        for (int i = 0; i < _objects.Count; i++)
        {
            var o = _objects[i];
            if (o.Active && !o.Destroyed) o.UpdateComponents(dt, PlayMode);
        }
        Flush();
        if (PlayMode) { _scripts ??= new WorldScriptHost(this); _scripts.Update(dt); }
        Flush();
        if (PlayMode && !ErrorLog.Try("world streaming", () => Streamer.Update(dt)))
        {
            Streamer.Reset();           // a broken chunk must not stop the game: carry on without streaming
            Streamer.Failed = true;
        }
        if (PlayMode && ClampCamera && !Streamer.Active) KeepCameraInside();
    }

    private void KeepCameraInside()
    {
        if (FindCamera() is not { } cam) return;
        var p = cam.Transform.Position;
        float hx = SizeX / 2f, hz = SizeZ / 2f;
        var c = new Vector3(Math.Clamp(p.X, -hx, hx), p.Y, Math.Clamp(p.Z, -hz, hz));
        if (c != p) cam.Transform.Position = c;
    }

    /// <summary>Removes every GameObject immediately and resets the clock.</summary>
    public void Clear()
    {
        Streamer.Reset();
        Streamer.Failed = false;
        foreach (var o in _objects.Concat(_added).ToList())
        {
            o.Destroyed = true;
            o.DestroyComponents();
        }
        _objects.Clear();
        _added.Clear();
        _removed.Clear();
        Folders.Reset();
        Time = 0f;
        SizeX = SizeZ = 60f;
        ShowBounds = true;
        ClampCamera = true;
    }

    /// <summary>Camera of the first active GameObject that has an enabled CameraComponent.</summary>
    public bool TryGetCamera(out Camera3D camera)
    {
        if (UsesEditorView) { EnsureEditorView(); camera = EditorView.ToRaylib(); return true; }
        var go = FindCamera();
        if (go is null) { camera = default; return false; }
        camera = go.GetComponent<CameraComponent>()!.ToRaylib();
        return true;
    }

    /// <summary>Nearest active object whose bounding box is hit by the ray (objects without a mesh use a small box).</summary>
    public GameObject? Raycast(Ray ray, out float distance)
    {
        GameObject? best = null;
        distance = float.MaxValue;
        var dir = Vector3.Normalize(ray.Direction);
        foreach (var o in _objects)
        {
            if (!o.Active || o.Destroyed || o.IsScene) continue;
            var (min, max) = WorldBounds(o);
            if (RayHitsBox(ray.Position, dir, min, max, out float t) && t < distance)
            {
                distance = t;
                best = o;
            }
        }
        return best;
    }

    /// <summary>World-space bounding box of an object (slightly padded so flat meshes can be hit).</summary>
    public static (Vector3 Min, Vector3 Max) WorldBounds(GameObject go)
    {
        const float pad = 0.03f;
        var pos = go.Transform.Position;
        if (go.GetComponent<MeshRenderer>() is not { Mesh: { } mesh })
            return (pos - new Vector3(0.25f), pos + new Vector3(0.25f));

        var (lo, hi) = mesh.Bounds;
        var m = go.Transform.WorldMatrix;
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3((i & 1) == 0 ? lo.X : hi.X, (i & 2) == 0 ? lo.Y : hi.Y, (i & 4) == 0 ? lo.Z : hi.Z);
            var w = Vector3.Transform(corner, m);
            min = Vector3.Min(min, w);
            max = Vector3.Max(max, w);
        }
        return (min - new Vector3(pad), max + new Vector3(pad));
    }

    private static bool RayHitsBox(Vector3 o, Vector3 d, Vector3 min, Vector3 max, out float t)
    {
        float tmin = 0f, tmax = float.MaxValue;
        for (int i = 0; i < 3; i++)
        {
            float oi = Axis(o, i), di = Axis(d, i), lo = Axis(min, i), hi = Axis(max, i);
            if (MathF.Abs(di) < 1e-8f)
            {
                if (oi < lo || oi > hi) { t = 0; return false; }
            }
            else
            {
                float t1 = (lo - oi) / di, t2 = (hi - oi) / di;
                if (t1 > t2) (t1, t2) = (t2, t1);
                tmin = MathF.Max(tmin, t1);
                tmax = MathF.Min(tmax, t2);
                if (tmin > tmax) { t = 0; return false; }
            }
        }
        t = tmin;
        return true;
    }

    private static float Axis(Vector3 v, int i) => i == 0 ? v.X : i == 1 ? v.Y : v.Z;

    /// <summary>Draws sky, grid and meshes. Call between BeginDrawing and EndDrawing.</summary>
    public void Render()
    {
        int w = Rl.GetScreenWidth(), h = Rl.GetScreenHeight();
        Rl.DrawRectangleGradientV(0, 0, w, h, SkyTop, SkyBottom);

        Camera3D camera;
        if (UsesEditorView) { EnsureEditorView(); camera = EditorView.ToRaylib(); }
        else
        {
            var camObject = FindCamera();
            if (camObject is null) return;
            camera = camObject.GetComponent<CameraComponent>()!.ToRaylib();
        }

        Rl.BeginMode3D(camera);
        try
        {
            Rlgl.DisableBackfaceCulling();

            if (ShowGrid) Rl.DrawGrid(40, 1f);
            if (ShowAxes)
            {
                Rl.DrawLine3D(Vector3.Zero, new Vector3(2, 0, 0), Color.Red);
                Rl.DrawLine3D(Vector3.Zero, new Vector3(0, 2, 0), Color.Green);
                Rl.DrawLine3D(Vector3.Zero, new Vector3(0, 0, 2), Color.Blue);
            }

            GpuRenderer.BeginFrame(camera, Sun, h > 0 ? (float)w / h : 1f);
            int tris = 0;
            foreach (var o in _objects)
            {
                if (!o.Active || o.Destroyed) continue;
                foreach (var c in o.Components)
                    if (c is MeshRenderer { Enabled: true } mr)
                        tris += mr.Draw(Sun, camera.Position);
            }
            GpuRenderer.EndFrame();
            TrianglesDrawn = tris;

            if (ShowChunkDebug) Streamer.DrawDebug();
            Render3D?.Invoke(this, camera);
        }
        finally
        {
            Rl.EndMode3D();
            Rlgl.EnableBackfaceCulling();
        }
    }

    /// <summary>
    /// Describes the frame to a <see cref="IFrameSink"/> instead of drawing it with Raylib (the browser player uses this).
    /// Returns false when there is no camera.
    /// </summary>
    public bool Collect(IFrameSink sink)
    {
        var camObject = FindCamera();
        if (camObject is null) return false;
        var cam = camObject.GetComponent<CameraComponent>()!;
        var t = camObject.Transform;
        sink.BeginFrame(SkyTop, SkyBottom, t.Position, t.Position + t.Forward, t.Up, cam.FieldOfView);

        if (ShowGrid)
        {
            var gray = new Color(127, 127, 127, 255);
            var dark = new Color(80, 80, 80, 255);
            for (int i = -20; i <= 20; i++)
            {
                var c = i == 0 ? dark : gray;
                sink.Line(new Vector3(i, 0, -20), new Vector3(i, 0, 20), c);
                sink.Line(new Vector3(-20, 0, i), new Vector3(20, 0, i), c);
            }
        }
        if (ShowAxes)
        {
            sink.Line(Vector3.Zero, new Vector3(2, 0, 0), Color.Red);
            sink.Line(Vector3.Zero, new Vector3(0, 2, 0), Color.Green);
            sink.Line(Vector3.Zero, new Vector3(0, 0, 2), Color.Blue);
        }

        int tris = 0;
        foreach (var o in _objects)
        {
            if (!o.Active || o.Destroyed) continue;
            foreach (var c in o.Components)
                if (c is MeshRenderer { Enabled: true } mr)
                    tris += mr.Collect(sink, Sun, t.Position);
        }
        TrianglesDrawn = tris;
        sink.EndFrame();
        return true;
    }

    internal GameObject? FindCameraObject() => FindCamera();

    internal bool TryGetCameraPosition(out Vector3 position)
    {
        if (UsesEditorView) { EnsureEditorView(); position = EditorView.Position; return true; }
        var go = FindCamera();
        position = go?.Transform.Position ?? default;
        return go is not null;
    }

    private GameObject? FindCamera()
    {
        foreach (var o in _objects)
        {
            if (!o.Active || o.Destroyed) continue;
            if (o.GetComponent<CameraComponent>() is { Enabled: true }) return o;
        }
        return null;
    }

    /// <summary>Applies pending spawns and destroys now (normally done by Update).</summary>
    public void Flush()
    {
        if (_added.Count > 0)
        {
            _objects.AddRange(_added);
            _added.Clear();
        }
        if (_removed.Count > 0)
        {
            foreach (var o in _removed)
            {
                o.DestroyComponents();
                _objects.Remove(o);
            }
            _removed.Clear();
        }
    }
}
