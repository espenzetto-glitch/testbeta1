using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;

namespace SharpEngine.Web;

/// <summary>The WebGL side (wwwroot/sharp.js). The functions are looked up on globalThis.sharp.</summary>
internal static partial class Js
{
    [JSImport("globalThis.sharp.begin")] public static partial void Begin(int skyTop, int skyBottom, [JSMarshalAs<JSType.Array<JSType.Number>>] double[] camera);
    [JSImport("globalThis.sharp.triangles")] public static partial void Triangles(string texture, [JSMarshalAs<JSType.MemoryView>] Span<byte> vertices, int count);
    [JSImport("globalThis.sharp.lines")] public static partial void Lines([JSMarshalAs<JSType.MemoryView>] Span<byte> vertices, int count);
    [JSImport("globalThis.sharp.end")] public static partial void End();
    [JSImport("globalThis.sharp.hud")] public static partial void Hud(string text, string prompt);
    [JSImport("globalThis.sharp.ready")] public static partial void Ready();
    [JSImport("globalThis.sharp.capture")] public static partial void Capture(bool on);
}

internal sealed class JsCanvas : IWebCanvas
{
    public void Begin(int skyTop, int skyBottom, ReadOnlySpan<double> camera) => Js.Begin(skyTop, skyBottom, camera.ToArray());
    public void Triangles(string texture, ReadOnlySpan<byte> vertices, int vertexCount) => Js.Triangles(texture, MemoryMarshal.CreateSpan(ref MemoryMarshal.GetReference(vertices), vertices.Length), vertexCount);
    public void Lines(ReadOnlySpan<byte> vertices, int vertexCount) => Js.Lines(MemoryMarshal.CreateSpan(ref MemoryMarshal.GetReference(vertices), vertices.Length), vertexCount);
    public void End() => Js.End();
    public void Hud(string text, string prompt) => Js.Hud(text, prompt);
    public void Ready() => Js.Ready();
}
