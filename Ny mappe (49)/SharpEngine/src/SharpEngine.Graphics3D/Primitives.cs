using System.Numerics;
using Raylib_cs;

namespace SharpEngine.Graphics3D;

/// <summary>Ready-made meshes (like Unity's GameObject &gt; 3D Object menu). All are unit sized and centred on the origin.</summary>
public static class Primitives
{
    /// <summary>1x1x1 cube.</summary>
    public static MeshData Cube()
    {
        var b = new MeshBuilder();
        const float h = 0.5f;
        var c = Vector3.Zero;
        b.AddFlatQuad(new(-h, -h, h), new(h, -h, h), new(h, h, h), new(-h, h, h), outwardFrom: c);     // +Z
        b.AddFlatQuad(new(h, -h, -h), new(-h, -h, -h), new(-h, h, -h), new(h, h, -h), outwardFrom: c); // -Z
        b.AddFlatQuad(new(h, -h, h), new(h, -h, -h), new(h, h, -h), new(h, h, h), outwardFrom: c);     // +X
        b.AddFlatQuad(new(-h, -h, -h), new(-h, -h, h), new(-h, h, h), new(-h, h, -h), outwardFrom: c); // -X
        b.AddFlatQuad(new(-h, h, h), new(h, h, h), new(h, h, -h), new(-h, h, -h), outwardFrom: c);     // +Y
        b.AddFlatQuad(new(-h, -h, -h), new(h, -h, -h), new(h, -h, h), new(-h, -h, h), outwardFrom: c); // -Y
        return b.Build(true, "Cube");
    }

    /// <summary>1x1 quad in the XZ plane facing +Y.</summary>
    public static MeshData Plane()
    {
        var b = new MeshBuilder();
        b.AddFlatQuad(new(-0.5f, 0, 0.5f), new(0.5f, 0, 0.5f), new(0.5f, 0, -0.5f), new(-0.5f, 0, -0.5f), outwardFrom: new Vector3(0, -1, 0));
        return b.Build(true, "Plane");
    }

    /// <summary>1x1 ground divided into tiles x tiles squares coloured like a checkerboard.</summary>
    public static MeshData CheckerPlane(int tiles, Color a, Color b2)
    {
        var b = new MeshBuilder();
        float step = 1f / tiles;
        for (int z = 0; z < tiles; z++)
            for (int x = 0; x < tiles; x++)
            {
                float x0 = -0.5f + x * step, z0 = -0.5f + z * step;
                var col = ((x + z) & 1) == 0 ? a : b2;
                b.AddFlatQuad(new(x0, 0, z0 + step), new(x0 + step, 0, z0 + step), new(x0 + step, 0, z0), new(x0, 0, z0),
                    col, outwardFrom: new Vector3(0, -1, 0));
            }
        return b.Build();
    }

    /// <summary>The default checkerboard floor (named so it can be saved).</summary>
    public static MeshData Ground()
    {
        var m = CheckerPlane(20, new Color(120, 124, 132, 255), new Color(92, 96, 104, 255));
        return new MeshData { Positions = m.Positions, Normals = m.Normals, Colors = m.Colors, UVs = m.UVs, Indices = m.Indices, Name = "Ground" };
    }

    /// <summary>Smooth UV sphere, diameter 1.</summary>
    public static MeshData Sphere(int segments = 32, int rings = 16)
    {
        var b = new MeshBuilder();
        for (int r = 0; r <= rings; r++)
        {
            float v = r / (float)rings;
            float phi = v * MathF.PI;
            for (int s = 0; s <= segments; s++)
            {
                float theta = s / (float)segments * MathF.Tau;
                var n = new Vector3(MathF.Sin(phi) * MathF.Cos(theta), MathF.Cos(phi), MathF.Sin(phi) * MathF.Sin(theta));
                b.AddVertex(n * 0.5f, n, null, new Vector2(s / (float)segments, v));
            }
        }
        int stride = segments + 1;
        for (int r = 0; r < rings; r++)
            for (int s = 0; s < segments; s++)
            {
                int i0 = r * stride + s, i1 = i0 + 1, i2 = i0 + stride, i3 = i2 + 1;
                b.AddTriangle(i0, i2, i1);
                b.AddTriangle(i1, i2, i3);
            }
        return b.Build(true, "Sphere");
    }

