using System.Numerics;
using Raylib_cs;

namespace SharpEngine.Graphics3D;

/// <summary>Draws a <see cref="MeshData"/> with the world's directional light (Blinn-Phong, computed per vertex).</summary>
public sealed class MeshRenderer : Component
{
    private Color[] _lit = Array.Empty<Color>();

    private MeshData? _mesh;
    private string? _primitive;

    public MeshData? Mesh
    {
        get => _mesh;
        set
        {
            _mesh = value;
            _primitive = value?.Name;
            if (value?.Name is not null) _modelPath = "";
        }
    }

    private string _modelPath = "";

    /// <summary>Model file in the Assets folder (.fbx / .obj), e.g. "Assets/Models/robot.fbx". Replaces the built-in mesh.</summary>
    public string ModelPath
    {
        get => _modelPath;
        set
        {
            value ??= "";
            if (value.Length == 0) { _modelPath = ""; return; }
            try
            {
                var mesh = ModelLoader.Load(value);
                _modelPath = value;
                _mesh = mesh;
                _primitive = null;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or UnauthorizedAccessException or ArgumentException)
            {
                Console.Error.WriteLine($"[model] {value}: {ex.Message}");
                LastModelError = ex.Message;
                throw new InvalidOperationException(ex.Message, ex);
            }
        }
    }

    /// <summary>Why the last ModelPath assignment failed (shown by the editor).</summary>
    public static string? LastModelError { get; private set; }

    /// <summary>Image in the Assets folder, e.g. "Assets/Textures/brick.png". Empty = no texture.</summary>
    public string TexturePath { get; set; } = "";

    /// <summary>How many times the texture repeats across the mesh.</summary>
    public float TileU { get; set; } = 1f;
    public float TileV { get; set; } = 1f;

    /// <summary>Name of the built-in mesh (Cube, Sphere, ...). Setting it swaps the mesh.</summary>
    public string? PrimitiveName
    {
        get => _primitive;
        set
        {
            if (value is null) { _primitive = null; _mesh = null; }
            else if (Primitives.TryCreate(value, out var m)) { _mesh = m; _primitive = m.Name; _modelPath = ""; }
        }
    }
    public Color Tint { get; set; } = Color.White;

    /// <summary>Skip lighting and draw the plain colour.</summary>
    public bool Unlit { get; set; }

    /// <summary>Light both sides of every triangle (useful for flat shapes such as the triangle).</summary>
    public bool DoubleSided { get; set; } = true;

    public float Specular { get; set; } = 0.25f;
    public float Shininess { get; set; } = 32f;

    /// <summary>Submits the mesh to the GPU batch. Returns the triangle count.</summary>
    internal int Draw(DirectionalLight sun, Vector3 cameraPosition)
    {
        var mesh = Mesh;
        if (mesh is null || mesh.Indices.Length == 0) return 0;

        int vc = mesh.Positions.Length;
        if (_lit.Length < vc) _lit = new Color[vc];
        var world = new Vector3[vc];

        var t = Transform;
        var matrix = t.WorldMatrix;
        var rot = t.Rotation;
        var scale = t.Scale;
        var invScale = new Vector3(
            MathF.Abs(scale.X) > 1e-6f ? 1f / scale.X : 0f,
            MathF.Abs(scale.Y) > 1e-6f ? 1f / scale.Y : 0f,
            MathF.Abs(scale.Z) > 1e-6f ? 1f / scale.Z : 0f);

        var toLight = Vector3.Normalize(-sun.Direction);
        var sunColor = new Vector3(sun.Color.R, sun.Color.G, sun.Color.B) / 255f * sun.Intensity;
        var ambient = sun.Ambient;

        for (int i = 0; i < vc; i++)
        {
            var p = Vector3.Transform(mesh.Positions[i], matrix);
            world[i] = p;

            var baseCol = mesh.Colors is { } cols ? cols[i] : Color.White;
            var albedo = new Vector3(
                baseCol.R * Tint.R, baseCol.G * Tint.G, baseCol.B * Tint.B) / (255f * 255f);
            float alpha = baseCol.A * Tint.A / 255f;

            Vector3 rgb;
            if (Unlit)
            {
                rgb = albedo;
            }
            else
            {
                var n = Vector3.Transform(mesh.Normals[i] * invScale, rot);
                n = n.LengthSquared() > 1e-12f ? Vector3.Normalize(n) : Vector3.UnitY;

                var viewDir = cameraPosition - p;
                viewDir = viewDir.LengthSquared() > 1e-12f ? Vector3.Normalize(viewDir) : Vector3.UnitY;
                if (DoubleSided && Vector3.Dot(n, viewDir) < 0f) n = -n;

                float diff = MathF.Max(Vector3.Dot(n, toLight), 0f);
                var color = albedo * (new Vector3(ambient) + sunColor * diff);

                if (diff > 0f && Specular > 0f)
                {
                    var h = Vector3.Normalize(toLight + viewDir);
                    float spec = MathF.Pow(MathF.Max(Vector3.Dot(n, h), 0f), Shininess) * Specular;
                    color += sunColor * spec;
                }
                rgb = color;
            }

            _lit[i] = new Color(ToByte(rgb.X), ToByte(rgb.Y), ToByte(rgb.Z), ToByte(alpha));
        }

        int[] idx = mesh.Indices;
        var uvs = mesh.UVs;
        bool textured = uvs is { Length: > 0 } && TexturePath.Length > 0 && TextureCache.TryGet(TexturePath, out _);
        Raylib_cs.Texture2D tex = default;
        if (textured) { TextureCache.TryGet(TexturePath, out tex); Rlgl.SetTexture(tex.Id); }

        Rlgl.CheckRenderBatchLimit(idx.Length);
        Rlgl.Begin(DrawMode.Triangles);
        foreach (int i in idx)
        {
            var c = _lit[i];
            var p = world[i];
            Rlgl.Color4ub(c.R, c.G, c.B, c.A);
            if (textured) Rlgl.TexCoord2f(uvs![i].X * TileU, uvs[i].Y * TileV);
            Rlgl.Vertex3f(p.X, p.Y, p.Z);
        }
        Rlgl.End();
        if (textured) Rlgl.SetTexture(0);

        return idx.Length / 3;
    }

    private static byte ToByte(float v) => (byte)Math.Clamp(v * 255f + 0.5f, 0f, 255f);
}
