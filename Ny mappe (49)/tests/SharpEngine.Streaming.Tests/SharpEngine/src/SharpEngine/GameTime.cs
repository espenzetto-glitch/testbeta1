namespace SharpEngine;

/// <summary>Timing information passed to every fixed update step.</summary>
/// <param name="Delta">Seconds per update step (constant).</param>
/// <param name="Total">Simulated seconds since the engine started.</param>
/// <param name="Frame">Rendered frame counter.</param>
public readonly record struct GameTime(float Delta, double Total, long Frame);