    /// <summary>Cylinder, diameter 1, height 1.</summary>
    public static MeshData Cylinder(int segments = 32)
    {
        var b = new MeshBuilder();
        for (int s = 0; s <= segments; s++)
        {
            float t = s / (float)segments * MathF.Tau;
            var n = new Vector3(MathF.Cos(t), 0, MathF.Sin(t));
            float u = s / (float)segments;
            b.AddVertex(n * 0.5f + new Vector3(0, -0.5f, 0), n, null, new Vector2(u, 1f));
            b.AddVertex(n * 0.5f + new Vector3(0, 0.5f, 0), n, null, new Vector2(u, 0f));
        }
        for (int s = 0; s < segments; s++)
        {
            int i = s * 2;
            b.AddTriangle(i, i + 1, i + 2);
            b.AddTriangle(i + 1, i + 3, i + 2);
        }
        foreach (float y in new[] { 0.5f, -0.5f })
        {
            var n = new Vector3(0, y > 0 ? 1 : -1, 0);
            int center = b.AddVertex(new Vector3(0, y, 0), n);
            int first = -1, prev = -1;
            for (int s = 0; s <= segments; s++)
            {
                float t = s / (float)segments * MathF.Tau;
                int v = b.AddVertex(new Vector3(MathF.Cos(t) * 0.5f, y, MathF.Sin(t) * 0.5f), n);
                if (first < 0) first = v;
                if (prev >= 0) b.AddTriangle(center, prev, v);
                prev = v;
            }
        }
        return b.Build(true, "Cylinder");
    }

    /// <summary>Four-sided pyramid, base 1x1, height 1.</summary>
    public static MeshData Pyramid()
    {
        var b = new MeshBuilder();
        var apex = new Vector3(0, 0.5f, 0);
        var c0 = new Vector3(-0.5f, -0.5f, -0.5f);
        var c1 = new Vector3(0.5f, -0.5f, -0.5f);
        var c2 = new Vector3(0.5f, -0.5f, 0.5f);
        var c3 = new Vector3(-0.5f, -0.5f, 0.5f);
        var inside = Vector3.Zero;
        b.AddFlatTriangle(c0, c1, apex, outwardFrom: inside);
        b.AddFlatTriangle(c1, c2, apex, outwardFrom: inside);
        b.AddFlatTriangle(c2, c3, apex, outwardFrom: inside);
        b.AddFlatTriangle(c3, c0, apex, outwardFrom: inside);
        b.AddFlatQuad(c0, c1, c2, c3, outwardFrom: inside);
        return b.Build(true, "Pyramid");
    }

    /// <summary>A single flat triangle in the XY plane with red, green and blue corners (visible from both sides).</summary>
    public static MeshData ColorTriangle()
    {
        var b = new MeshBuilder();
        b.AddFlatTriangle(new(0f, 0.9f, 0f), new(-1f, -0.7f, 0f), new(1f, -0.7f, 0f),
            new Color(255, 40, 40, 255), new Color(40, 255, 40, 255), new Color(60, 90, 255, 255),
            outwardFrom: new Vector3(0, 0, -1));
        return b.Build(true, "Triangle");
    }

    /// <summary>Names accepted by <see cref="TryCreate"/>.</summary>
    public static readonly string[] Names = { "Cube", "Sphere", "Cylinder", "Pyramid", "Plane", "Triangle", "Ground" };

    public static bool TryCreate(string name, out MeshData mesh)
    {
        MeshData? m = name.ToLowerInvariant() switch
        {
            "cube" => Cube(),
            "sphere" => Sphere(),
            "cylinder" => Cylinder(),
            "pyramid" => Pyramid(),
            "plane" => Plane(),
            "triangle" => ColorTriangle(),
            "ground" => Ground(),
            _ => null
        };
        mesh = m!;
        return m is not null;
    }
}
