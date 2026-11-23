using System.Numerics;
using System.Runtime.InteropServices;
using Raylib_cs;
using SharpEngine.Graphics3D;

namespace SharpEngine.Web;

/// <summary>What the page has to do. The real one calls into sharp.js (WebGL); tests use a fake.</summary>
public interface IWebCanvas
{
    /// <summary>Starts a frame. camera = position xyz, target xyz, up xyz, fov. Colours are 0xRRGGBB.</summary>
    void Begin(int skyTop, int skyBottom, ReadOnlySpan<double> camera);
    /// <summary>Triangles, 9 floats per vertex: x y z r g b a u v (colours 0..1). texture is "" for none.</summary>
    void Triangles(string texture, ReadOnlySpan<byte> vertices, int vertexCount);
    /// <summary>Lines, 7 floats per vertex: x y z r g b a.</summary>
    void Lines(ReadOnlySpan<byte> vertices, int vertexCount);
    void End();
    void Hud(string text, string prompt);
    void Ready();
}

/// <summary>Turns the world into vertex buffers for the canvas (grouped by texture so the page draws a few big batches).</summary>
public sealed class WebSink : IFrameSink
{
    private readonly IWebCanvas _canvas;
    private readonly Dictionary<string, List<float>> _batches = new();
    private readonly List<float> _lines = new();

    public int Triangles { get; private set; }

    public WebSink(IWebCanvas canvas) => _canvas = canvas;

    public void BeginFrame(Color skyTop, Color skyBottom, Vector3 cameraPosition, Vector3 cameraTarget, Vector3 cameraUp, float fieldOfView)
    {
        foreach (var b in _batches.Values) b.Clear();
        _lines.Clear();
        Triangles = 0;
        Span<double> cam = stackalloc double[10]
        {
            cameraPosition.X, cameraPosition.Y, cameraPosition.Z,
            cameraTarget.X, cameraTarget.Y, cameraTarget.Z,
            cameraUp.X, cameraUp.Y, cameraUp.Z, fieldOfView
        };
        _canvas.Begin(Rgb(skyTop), Rgb(skyBottom), cam);
    }

    public void Mesh(string texture, float tileU, float tileV, ReadOnlySpan<Vector3> positions, ReadOnlySpan<Color> colors, Vector2[]? uvs, int[] indices)
    {
        if (!_batches.TryGetValue(texture, out var list)) _batches[texture] = list = new List<float>(4096);
        foreach (int i in indices)
        {
            var p = positions[i];
            var c = colors[i];
            list.Add(p.X); list.Add(p.Y); list.Add(p.Z);
            list.Add(c.R / 255f); list.Add(c.G / 255f); list.Add(c.B / 255f); list.Add(c.A / 255f);
            if (uvs is not null) { list.Add(uvs[i].X * tileU); list.Add(uvs[i].Y * tileV); }
            else { list.Add(0f); list.Add(0f); }
        }
        Triangles += indices.Length / 3;
    }

    public void Line(Vector3 from, Vector3 to, Color color)
    {
        foreach (var p in new[] { from, to })
        {
            _lines.Add(p.X); _lines.Add(p.Y); _lines.Add(p.Z);
            _lines.Add(color.R / 255f); _lines.Add(color.G / 255f); _lines.Add(color.B / 255f); _lines.Add(color.A / 255f);
        }
    }

    public void EndFrame()
    {
        if (_lines.Count > 0) _canvas.Lines(MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(_lines)), _lines.Count / 7);
        foreach (var (texture, list) in _batches)
            if (list.Count > 0) _canvas.Triangles(texture, MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(list)), list.Count / 9);
        _canvas.End();
    }

    private static int Rgb(Color c) => (c.R << 16) | (c.G << 8) | c.B;
}
