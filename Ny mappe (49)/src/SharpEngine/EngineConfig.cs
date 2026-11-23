using SharpEngine.Rendering;

namespace SharpEngine;

public sealed class EngineConfig
{
    public string Title { get; init; } = "SharpEngine";

    /// <summary>Fixed simulation steps per second.</summary>
    public int UpdateRate { get; init; } = 60;

    /// <summary>Frame limiter. 0 = unlimited.</summary>
    public int TargetFps { get; init; } = 60;

    /// <summary>Stop automatically after this many frames. 0 = run until Quit().</summary>
    public long MaxFrames { get; init; } = 0;

    /// <summary>
    /// When true, every rendered frame advances the simulation by exactly one fixed step
    /// regardless of wall-clock time. Useful for tests, replays and headless runs.
    /// </summary>
    public bool Deterministic { get; init; } = false;

    public Color ClearColor { get; init; } = Color.Black;
}
