using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace SharpEngine.Graphics3D;

/// <summary>Opens an OpenGL window and runs the game loop for a <see cref="World"/>.</summary>
public static class App3D
{
    /// <param name="onGui">Optional 2D overlay (HUD), called after the 3D scene and the editor each frame.</param>
    /// <param name="editor">Show the admin editor (F1 hides it, F5 plays/stops). Starts in edit mode.</param>
    public static void Run(string title, int width, int height, World world, Action<World>? onGui = null, bool editor = true)
    {
        Rl.SetConfigFlags(ConfigFlags.Msaa4xHint | ConfigFlags.VSyncHint | ConfigFlags.ResizableWindow);
        Rl.SetTraceLogLevel(TraceLogLevel.Warning);
        Rl.InitWindow(width, height, title);
        Rl.SetExitKey(KeyboardKey.Null); // Esc cancels text editing; it quits only when nothing is being edited
        if (!editor && GameProject.IsPlayer && SceneManager.Config.StartFullscreen) Rl.ToggleFullscreen();
        if (editor) Editor.AdminEditor.Attach(world);

        try
        {
            while (!Rl.WindowShouldClose())
            {
                if (Rl.IsKeyPressed(KeyboardKey.Escape) && !Editor.Gui.WantsKeyboard) break;

                float dt = Math.Min(Rl.GetFrameTime(), 0.1f);
                KeyEvents.Poll();
                if (editor) Editor.AdminEditor.Update(world);
                SceneManager.Process(world);
                if (world.PlayMode && !Editor.Gui.WantsKeyboard) SceneManager.HandleHotkeys();
                ScenePortal.ResetPrompt();
                world.Update(dt);
                SceneManager.Process(world);   // a scene change requested this frame shows up this frame

                Rl.BeginDrawing();
                Rl.ClearBackground(Color.Black);
                world.Render();
                if (editor) Editor.AdminEditor.DrawGui(world);
                onGui?.Invoke(world);
                DrawPortalPrompt(world);
                Rl.EndDrawing();
            }
        }
        finally
        {
            Rl.CloseWindow();
        }
    }

    private static void DrawPortalPrompt(World world)
    {
        if (!world.PlayMode || ScenePortal.ActivePrompt is not { } text) return;
        int size = 24, w = Rl.MeasureText(text, size);
        int x = (Rl.GetScreenWidth() - w) / 2, y = Rl.GetScreenHeight() - 120;
        Rl.DrawRectangle(x - 16, y - 8, w + 32, size + 16, new Color(0, 0, 0, 170));
        Rl.DrawRectangleLines(x - 16, y - 8, w + 32, size + 16, new Color(255, 220, 110, 255));
        Rl.DrawText(text, x, y, size, Color.White);
    }
}
