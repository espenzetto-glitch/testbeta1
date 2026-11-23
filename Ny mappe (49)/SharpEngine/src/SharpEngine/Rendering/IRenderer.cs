namespace SharpEngine.Rendering;

/// <summary>
/// Backend-agnostic drawing surface. Coordinates are in the renderer's own units
/// (character cells for the console backends). Implement this to plug in SDL, OpenGL, Vulkan, etc.
/// </summary>
public interface IRenderer : IDisposable
{
    int Width { get; }
    int Height { get; }

    void Initialize(string title);
    void BeginFrame(Color clear);
    void FillRect(float x, float y, float width, float height, Color color);
    void DrawText(float x, float y, string text, Color color);
    void EndFrame();
}
