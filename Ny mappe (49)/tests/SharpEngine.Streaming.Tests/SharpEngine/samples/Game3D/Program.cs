// Standalone 3D client (no server needed).
//   dotnet run --project samples\Game3D
// Run from the editor: Build Settings... > Build And Run produces a stand-alone player of this game.
using Game3D;
using Raylib_cs;
using SharpEngine.Graphics3D;
using Rl = Raylib_cs.Raylib;

var world = new World();
bool player = GameProject.IsPlayer;

if (player)
{
    // a built game: start at build index 0 and play, no editor
    if (!SceneManager.LoadFirst(world))
    {
        Console.Error.WriteLine("This build contains no scenes.");
        return 1;
    }
}
else
{
    SampleScenes.EnsureExist();
    var first = SceneManager.AvailableScenes().Contains("MainScene") ? "MainScene" : SceneManager.AvailableScenes().FirstOrDefault();
    if (first is null) SceneManager.CreateEmpty(world, "NewScene");
    else SceneManager.Open(world, first);
}

var cfg = SceneManager.Config;
string title = player ? cfg.ProductName : "SharpEngine 3D (OpenGL) - editor";
int width = player ? cfg.WindowWidth : 1280;
int height = player ? cfg.WindowHeight : 720;

App3D.Run(title, width, height, world, DrawHud, editor: !player);
return 0;

static void DrawHud(World w)
{
    if (SharpEngine.Graphics3D.Editor.AdminEditor.Visible) return; // the editor has its own panels

    var scenes = SceneManager.BuildScenes;
    int index = SceneManager.CurrentBuildIndex;
    string where = index >= 0 ? $"{SceneManager.CurrentName} ({index + 1}/{scenes.Count})" : SceneManager.CurrentName;

    Rl.DrawFPS(10, 10);
    Rl.DrawText($"Scene: {where}", 10, 34, 20, Color.White);
    Rl.DrawText($"objects: {w.Objects.Count}   triangles: {w.TrianglesDrawn}", 10, 58, 20, Color.White);
    Rl.DrawText("Hold RIGHT MOUSE + WASD to fly | Q/E down/up | Shift = fast | fly into a portal, or Tab / Shift+Tab / 1-9 = switch scene | Esc = quit",
        10, Rl.GetScreenHeight() - 30, 18, Color.White);
}
