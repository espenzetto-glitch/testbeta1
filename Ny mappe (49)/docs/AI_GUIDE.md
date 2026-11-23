# SharpEngine - guide for AI assistants

Read this first. It is a compact, rule-style summary so you can write correct code for this engine without guessing.
The full member list is in `ENGINE_REFERENCE.md` (same folder); this file says what to write and what to avoid.

## 1. What this is
- C# 3D engine on Raylib-cs 7.0.1, Unity-style: `World` > `GameObject` > `Component`. In-game editor (F1 toggles), play with F5, code editor F4.
- Projects: `src/SharpEngine` (2D core), `src/SharpEngine.Graphics3D` (3D + editor), `src/SharpEngine.Web` (engine as WebAssembly), `samples/Game3D`.
- Runs on .NET 11 with C# `preview` (fallback: `-p:SharpTfm=net8.0`). User shell is **Windows PowerShell**, give PowerShell commands, never cmd or bash.
- You write **scripts** (`Scripts/*.cs`). They are compiled by the engine with Roslyn when the user presses **Ctrl+S in the code editor**, not by `dotnet build`.

## 2. Rules for generated scripts (follow all of them)
1. A script file is plain C# with **no namespace, no `using` needed**. Already imported: `System`, `System.Collections.Generic`, `System.Numerics`, `System.Linq`, `SharpEngine.Graphics3D`, `Color`, `KeyboardKey`, `Rl` (Raylib).
2. Put one public class per behaviour. Component scripts derive from `Component`, whole-game scripts from `WorldScript`, procedural meshes from `MeshGenerator`.
3. Inspector/saved values are **public properties** (`{ get; set; }`) of type `float`, `int`, `bool`, `string`, `Vector3`, `Color`. Fields, other types and unions are not saved.
4. Use `Update(float dt)` and multiply movement by `dt`. Never block, never `Thread.Sleep`, never loop forever (it freezes the program).
5. Angles are **degrees** (`Transform.EulerAngles`: X pitch, Y yaw, Z roll). Forward is -Z. Y is up.
6. Do not edit engine source to add game logic. Scripts are enough. Do not add `Scripts/**` to `<Compile>` items.
7. An exception in a script disables that script and logs it. Use `Debug.Log / LogWarning / LogError(x)` for output; it appears in the code editor's Log panel.
8. Do not use `Raylib_cs.Raylib.BeginDrawing` or anything that draws directly. The engine draws; scripts change objects.

## 3. Templates

### Component (`Scripts/Spin.cs`)
```csharp
public class Spin : Component
{
    public float Speed { get; set; } = 1f;                       // shows in Inspector, saved with the scene
    public override void Start() { }                             // once when play starts
    public override void Update(float dt)                        // every frame while playing
    {
        Transform.Rotate(new Vector3(0f, 45f * Speed * dt, 0f));
        if (Rl.IsKeyPressed(KeyboardKey.Space)) Transform.Translate(new Vector3(0, 1, 0));
    }
    public override void OnDestroy() { }
}
```
Available inside: `Transform` (Position, EulerAngles, Scale, Forward, Right, Up, Translate, Rotate, LookAt), `GameObject` (Name, Active, Folder, GetComponent<T>(), AddComponent, Destroy), `World`.
`public override bool RunInEditMode => true;` makes it update in the editor too.

### World script (`Scripts/World/WorldCode.cs`): code for the whole game
One instance is created when play starts and lives until play stops, across scene changes, merges and streaming.
```csharp
public class MyWorld : WorldScript
{
    public override void Start() { }
    public override void OnSceneLoaded(string scene) { }
    public override void OnMergeChanged(IReadOnlyCollection<string> merged) { }
    public override void Update(float dt) { }
    public override void OnStop() { }
}
```
Helpers: `World`, `Find("name")` (GameObject?), `LoadScene("Scene")`, `Merge("A","B")`, `Unmerge()`, `CurrentScene`, `MergedScenes`, `Enabled`, and `Scenes` / `WorldIndex` (every scene, object and component of the project, also unloaded scenes).

### Mesh script: code that makes triangles (GPU renderer, millions are fine)
```csharp
public class WaveMesh : MeshGenerator
{
    public float Size { get; set; } = 20f;
    public int Cells { get; set; } = 40;
    public override void Generate(MeshBuilder b)     // rebuilt when a public property changes, also in edit mode
    {
        b.AddGrid(Size, Size, Cells, Cells, (x, z) => MathF.Sin(x * 0.5f) * MathF.Cos(z * 0.5f),
                  h => MeshBuilder.Lerp(new Color(40,120,60,255), new Color(230,230,200,255), Math.Clamp(h * 0.5f + 0.5f, 0f, 1f)));
    }
}
```
MeshBuilder: `AddVertex(pos, normal, color?, uv?)` returns the index, `AddTriangle(a,b,c)`, `AddFlatTriangle`, `AddFlatQuad`, `AddBox`, `AddSphere`, `AddGrid`, `VertexCount`, `TriangleCount`.

