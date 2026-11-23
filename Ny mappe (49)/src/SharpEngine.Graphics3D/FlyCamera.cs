using System.Numerics;
using Raylib_cs;

namespace SharpEngine.Graphics3D;

/// <summary>
/// Unity scene-view style camera: hold the RIGHT mouse button to look around, then
/// W/A/S/D move, Q/E down/up, Shift = faster.
/// </summary>
public sealed class FlyCamera : Component
{
    private bool _looking;
    private bool _skipDelta;

    // runs in play mode only: while editing, the editor's own view camera (World.EditorView) is what you fly

    public float Speed { get; set; } = 6f;
    public float FastMultiplier { get; set; } = 3f;

    /// <summary>Degrees per mouse pixel.</summary>
    public float Sensitivity { get; set; } = 0.12f;

    public override void Update(float dt)
    {
        bool rmb = Platform.Input.IsMouseDown(MouseButton.Right);
        if (rmb && !_looking)
        {
            Platform.Input.CaptureMouse(true);
            _looking = true;
            _skipDelta = true; // the first delta after capturing can be a large jump
        }
        else if (!rmb && _looking)
        {
            Platform.Input.CaptureMouse(false);
            _looking = false;
        }
        if (!_looking) return;

        var mouse = Platform.Input.MouseDelta;
        if (_skipDelta) { _skipDelta = false; mouse = Vector2.Zero; }

        var e = Transform.EulerAngles;
        e.Y -= mouse.X * Sensitivity;
        e.X = Math.Clamp(e.X - mouse.Y * Sensitivity, -89f, 89f);
        Transform.EulerAngles = e;

        var move = Vector3.Zero;
        if (Held(KeyboardKey.W)) move += Transform.Forward;
        if (Held(KeyboardKey.S)) move -= Transform.Forward;
        if (Held(KeyboardKey.D)) move += Transform.Right;
        if (Held(KeyboardKey.A)) move -= Transform.Right;
        if (Held(KeyboardKey.E)) move += Vector3.UnitY;
        if (Held(KeyboardKey.Q)) move -= Vector3.UnitY;

        if (move.LengthSquared() > 0f)
        {
            float speed = Speed * (Platform.Input.IsKeyDown(KeyboardKey.LeftShift) ? FastMultiplier : 1f);
            Transform.Position += Vector3.Normalize(move) * speed * dt;
        }
    }

    /// <summary>Key held and not reserved by a portal that is waiting for it (E enters portals, it must not also fly up).</summary>
    private static bool Held(KeyboardKey key) => Platform.Input.IsKeyDown(key) && ScenePortal.ReservedKey != key;

    public override void OnDestroy()
    {
        if (_looking) Platform.Input.CaptureMouse(false);
    }
}
