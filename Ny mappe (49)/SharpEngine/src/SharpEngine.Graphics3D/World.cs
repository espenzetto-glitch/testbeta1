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

    /// <summary>Seconds since the world started updating.</summary>
    public float Time { get; private set; }

    public int TrianglesDrawn { get; private set; }

    /// <summary>When false (editor edit mode) only components with RunInEditMode update, and Time stands still.</summary>
    public bool PlayMode { get; set; } = true;

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
        if (PlayMode && ClampCamera) KeepCameraInside();
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
        foreach (var o in _objects.Concat(_added).ToList())
        {
            o.Destroyed = true;
            o.DestroyComponents();
        }
        _objects.Clear();
        _added.Clear();
        _removed.Clear();
        Time = 0f;
        SizeX = SizeZ = 60f;
        ShowBounds = true;
        ClampCamera = true;
    }

    /// <summary>Camera of the first active GameObject that has an enabled CameraComponent.</summary>
    public bool TryGetCamera(out Camera3D camera)
    {
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
            if (!o.Active || o.Destroyed) continue;
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

        var camObject = FindCamera();
        if (camObject is null) return;
        var camera = camObject.GetComponent<CameraComponent>()!.ToRaylib();

        Rl.BeginMode3D(camera);
        Rlgl.DisableBackfaceCulling();

        if (ShowGrid) Rl.DrawGrid(40, 1f);
        if (ShowAxes)
        {
            Rl.DrawLine3D(Vector3.Zero, new Vector3(2, 0, 0), Color.Red);
            Rl.DrawLine3D(Vector3.Zero, new Vector3(0, 2, 0), Color.Green);
            Rl.DrawLine3D(Vector3.Zero, new Vector3(0, 0, 2), Color.Blue);
        }

        int tris = 0;
        foreach (var o in _objects)
        {
            if (!o.Active || o.Destroyed) continue;
            foreach (var c in o.Components)
                if (c is MeshRenderer { Enabled: true } mr)
                    tris += mr.Draw(Sun, camera.Position);
        }
        TrianglesDrawn = tris;

        Render3D?.Invoke(this, camera);

        Rl.EndMode3D();
        Rlgl.EnableBackfaceCulling();
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
