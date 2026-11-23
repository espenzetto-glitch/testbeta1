
public class Step5Mesh : MeshGenerator
{
    // ==========================================
    // INSPECTOR SETTINGS
    // ==========================================

    public float Radius { get; set; } = 0.8f;

    public float WaveStrength { get; set; } = 0.24f;

    public float WaveSpeed { get; set; } = 2.8f;

    public float SpikeStrength { get; set; } = 0.28f;

    public float CoreGlow { get; set; } = 1.0f;

    public int Segments { get; set; } = 40;

    public int Rings { get; set; } = 28;

    // Internal animation clock.
    private float animationTime = 0f;

    // ==========================================
    // LIFECYCLE
    // ==========================================

    public override void Start()
    {
        Regenerate();

        // Make the fireball colours visible without
        // depending on scene lighting.
        MeshRenderer renderer =
            GameObject.GetComponent<MeshRenderer>();

        if (renderer != null)
        {
            renderer.Unlit = true;
            renderer.DoubleSided = true;
        }
    }

    public override void Update(float dt)
    {
        // Preserve the base mesh generator update.
        base.Update(dt);

        // Advance animation.
        animationTime += dt * WaveSpeed;

        // Rebuild the animated mesh.
        Regenerate();
    }

    // ==========================================
    // MESH GENERATION
    // ==========================================

    public override void Generate(MeshBuilder b)
    {
        int segments = Math.Max(12, Segments);
        int rings = Math.Max(8, Rings);

        float t = animationTime;

        // Generate spherical fire surface.
        for (int y = 0; y <= rings; y++)
        {
            float v = (float)y / rings;

            float latitude =
                (v - 0.5f) * MathF.PI;

            float sinLat = MathF.Sin(latitude);
            float cosLat = MathF.Cos(latitude);

            for (int x = 0; x <= segments; x++)
            {
                float u = (float)x / segments;

                float longitude =
                    u * MathF.PI * 2f;

                // Spherical direction.
                float dx =
                    cosLat * MathF.Cos(longitude);

                float dy = sinLat;

                float dz =
                    cosLat * MathF.Sin(longitude);

                // ==================================
                // FIRE WAVE PATTERNS
                // ==================================

                float wave1 = MathF.Sin(
                    longitude * 5f +
                    latitude * 8f +
                    t * 1.2f
                );

                float wave2 = MathF.Sin(
                    longitude * 9f -
                    latitude * 6f -
                    t * 1.8f
                );

                float wave3 = MathF.Cos(
                    longitude * 3f +
                    latitude * 12f +
                    t * 0.7f
                );

                // Combine waves into turbulence.
                float turbulence =
                    wave1 * 0.45f +
                    wave2 * 0.30f +
                    wave3 * 0.25f;

                // ==================================
                // FLAME SPIKES
                // ==================================

                float spikes = MathF.Pow(
                    MathF.Max(0f, wave1 * wave2),
                    2f
                );

                // Animate the sphere's radius.
                float radius = Radius *
                    (
                        1f +
                        turbulence * WaveStrength +
                        spikes * SpikeStrength
                    );

                // ==================================
                // FINAL VERTEX POSITION
                // ==================================

                Vector3 position = new Vector3(
                    dx * radius,
                    dy * radius,
                    dz * radius
                );

                Vector3 normal = new Vector3(
                    dx,
                    dy,
                    dz
                );

                // ==================================
                // ANIMATED FIRE COLOUR
                // ==================================

                float flicker = 0.5f + 0.5f *
                    MathF.Sin(
                        t * 2.5f +
                        longitude * 4f +
                        latitude * 3f
                    );

                float heat = Clamp01(
                    0.25f +
                    flicker * 0.44f +
                    spikes * 0.22f +
                    CoreGlow * 0.09f
                );

                // Hot regions approach white-yellow.
                float hot = Clamp01(
                    (heat - 0.76f) * 4f
                );

                float red =
                    120f + 135f * heat;

                float green =
                    12f + 225f * heat * heat;

                float blue =
                    3f + 230f * hot;

                // Explicit byte conversions.
                byte r = ToByte(red);
                byte g = ToByte(green);
                byte bl = ToByte(blue);

                Color fireColor = new Color(
                    r,
                    g,
                    bl,
                    (byte)255
                );

                // Add vertex.
                b.AddVertex(
                    position,
                    normal,
                    fireColor
                );
            }
        }

        // ==========================================
        // CONNECT THE TRIANGLES
        // ==========================================

        for (int y = 0; y < rings; y++)
        {
            for (int x = 0; x < segments; x++)
            {
                int a = y * (segments + 1) + x;

                int c = a + segments + 1;

                b.AddTriangle(a, c, a + 1);

                b.AddTriangle(a + 1, c, c + 1);
            }
        }
    }

    // ==========================================
    // HELPER FUNCTIONS
    // ==========================================

    private static float Clamp01(float value)
    {
        if (value < 0f) return 0f;
        if (value > 1f) return 1f;

        return value;
    }

    private static byte ToByte(float value)
    {
        if (value < 0f) value = 0f;
        if (value > 255f) value = 255f;

        return (byte)value;
    }
}

