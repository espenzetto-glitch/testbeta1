using System.Numerics;

namespace SharpEngine.Streaming;

/// <summary>Sky / light values a chunk contributes. Everything is a plain float so any renderer can consume it.</summary>
public readonly record struct EnvironmentSettings(
    Vector3 SkyTop,
    Vector3 SkyHorizon,
    Vector3 SunColor,
    float SunIntensity,
    float Ambient,
    float FogDensity)
{
    public static EnvironmentSettings Lerp(EnvironmentSettings a, EnvironmentSettings b, float t) => new(
        Vector3.Lerp(a.SkyTop, b.SkyTop, t),
        Vector3.Lerp(a.SkyHorizon, b.SkyHorizon, t),
        Vector3.Lerp(a.SunColor, b.SunColor, t),
        a.SunIntensity + (b.SunIntensity - a.SunIntensity) * t,
        a.Ambient + (b.Ambient - a.Ambient) * t,
        a.FogDensity + (b.FogDensity - a.FogDensity) * t);
}

/// <summary>
/// Blends lighting and sky between chunks by distance, so crossing a border is continuous.
/// Each chunk gets weight 1 at/inside its border and fades to 0 at <c>blendDistance</c> away from it;
/// at the border itself a chunk and its neighbour contribute equally.
/// </summary>
public static class EnvironmentBlender
{
    public static EnvironmentSettings Evaluate(
        Vector3 player,
        IReadOnlyList<(Rect2 Rect, EnvironmentSettings Env)> chunks,
        float blendDistance)
    {
        if (chunks.Count == 0) throw new ArgumentException("No chunks to blend.", nameof(chunks));
        if (blendDistance <= 0f) return Nearest(player, chunks);

        float total = 0f;
        EnvironmentSettings acc = default;
        bool first = true;
        foreach (var (rect, env) in chunks)
        {
            float d = rect.DistanceTo(player.X, player.Z);
            float w = Smooth(1f - d / blendDistance);
            if (w <= 0f) continue;
            if (first) { acc = env; total = w; first = false; continue; }
            total += w;
            acc = EnvironmentSettings.Lerp(acc, env, w / total); // running weighted average
        }
        return first ? Nearest(player, chunks) : acc;
    }

    private static EnvironmentSettings Nearest(Vector3 p, IReadOnlyList<(Rect2 Rect, EnvironmentSettings Env)> chunks)
    {
        var best = chunks[0];
        float bd = best.Rect.DistanceTo(p.X, p.Z);
        for (int i = 1; i < chunks.Count; i++)
        {
            float d = chunks[i].Rect.DistanceTo(p.X, p.Z);
            if (d < bd) { bd = d; best = chunks[i]; }
        }
        return best.Env;
    }

    private static float Smooth(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
