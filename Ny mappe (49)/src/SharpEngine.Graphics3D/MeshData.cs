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

    /// <summary>Vertices added so far.</summary>
    public int VertexCount => _p.Count;

    /// <summary>Triangles added so far.</summary>
    public int TriangleCount => _i.Count / 3;

    public static Color Lerp(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        byte L(byte x, byte y) => (byte)(x + (y - x) * t + 0.5f);
        return new Color(L(a.R, b.R), L(a.G, b.G), L(a.B, b.B), L(a.A, b.A));
    }

    /// <summary>Adds a box (6 flat faces, 12 triangles) centred on <paramref name="center"/>.</summary>
    public void AddBox(Vector3 center, Vector3 size, Color? color = null)
    {
        var h = size / 2f;
        Vector3 P(float x, float y, float z) => center + new Vector3(x * h.X, y * h.Y, z * h.Z);
        AddFlatQuad(P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), color, center);     // front
        AddFlatQuad(P(1, -1, -1), P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1), color, center); // back
        AddFlatQuad(P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1), color, center); // left
        AddFlatQuad(P(1, -1, 1), P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), color, center);     // right
        AddFlatQuad(P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), P(-1, 1, -1), color, center);     // top
        AddFlatQuad(P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1), color, center); // bottom
    }

    /// <summary>Adds a smooth sphere. <paramref name="segments"/> = slices around (stacks are half of it): 32 gives 1,984 triangles.</summary>
    public void AddSphere(Vector3 center, float radius, int segments = 24, Color? color = null)
    {
        segments = Math.Max(segments, 3);
        int stacks = Math.Max(segments / 2, 2);
        int first = VertexCount;
        for (int i = 0; i <= stacks; i++)
        {
            float v = i / (float)stacks, phi = MathF.PI * v;
            for (int j = 0; j <= segments; j++)
            {
                float u = j / (float)segments, theta = MathF.PI * 2f * u;
                var n = new Vector3(MathF.Sin(phi) * MathF.Cos(theta), MathF.Cos(phi), MathF.Sin(phi) * MathF.Sin(theta));
                AddVertex(center + n * radius, n, color, new Vector2(u, v));
            }
        }
        int row = segments + 1;
        for (int i = 0; i < stacks; i++)
            for (int j = 0; j < segments; j++)
            {
                int a = first + i * row + j, b = a + 1, c = a + row, d = c + 1;
                if (i != 0) AddTriangle(a, c, b);
                if (i != stacks - 1) AddTriangle(b, c, d);
            }
    }

    /// <summary>
    /// Adds a height field centred on the origin: <paramref name="cellsX"/> x <paramref name="cellsZ"/> squares (two triangles each)
    /// of the given total size. <paramref name="height"/>(x, z) gives the height at a point; <paramref name="colorOf"/>(height) the colour.
    /// Normals are smooth. 500 x 500 cells is half a million triangles.
    /// </summary>
    public void AddGrid(float width, float depth, int cellsX, int cellsZ, Func<float, float, float> height, Func<float, Color>? colorOf = null)
    {
        cellsX = Math.Max(cellsX, 1); cellsZ = Math.Max(cellsZ, 1);
        float dx = width / cellsX, dz = depth / cellsZ;
        int first = VertexCount;
        for (int k = 0; k <= cellsZ; k++)
            for (int i = 0; i <= cellsX; i++)
            {
                float x = -width / 2f + i * dx, z = -depth / 2f + k * dz;
                float h = height(x, z);
                float hx = height(x + dx * 0.5f, z) - height(x - dx * 0.5f, z);
                float hz = height(x, z + dz * 0.5f) - height(x, z - dz * 0.5f);
                var n = Vector3.Normalize(new Vector3(-hx / dx, 1f, -hz / dz));
                AddVertex(new Vector3(x, h, z), n, colorOf?.Invoke(h), new Vector2(i / (float)cellsX, k / (float)cellsZ));
            }
        int row = cellsX + 1;
        for (int k = 0; k < cellsZ; k++)
            for (int i = 0; i < cellsX; i++)
            {
                int a = first + k * row + i, b = a + 1, c = a + row, d = c + 1;
                AddTriangle(a, c, b);
                AddTriangle(b, c, d);
            }
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
