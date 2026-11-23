using System.Numerics;

namespace SharpEngine.Rendering;

/// <summary>
/// Small software 3D rasterizer: perspective-correct colour interpolation, depth buffer,
/// no external dependencies. Renders into <see cref="Pixels"/> which a renderer then presents.
/// </summary>
public sealed class Rasterizer
{
    private const float NearW = 0.05f;

    public Rasterizer(int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        Width = width;
        Height = height;
        Pixels = new Color[width * height];
        Depth = new float[width * height];
    }

    public int Width { get; }
    public int Height { get; }
    public Color[] Pixels { get; }
    public float[] Depth { get; }

    public float Aspect => (float)Width / Height;

    public void Clear(Color color)
    {
        Array.Fill(Pixels, color);
        Array.Fill(Depth, float.PositiveInfinity);
    }

    /// <summary>
    /// Draws one triangle. <paramref name="mvp"/> uses System.Numerics conventions (row vectors:
    /// model * view * projection). Both faces are drawn. Triangles with a vertex behind the near
    /// plane are skipped (no clipping), which is fine for objects kept in front of the camera.
    /// </summary>
    public void DrawTriangle(
        Vector3 a, Vector3 b, Vector3 c,
        Color ca, Color cb, Color cc,
        in Matrix4x4 mvp)
    {
        var pa = Vector4.Transform(new Vector4(a, 1f), mvp);
        var pb = Vector4.Transform(new Vector4(b, 1f), mvp);
        var pc = Vector4.Transform(new Vector4(c, 1f), mvp);
        if (pa.W < NearW || pb.W < NearW || pc.W < NearW) return;

        var (ax, ay, az) = ToScreen(pa);
        var (bx, by, bz) = ToScreen(pb);
        var (cx, cy, cz) = ToScreen(pc);

        float area = Edge(ax, ay, bx, by, cx, cy);
        if (MathF.Abs(area) < 1e-6f) return;

        int minX = Math.Max(0, (int)MathF.Floor(MathF.Min(ax, MathF.Min(bx, cx))));
        int maxX = Math.Min(Width - 1, (int)MathF.Ceiling(MathF.Max(ax, MathF.Max(bx, cx))));
        int minY = Math.Max(0, (int)MathF.Floor(MathF.Min(ay, MathF.Min(by, cy))));
        int maxY = Math.Min(Height - 1, (int)MathF.Ceiling(MathF.Max(ay, MathF.Max(by, cy))));

        float invA = 1f / pa.W, invB = 1f / pb.W, invC = 1f / pc.W;

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                float w0 = Edge(bx, by, cx, cy, px, py) / area;
                float w1 = Edge(cx, cy, ax, ay, px, py) / area;
                float w2 = Edge(ax, ay, bx, by, px, py) / area;
                if (w0 < 0 || w1 < 0 || w2 < 0) continue;

                float z = w0 * az + w1 * bz + w2 * cz;
                int i = y * Width + x;
                if (z >= Depth[i]) continue;
                Depth[i] = z;

                // perspective-correct weights
                float p0 = w0 * invA, p1 = w1 * invB, p2 = w2 * invC;
                float norm = 1f / (p0 + p1 + p2);
                p0 *= norm; p1 *= norm; p2 *= norm;

                Pixels[i] = new Color(
                    Mix(ca.R, cb.R, cc.R, p0, p1, p2),
                    Mix(ca.G, cb.G, cc.G, p0, p1, p2),
                    Mix(ca.B, cb.B, cc.B, p0, p1, p2));
            }
        }
    }

    private (float x, float y, float z) ToScreen(Vector4 clip)
    {
        float invW = 1f / clip.W;
        float nx = clip.X * invW, ny = clip.Y * invW, nz = clip.Z * invW;
        return ((nx * 0.5f + 0.5f) * Width, (1f - (ny * 0.5f + 0.5f)) * Height, nz);
    }

    private static float Edge(float ax, float ay, float bx, float by, float px, float py) =>
        (px - ax) * (by - ay) - (py - ay) * (bx - ax);

    private static byte Mix(byte a, byte b, byte c, float wa, float wb, float wc) =>
        (byte)Math.Clamp(a * wa + b * wb + c * wc + 0.5f, 0f, 255f);
}