## 4. Union types (C# 15) with a world script
`union` is the C# 15 closed-choice type: a value is exactly one of several case types, and a `switch` over it must cover every case.
It needs a Roslyn compiler that knows C# 15 (the .NET 11 SDK, which this project uses). The two runtime types it needs (`UnionAttribute`, `IUnion`) are added to every script build automatically on runtimes that lack them.
**All scripts compile into one assembly with no namespace**, so a union declared in one file can be used by every component and world script.

### Pattern: typed game events handled by a world script
`Scripts/World/GameEvents.cs`
```csharp
public record class ScoreChanged(int Points);
public record class DoorOpened(string Door);
public record class SceneCleared(string Scene);

public union GameEvent(ScoreChanged, DoorOpened, SceneCleared);

public static class Events
{
    public static readonly Queue<GameEvent> Pending = new();
    public static void Raise(GameEvent e) => Pending.Enqueue(e);   // ScoreChanged converts to GameEvent implicitly
}
```
`Scripts/World/WorldCode.cs`
```csharp
public class WorldCode : WorldScript
{
    private int _score;

    public override void Update(float dt)
    {
        while (Events.Pending.Count > 0)
        {
            GameEvent e = Events.Pending.Dequeue();
            switch (e)                                    // exhaustive: no default needed, a missing case is a compiler warning
            {
                case ScoreChanged s:  _score += s.Points; Debug.Log($"score {_score}"); break;
                case DoorOpened d:    Find(d.Door)?.Transform.Translate(new Vector3(0, 3, 0)); break;
                case SceneCleared c:  LoadScene(c.Scene); break;
            }
        }
    }
}
```
`Scripts/Coin.cs` (a component raising an event)
```csharp
public class Coin : Component
{
    public int Value { get; set; } = 10;
    public override void Update(float dt)
    {
        if (Rl.IsKeyPressed(KeyboardKey.E)) { Events.Raise(new ScoreChanged(Value)); GameObject.Destroy(); }
    }
}
```
Rules for unions: declare them as top-level types in a script; create values by assigning a case (`GameEvent e = new DoorOpened("Gate");`); consume them with `switch`/`switch` expressions and type patterns; never put a union in a public property you want in the Inspector or saved in a scene (only float/int/bool/string/Vector3/Color are saved; keep the union in a field or static).

### If the compiler rejects `union` (no C# 15 compiler)
Use the same shape with a closed record hierarchy; the code that uses it does not change except for the declaration:
```csharp
public abstract record GameEvent { private protected GameEvent() { } }
public sealed record ScoreChanged(int Points) : GameEvent;
public sealed record DoorOpened(string Door) : GameEvent;
public sealed record SceneCleared(string Scene) : GameEvent;
```
The engine's own code uses this form (`HierarchyItem` = `ObjectItem` | `FolderItem`) so it builds on every SDK. **Note:** the `union` keyword itself could not be compiled where this guide was written (only an older SDK was available), so test it once on the .NET 11 SDK and fall back to the record form if it errors.

