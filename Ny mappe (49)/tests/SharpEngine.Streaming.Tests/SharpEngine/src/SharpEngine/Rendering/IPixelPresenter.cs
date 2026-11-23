namespace SharpEngine.Rendering;

/// <summary>
/// Optional renderer capability: show a full-screen pixel image (used by the 3D rasterizer).
/// Create the <see cref="Rasterizer"/> at <see cref="PixelWidth"/> x <see cref="PixelHeight"/>.
/// </summary>
public interface IPixelPresenter
{
    int PixelWidth { get; }
    int PixelHeight { get; }

    /// <summary>Draws a PixelWidth x PixelHeight image over the whole screen.</summary>
    void DrawPixels(Color[] pixels);
}
