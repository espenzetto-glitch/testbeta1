using System.Numerics;
using Raylib_cs;

namespace SharpEngine.Graphics3D;

/// <summary>CPU-side triangle mesh: positions, normals, optional per-vertex colours, triangle indices.</summary>
public sealed class MeshData
{
    public Vector3[] Positions { get; init; } = Array.Empty<Vector3>();
    public Vector3[] Normals { get; init; } = Array.Empty<Vector3>();
    public Color[]? Colors { get; init; }

    /// <summary>Texture coordinates (0..1, v pointing down like image rows). Null = none.</summary>
    public Vector2[]? UVs { get; init; }
    public int[] Indices { get; init; } = Array.Empty<int>();

    /// <summary>Primitive name (e.g. "Cube") so scenes can be saved and re-created. Null for custom meshes.</summary>
    public string? Name { get; init; }

    private (Vector3, Vector3)? _bounds;

    /// <summary>Local-space axis-aligned bounding box.</summary>
    public (Vector3 Min, Vector3 Max) Bounds
    {
        get
        {
            if (_bounds is { } b) return b;
            if (Positions.Length == 0) return (_bounds = (Vector3.Zero, Vector3.Zero)).Value;
            var min = Positions[0];
            var max = Positions[0];
            foreach (var p in Positions)
            {
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
            return (_bounds = (min, max)).Value;
        }
    }

    public int TriangleCount => Indices.Length / 3;
}

/// <summary>Builds meshes incrementally.</summary>
public sealed class MeshBuilder
{
    private readonly List<Vector3> _p = new();
    private readonly List<Vector3> _n = new();
    private readonly List<Color> _c = new();
    private readonly List<Vector2> _uv = new();
    private readonly List<int> _i = new();

    /// <summary>Adds a vertex. Without <paramref name="uv"/> the texture coordinate is a box projection along the dominant normal axis.</summary>
    public int AddVertex(Vector3 position, Vector3 normal, Color? color = null, Vector2? uv = null)
    {
        _p.Add(position);
        _n.Add(normal);
        _c.Add(color ?? Color.White);
        _uv.Add(uv ?? BoxUV(position, normal));
        return _p.Count - 1;
    }

    private static Vector2 BoxUV(Vector3 p, Vector3 n)
    {
        float ax = MathF.Abs(n.X), ay = MathF.Abs(n.Y), az = MathF.Abs(n.Z);
        if (ay >= ax && ay >= az) return new Vector2(p.X + 0.5f, p.Z + 0.5f);
        if (ax >= az) return new Vector2(p.Z + 0.5f, 0.5f - p.Y);
        return new Vector2(p.X + 0.5f, 0.5f - p.Y);
    }

    public void AddTriangle(int a, int b, int c)
    {
        _i.Add(a); _i.Add(b); _i.Add(c);
    }

    /// <summary>Adds a flat-shaded triangle. If <paramref name="outwardFrom"/> is given, the normal faces away from it.</summary>
    public void AddFlatTriangle(Vector3 a, Vector3 b, Vector3 c, Color? ca = null, Color? cb = null, Color? cc = null, Vector3? outwardFrom = null)
    {
        var n = Vector3.Cross(b - a, c - a);
        n = n.LengthSquared() > 1e-12f ? Vector3.Normalize(n) : Vector3.UnitY;
        if (outwardFrom is { } o && Vector3.Dot(n, (a + b + c) / 3f - o) < 0) n = -n;
        AddTriangle(AddVertex(a, n, ca), AddVertex(b, n, cb), AddVertex(c, n, cc));
    }

    /// <summary>Adds a flat quad (a,b,c,d in order around the edge).</summary>
    public void AddFlatQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color? color = null, Vector3? outwardFrom = null)
    {
        AddFlatTriangle(a, b, c, color, color, color, outwardFrom);
        AddFlatTriangle(a, c, d, color, color, color, outwardFrom);
    }

    public MeshData Build(bool withColors = true, string? name = null) => new()
    {
        Positions = _p.ToArray(),
        Normals = _n.ToArray(),
        Colors = withColors ? _c.ToArray() : null,
        UVs = _uv.ToArray(),
        Indices = _i.ToArray(),
        Name = name
    };
}