## 4b. Editor tools (the dynamic admin editor)
The editor can be extended while it runs. Scripts in `Scripts/Editor/` derive from `EditorScript` and register things in `OnRegister(EditorApi editor)`:
`AddCommand(name, world => ...)` (Tools menu), `AddToolbarButton`, `AddPanel(title, ui => ...)`, `AddObjectInspector<T>(title, (ui, go) => ...)`, `AddShortcut(key, run, ctrl:, shift:)`; `editor.Selected`, `Select(go)`, `FocusSelection()`, `Say(text)`, `World`.
`ui` (PanelBuilder): `Heading, Label, Dim, Separator, Space, Button -> bool, Buttons(...) -> index, Toggle, Slider, Text, Dropdown` (value controls take `ref`). Panels redraw every frame, so show live values. All registered items disappear and are registered again on every recompile, so keep state in fields of the tool class.
The item kinds form a closed set, `EditorItem = ToolbarButton | MenuCommand | PanelItem | ObjectInspector | ShortcutItem` (a `union` in C# 15, a closed record hierarchy in the engine). Example of a script using a real union to describe its own tool actions:
```csharp
public record class Spawn(string Primitive, int Count);
public record class Clear(string Folder);
public union ToolAction(Spawn, Clear);

public class Builder : EditorScript
{
    private void Do(ToolAction a)
    {
        switch (a)
        {
            case Spawn s: for (int i = 0; i < s.Count; i++) World.CreatePrimitive(s.Primitive, Color.White, new Vector3(i * 2f, 1, 0)); break;
            case Clear c: World.Folders.Delete(c.Folder, destroyObjects: true); break;
        }
    }
    public override void OnRegister(EditorApi editor)
    {
        editor.AddCommand("Spawn 10 cubes", w => Do(new Spawn("Cube", 10)));
        editor.AddCommand("Clear folder Test", w => Do(new Clear("Test")));
    }
}
```
(`union` was not compiled where this guide was written; if the compiler rejects it use `public abstract record ToolAction; public sealed record Spawn(...) : ToolAction;` ...)

## 5. Everyday API (World, GameObject)
- Create: `World.Instantiate("Name", position)`, `World.CreatePrimitive("Cube", Color, position)`, `World.CreateMesh(name, MeshData, Color, position)`.
- Find/destroy: `World.Find("Name")`, `go.Destroy()`, `World.Raycast(ray, out dist)`, `World.WorldBounds(go)`.
- Components: `go.AddComponent(new MeshRenderer { PrimitiveName = "Cube", Tint = new Color(255,0,0,255) })`, `go.GetComponent<T>()`.
- `MeshRenderer` properties: `PrimitiveName` (Cube, Sphere, Cylinder, Pyramid, Plane, Triangle, Ground), `ModelPath`, `ModelPart`, `TexturePath`, `TileU`, `TileV`, `Tint`, `Unlit`, `DoubleSided`, `Specular`, `Shininess`.
- Scenes: `SceneManager.LoadScene("Name")`, `SceneManager.Merge(...)`, `SceneManager.CurrentName`.
- Folders (organising objects in the hierarchy; no transform): `go.Folder = "Town/Houses"`, `World.Folders.Create / Put / Move / Rename / Delete / Objects(path) / TryGetBounds / SetActive`.
- Input: `Rl.IsKeyDown(KeyboardKey.W)`, `Rl.IsKeyPressed(...)`, `Rl.GetMouseDelta()`.

## 6. Files and formats
| Path | Meaning |
| --- | --- |
| `Scripts/*.cs`, `Scripts/World/*.cs` | your scripts (compiled by the editor, Ctrl+S) |
| `Scenes/<Name>.json` | scene: `{version, settings{sunDirection,sunColor,...}, objects[], folders[]?}` |
| object | `{name, active, position[3], rotation[3], scale[3], folder?, components[{type, properties{...}}]}` |
| `Assets/Models`, `Assets/Textures` | imported models (.fbx binary, .obj) and textures (.png .jpg .bmp .tga) |
| `BuildSettings.json`, `WorldMap.json` | scenes in the build, world layout and merge groups |
| `Logs/errors.log`, `Logs/latest.txt` | caught errors; exported readable log |
When writing scene JSON by hand, `rotation` is Euler degrees `[pitch, yaw, roll]`, colours are `[r,g,b,a]` 0-255, and a component `type` is the class name.

## 7. Editor quick facts (to tell the user)
- F1 hide editor, **F5** play/stop, **F4** code editor, **Ctrl+S** save scene / compile scripts, **Ctrl+I** Import browser, Ctrl+M World Map, Ctrl+Shift+B Build Settings, Ctrl+Shift+M Manage Scenes (multi-delete), **Ctrl+Shift+L Export Log**.
- Scene View: hold right mouse + WASD/Q/E to fly, **F** flies to the selected object or folder, G drops it to the ground.
- Import: Ctrl+I browser or drag files/folders onto the window. An FBX/OBJ with several meshes becomes a hierarchy folder, one object per mesh, each with its own texture (`MeshRenderer.ModelPart`).
- Code editor: Ctrl+Space completion, **Log** panel with **Copy** and **Save** (readable report to clipboard or `Logs/latest.txt`; paste it to an AI when asking for a bug fix).

## 8. When asked to fix a bug
1. Ask for (or read) the exported log report (`Logs/latest.txt`, updated automatically after every error): it lists each compile problem with file, line, column and the offending source line.
2. Fix only the named script; keep public property names (saved scenes reference them).
3. Reply with the full corrected file so it can be pasted into the code editor and compiled with Ctrl+S.
