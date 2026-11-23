using System.Numerics;
using Raylib_cs;
using SharpEngine.Graphics3D;
using SharpEngine.Graphics3D.Editor;

namespace Game3D;

/// <summary>
/// Creates three demo scenes the first time the game runs (Scenes/MainScene.json, Level2.json, Level3.json),
/// linked by portals. After that the editor owns them: change them, add more, and list them in Build Settings.
/// </summary>
public static class SampleScenes
{
    public static readonly string[] Names = { "MainScene", "Level2", "Level3" };

    /// <summary>Two small textures to try out: drop them on an object or pick them in Texture Path.</summary>
    private static void EnsureTextures()
    {
        string dir = Path.Combine(Assets.Folder, "Textures");
        string checker = Path.Combine(dir, "checker.png"), bricks = Path.Combine(dir, "bricks.png");
        if (!File.Exists(checker))
            PngWriter.Write(checker, 128, 128, (x, y) => ((x / 16 + y / 16) % 2 == 0) ? ((byte)240, (byte)240, (byte)240) : ((byte)50, (byte)90, (byte)200));
        if (!File.Exists(bricks))
            PngWriter.Write(bricks, 128, 128, (x, y) =>
            {
                int row = y / 16, off = (row % 2) * 16;
                bool mortar = y % 16 < 2 || (x + off) % 32 < 2;
                return mortar ? ((byte)200, (byte)196, (byte)186) : ((byte)(150 + (x * 7 + y * 3) % 30), (byte)60, (byte)45);
            });
    }

    public static void EnsureExist()
    {
        EnsureTextures();
        bool any = SceneManager.AvailableScenes().Count > 0;
        if (!any)
        {
            Write("MainScene", BuildMain);
            Write("Level2", BuildLevel2);
            Write("Level3", BuildLevel3);
        }

        if (SceneManager.Config.Scenes.Count == 0 && !any)
        {
            foreach (var n in Names)
                SceneManager.Config.Scenes.Add(new BuildScene { Path = SceneManager.BuildPathFor(n), Enabled = true });
            SceneManager.Config.ProductName = "SharpEngine Demo";
            SceneManager.SaveConfig();
        }
    }

    private static void Write(string name, Action<World> build)
    {
        var w = new World();
        build(w);
        w.Flush();
        SceneManager.Save(w, name);
    }

    private static GameObject Camera(World w, Vector3 pos, Vector3 lookAt)
    {
        var cam = w.Instantiate("Main Camera", pos);
        cam.Transform.LookAt(lookAt);
        cam.AddComponent<CameraComponent>().FieldOfView = 60f;
        cam.AddComponent<FlyCamera>();
        return cam;
    }

    private static void Portal(World w, string name, Vector3 pos, string target, Color color)
    {
        var pad = w.CreatePrimitive("Cylinder", color, new Vector3(pos.X, 0.08f, pos.Z));
        pad.Name = name + " Pad";
        pad.Transform.Scale = new Vector3(2.6f, 0.15f, 2.6f);
        pad.GetComponent<MeshRenderer>()!.Specular = 0.6f;

        var marker = w.CreatePrimitive("Pyramid", color, pos);
        marker.Name = name;
        marker.Transform.Scale = new Vector3(1.2f, 1.8f, 1.2f);
        marker.AddComponent(new Spinner { DegreesPerSecond = new Vector3(0, 120, 0) });
        marker.AddComponent(new Bobber { Amplitude = 0.25f, Speed = 3f });
        marker.AddComponent(new ScenePortal { TargetScene = target, Radius = 2.6f });
    }

    private static void BuildMain(World w)
    {
        Camera(w, new Vector3(6f, 4f, 9f), new Vector3(0f, 1f, 0f));

        var ground = w.CreateMesh("Ground", Primitives.Ground(), Color.White);
        ground.Transform.Scale = new Vector3(24f, 1f, 24f);
        ground.GetComponent<MeshRenderer>()!.Specular = 0f;

        var tri = w.CreatePrimitive("Triangle", Color.White, new Vector3(0f, 1.8f, 0f));
        tri.Transform.Scale = new Vector3(1.6f);
        tri.GetComponent<MeshRenderer>()!.Specular = 0f;
        tri.AddComponent(new Spinner { DegreesPerSecond = new Vector3(0, 70, 0) });

        var cube = w.CreatePrimitive("Cube", new Color(230, 80, 70, 255), new Vector3(-3.5f, 0.5f, 0f));
        cube.AddComponent(new Spinner { DegreesPerSecond = new Vector3(20, 35, 0) });

        var sphere = w.CreatePrimitive("Sphere", new Color(80, 160, 240, 255), new Vector3(3.5f, 1f, 0f));
        sphere.Transform.Scale = new Vector3(1.6f);
        sphere.GetComponent<MeshRenderer>()!.Specular = 0.8f;
        sphere.AddComponent(new Bobber());

        var pyramid = w.CreatePrimitive("Pyramid", new Color(240, 200, 60, 255), new Vector3(0f, 0.75f, -3.5f));
        pyramid.Transform.Scale = new Vector3(1.5f);
        pyramid.AddComponent(new Spinner { DegreesPerSecond = new Vector3(0, -40, 0) });

        var cylinder = w.CreatePrimitive("Cylinder", new Color(110, 210, 120, 255), new Vector3(0f, 0.75f, 3.5f));
        cylinder.Transform.Scale = new Vector3(1.2f, 1.5f, 1.2f);

        w.Instantiate("Sun").AddComponent<SunOrbit>();
        Portal(w, "Portal to Level2", new Vector3(-9f, 1.2f, -9f), "Level2", new Color(60, 200, 255, 255));
    }

