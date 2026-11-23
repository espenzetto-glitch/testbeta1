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
        world.ShowChunkDebug = editor;   // the editor shows the chunk debugger by default, a player build only on F3

        ErrorLog.Install();
        int faults = 0;
        try
        {
            while (!Rl.WindowShouldClose())
            {
                if (Rl.IsKeyPressed(KeyboardKey.Escape) && !Editor.Gui.WantsKeyboard) break;
                if (Rl.IsKeyPressed(KeyboardKey.F3) && !Editor.Gui.WantsKeyboard) world.ShowChunkDebug = !world.ShowChunkDebug;

                // Every part of a frame is guarded: a bug in a script, a broken scene file or an editor problem is reported
                // (status bar + Logs/errors.log) and the program carries on with the next frame instead of closing.
                bool ok = true;
                float dt = Math.Min(Rl.GetFrameTime(), 0.1f);
                ok &= Guard("update", world, () =>
                {
                    KeyEvents.Poll();
                    if (editor) Editor.AdminEditor.Update(world);
                    SceneManager.Process(world);
                    if (world.PlayMode && !Editor.Gui.WantsKeyboard) SceneManager.HandleHotkeys();
                    world.Streamer.Enabled = SceneManager.Config.StreamWorld;
                    world.Streamer.Radius = SceneManager.Config.StreamRadius;
                    ScenePortal.ResetPrompt();
                    world.Update(dt);
                    SceneManager.Process(world);   // a scene change requested this frame shows up this frame
                });

                Rl.BeginDrawing();
                try
                {
                    Rl.ClearBackground(Color.Black);
                    ok &= Guard("drawing the world", world, () =>
                    {
                        world.Render();
                        if (world.ShowChunkDebug) world.Streamer.DrawDebugOverlay();
                    });
                    if (editor) ok &= Guard("editor", world, () => Editor.AdminEditor.DrawGui(world));
                    if (onGui is not null) ok &= Guard("onGui", world, () => onGui(world));
                    ok &= Guard("portal prompt", world, () => DrawPortalPrompt(world));
                }
                finally
                {
                    Rl.EndScissorMode();   // an error inside a scrolled panel must not leave the clip rectangle on
                    Rl.EndDrawing();
                }

                faults = ok ? 0 : faults + 1;
                // a playing game that keeps failing is stopped (the scene is restored) instead of failing 60 times a second
                if (faults >= 30 && editor && world.PlayMode) { Editor.AdminEditor.StopPlayAfterError(world); faults = 0; }
            }
        }
        finally
        {
            Rl.CloseWindow();
        }
    }

    private static bool Guard(string where, World world, Action action)
    {
        try { action(); return true; }
        catch (Exception ex) when (ErrorLog.IsRecoverable(ex))
        {
            ErrorLog.Report(where, ex);
            return false;
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
