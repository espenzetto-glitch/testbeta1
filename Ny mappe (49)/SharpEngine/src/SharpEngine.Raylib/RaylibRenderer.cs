using Rl = Raylib_cs.Raylib;
using Raylib_cs;
using SharpEngine.Rendering;
using EngineColor = SharpEngine.Rendering.Color;
using RlColor = Raylib_cs.Color;

namespace SharpEngine.Backends.RaylibBackend;

/// <summary>
/// Real window renderer. Game coordinates stay in "cells" (like the console backend) and are
/// scaled to pixels, so the same game code runs in the terminal and in a window.
/// </summary>
public sealed class RaylibRenderer : IRenderer, IPixelPresenter
{
    private readonly int _cellW, _cellH, _fontSize;
    private bool _open;

    public RaylibRenderer(int cellsX = 80, int cellsY = 24, int cellWidth = 12, int cellHeight = 24)
    {
        Width = cellsX;
        Height = cellsY;
        _cellW = cellWidth;
        _cellH = cellHeight;
        _fontSize = cellHeight - 4;
    }

    public int Width { get; }
    public int Height { get; }

    // 3D view: square pixels, 12x12 screen pixels each with the default cell size (80x48 image).
    public int PixelWidth => Width;
    public int PixelHeight => Height * 2;

    public void DrawPixels(EngineColor[] pixels)
    {
        if (pixels.Length != PixelWidth * PixelHeight)
            throw new ArgumentException($"Expected {PixelWidth}x{PixelHeight} pixels.", nameof(pixels));

        int sx = _cellW, sy = _cellH / 2;
        for (int y = 0; y < PixelHeight; y++)
            for (int x = 0; x < PixelWidth; x++)
                Rl.DrawRectangle(x * sx, y * sy, sx, sy, Convert(pixels[y * PixelWidth + x]));
    }

    /// <summary>Raised (on the game thread) when the user closes the window.</summary>
    public event Action? CloseRequested;

    public void Initialize(string title)
    {
        Rl.SetConfigFlags(ConfigFlags.VSyncHint);
        Rl.SetTraceLogLevel(TraceLogLevel.Warning);
        Rl.InitWindow(Width * _cellW, Height * _cellH, title);
        Rl.SetExitKey(KeyboardKey.Null); // Esc is handled by the game, not by Raylib
        _open = true;
    }

    public void BeginFrame(EngineColor clear)
    {
        Rl.BeginDrawing();
        Rl.ClearBackground(Convert(clear));
    }

    public void FillRect(float x, float y, float width, float height, EngineColor color)
    {
        if (color.A == 0) return;
        Rl.DrawRectangle(
            (int)MathF.Round(x * _cellW), (int)MathF.Round(y * _cellH),
            Math.Max(1, (int)MathF.Round(width * _cellW)), Math.Max(1, (int)MathF.Round(height * _cellH)),
            Convert(color));
    }

    public void DrawText(float x, float y, string text, EngineColor color)
    {
        Rl.DrawText(text, (int)MathF.Round(x * _cellW), (int)MathF.Round(y * _cellH) + 2, _fontSize, Convert(color));
    }

    public void EndFrame()
    {
        Rl.EndDrawing();
        if (Rl.WindowShouldClose()) CloseRequested?.Invoke();
    }

    public void Dispose()
    {
        if (!_open) return;
        _open = false;
        Rl.CloseWindow();
    }

    private static RlColor Convert(EngineColor c) => new(c.R, c.G, c.B, c.A);
}