    private static void BuildLevel2(World w)
    {
        Camera(w, new Vector3(0f, 4f, 12f), new Vector3(0f, 2f, 0f));
        w.SkyTop = new Color(120, 60, 150, 255);
        w.SkyBottom = new Color(250, 180, 120, 255);
        w.Sun.Direction = Vector3.Normalize(new Vector3(-0.8f, -0.45f, -0.3f));
        w.Sun.Color = new Color(255, 190, 130, 255);

        var ground = w.CreateMesh("Ground", Primitives.Ground(), new Color(255, 225, 200, 255));
        ground.Transform.Scale = new Vector3(30f, 1f, 30f);
        ground.GetComponent<MeshRenderer>()!.Specular = 0f;

        for (int i = 0; i < 6; i++) // staircase of cubes
        {
            var step = w.CreatePrimitive("Cube", new Color(120 + i * 22, 90 + i * 8, 200, 255), new Vector3(-6f + i * 1.6f, 0.5f + i * 0.5f, -2f));
            step.Name = "Step " + (i + 1);
            step.Transform.Scale = new Vector3(1.5f, 1f + i, 1.5f);
        }

        var tower = w.CreatePrimitive("Sphere", new Color(250, 120, 140, 255), new Vector3(5f, 2.5f, -3f));
        tower.Transform.Scale = new Vector3(3f);
        tower.GetComponent<MeshRenderer>()!.Specular = 0.9f;
        tower.AddComponent(new Bobber { Amplitude = 0.6f, Speed = 1.2f });

        var spinner = w.CreatePrimitive("Cylinder", new Color(255, 230, 120, 255), new Vector3(0f, 1.2f, 3f));
        spinner.Transform.Scale = new Vector3(0.6f, 2.4f, 0.6f);
        spinner.AddComponent(new Spinner { DegreesPerSecond = new Vector3(0, 90, 0) });

        Portal(w, "Portal to Level3", new Vector3(9f, 1.2f, 8f), "Level3", new Color(255, 170, 60, 255));
        Portal(w, "Portal to MainScene", new Vector3(-9f, 1.2f, 8f), "MainScene", new Color(60, 200, 255, 255));
    }

    private static void BuildLevel3(World w)
    {
        Camera(w, new Vector3(0f, 5f, 13f), new Vector3(0f, 1f, 0f));
        w.SkyTop = new Color(8, 10, 30, 255);
        w.SkyBottom = new Color(40, 50, 90, 255);
        w.Sun.Intensity = 0.55f;
        w.Sun.Ambient = 0.18f;
        w.Sun.Color = new Color(170, 190, 255, 255);

        var ground = w.CreateMesh("Ground", Primitives.Ground(), new Color(110, 160, 130, 255));
        ground.Transform.Scale = new Vector3(30f, 1f, 30f);
        ground.GetComponent<MeshRenderer>()!.Specular = 0f;

        for (int i = 0; i < 12; i++) // ring of pyramids
        {
            float a = i / 12f * MathF.Tau;
            var p = w.CreatePrimitive("Pyramid", new Color(120 + i * 10, 200, 255 - i * 12, 255),
                new Vector3(MathF.Cos(a) * 6f, 1f, MathF.Sin(a) * 6f));
            p.Name = "Pyramid " + (i + 1);
            p.Transform.Scale = new Vector3(1.2f, 2f, 1.2f);
            p.AddComponent(new Spinner { DegreesPerSecond = new Vector3(0, 40 + i * 6, 0) });
        }

        var core = w.CreatePrimitive("Sphere", new Color(255, 255, 255, 255), new Vector3(0f, 1.5f, 0f));
        core.Transform.Scale = new Vector3(2f);
        core.GetComponent<MeshRenderer>()!.Specular = 1f;
        core.AddComponent(new Bobber { Amplitude = 0.5f });

        Portal(w, "Portal to MainScene", new Vector3(9f, 1.2f, -9f), "MainScene", new Color(60, 200, 255, 255));
    }
}
