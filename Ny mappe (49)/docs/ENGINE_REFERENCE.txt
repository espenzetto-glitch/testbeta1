# SharpEngine - complete reference for tools and AI assistants

This file describes the whole SharpEngine project: what it is, how the project is laid out, every file format, how to write code for it
(C# 15 preview on .NET 11, with a .NET 8 fallback), how the editor works, and the complete public API (generated from the source code).
Give this whole file to another program or AI assistant so it knows the engine's syntax and behaviour.

How to read it:
- Part 1 is the overview and setup, part 2 file formats, part 3 how to write scripts (with templates), part 4 the editor and features,
  part 5 troubleshooting, part 6 the full API listing.
- In the API listing a line is `- return-type Name(parameters) - description`. `static` / `abstract` / `virtual` / `override` are shown when they apply.
  Properties show `{ get; set; }`. Types are in namespace SharpEngine.Graphics3D unless a namespace heading says otherwise.
- Rules for generated code: write plain C#, no Unity or other engines' APIs. Scripts live in `Scripts/*.cs`, have NO namespace and NO using lines
  (global usings are provided), and each file may contain several classes.

# PART 1 - OVERVIEW AND SETUP

## Getting started

## Requirements
- Windows, macOS or Linux
- **.NET 11 SDK (preview or RC)** for C# 15 (`union`, `[with(...)]`, labeled break). Download: https://dotnet.microsoft.com/download/dotnet/11.0 . Check with `dotnet --list-sdks`.
  - Only have .NET 8? Add `-p:SharpTfm=net8.0` to every `dotnet` command; everything works except C# 15 features.
- Raylib-cs 7.0.1 is restored from NuGet (native libraries included). A graphics card with OpenGL 3.3 is needed for the GPU renderer; otherwise the CPU renderer is used.

## Install and run (Windows cmd)
**PowerShell** (the default Windows Terminal window):
```powershell
mkdir C:\Games -Force; cd C:\Games
tar -xf "$env:USERPROFILE\Downloads\SharpEngine.zip"
cd SharpEngine
dotnet run --project samples\Game3D
```
**Command Prompt (cmd):**
```bat
cd /d C:\Games
tar -xf "%USERPROFILE%\Downloads\SharpEngine.zip"
cd SharpEngine
dotnet run --project samples\Game3D
```
Type these lines into the terminal; do not paste the README itself. `cd /d` and `%USERPROFILE%` only work in cmd.
Hold the right mouse button and use W A S D / Q E to fly (Shift = faster). F5 = Play, F1 hides the editor, F4 = code editor, F3 = chunk debugger.

## Project layout
| Folder | What it is |
|---|---|
| `src/SharpEngine` | 2D engine core: game loop, scene, input, net client |
| `src/SharpEngine.Raylib` | Raylib renderer/input for the 2D engine |
| `src/SharpEngine.Graphics3D` | 3D engine + editor: World, GameObject, Component, MeshRenderer, GPU renderer, SceneManager, streaming, scripting, code editor, build pipeline |
| `src/SharpEngine.Web` | the real engine compiled to WebAssembly (browser player) |
| `samples/Game3D` | the 3D sample and editor you run |
| `samples/*` | 2D demos (server, console client, window) |
| `Directory.Build.props`, `global.json` | .NET version (net11.0 preview), C# preview, SDK rules |

Your game's data lives next to the sample: `Scenes/`, `Scripts/`, `Assets/`, `WorldMap.json`, `BuildSettings.json`, `Logs/`, `Backups/`.

## Where to read next
- [The editor](editor.md) - hierarchy, inspector, building a player
- [Scenes, portals and the world](scenes-and-worlds.md)
- [Scripting and code editors](scripting.md)
- [Rendering and web export](rendering-and-export.md)
- [Architecture](architecture.md)
- [Troubleshooting](troubleshooting.md)

## Architecture

## Layers
1. **SharpEngine** - game loop, `Scene`, `IRenderer`, `IInput`, `NetClient`, `EventBus`.
2. **SharpEngine.Raylib** - window renderer and input for layer 1.
3. **SharpEngine.Graphics3D** - the Unity-style 3D engine and the editor:
   - `World` owns `GameObject`s; each has a `Transform` and `Component`s (`MeshRenderer`, `CameraComponent`, `MeshGenerator`, your scripts).
   - `SceneManager` loads/saves scenes (JSON), handles portals, merging and build order. `WorldStreamer` loads neighbour scenes as chunks around the camera.
   - `ScriptManager` compiles `Scripts/*.cs` with Roslyn into a collectible assembly; `WorldScript` classes run for the whole game.
   - `AdminEditor` (immediate-mode `Gui`), `CodeEditorWindow` + `CompletionEngine`, `WorldMapWindow`, `BuildSettingsWindow`, `BuildPipeline`.
4. **SharpEngine.Web** - the same engine sources compiled to WebAssembly; drawing goes through `IFrameSink` to WebGL, input through `IInputSource`.

## Rendering
`World.Render` -> `GpuRenderer` (uploads each `MeshData` once as <=65,535-vertex chunks, lit by a shader, frustum culling) with a per-vertex CPU fallback in `MeshRenderer`. `World.Collect(IFrameSink)` is the CPU path used by the browser player.

## Safety net
Exceptions in scripts, editor code and generators are caught (`ErrorLog`/`Guard`), logged to `Logs/errors.log`, and a scene backup is written to `Backups/`. A script that keeps throwing is disabled instead of crashing the game.

## Tests
Headless checks cover scene IO, streaming, merging, scripting, completion, mesh chunking/culling and web export. The GPU path was also checked in a real window.

# PART 2 - FILE FORMATS

## Folder layout of a game (next to samples/Game3D or inside a built game)
```
Scenes/<Name>.json          one file per scene (objects, components, lighting, sky, world size)
Scripts/*.cs                your component scripts (compiled with Roslyn while the editor runs and when a built game starts)
Scripts/World/WorldCode.cs  World Code (WorldScript classes), edited from the World Map window
Assets/                     textures (png/jpg), FBX/OBJ/GLTF models, anything referenced by relative path
WorldMap.json               where each scene sits on the World Map grid, plus merge groups
BuildSettings.json          build/export settings and the scene build order
Logs/errors.log             every caught error with stack trace
Backups/                    automatic scene backups made before risky operations
```

## Scene file (Scenes/<Name>.json)
Coordinates are in world units (1 unit = 1 metre), Y is up, right handed. Colours are [r,g,b,a] 0-255. Rotation is Euler degrees [x,y,z].
Every component stores its Inspector properties under `properties` by property name. A scene can also have components pinned to the whole scene
(they live on a hidden object) instead of one object. Excerpt of a real file (first two objects):
```json
{
 "version": 1,
 "settings": {
  "sunDirection": [-0.4267896, -0.8535792, -0.29875273],
  "sunColor": [255, 244, 222, 255],
  "sunIntensity": 1,
  "ambient": 0.28,
  "skyTop": [70, 120, 200, 255],
  "skyBottom": [190, 215, 240, 255],
  "showGrid": true,
  "showAxes": true
 },
 "objects": [
  {
   "name": "Main Camera",
   "active": true,
   "position": [-9, 1.0, -7.2],
   "rotation": [0, 0, 0],
   "scale": [1, 1, 1],
   "components": [{ "type": "CameraComponent", "properties": { "Enabled": true, "FieldOfView": 60 } }, { "type": "FlyCamera", "properties": { "Enabled": true, "Speed": 6, "FastMultiplier": 3, "Sensitivity": 0.12 } }]
  },
  {
   "name": "Ground",
   "active": true,
   "position": [0, 0, 0],
   "rotation": [0, 0, 0],
   "scale": [24, 1, 24],
   "components": [
    {
     "type": "MeshRenderer",
     "properties": {
      "Enabled": true,
      "ModelPath": "",
      "TexturePath": "",
      "TileU": 1,
      "TileV": 1,
      "PrimitiveName": "Ground",
      "Tint": [255, 255, 255, 255],
      "Unlit": false,
      "DoubleSided": true,
      "Specular": 0,
      "Shininess": 32
     }
    }
   ]
  }
 ]
}
```
Component types usable in a scene file: every class derived from Component in the engine or compiled from Scripts/ (written by class name in `"type"`).
Properties of type float, int, bool, string, Vector3 (saved as [x,y,z]) and Color (saved as [r,g,b,a]) are saved and shown in the Inspector;
other property types work in code but are not saved.

Built-in components and their saved properties:
- MeshRenderer: Enabled, ModelPath, TexturePath, TileU, TileV, PrimitiveName (Cube, Sphere, Pyramid, Cylinder, Triangle, Ground, ...), Tint, Unlit, DoubleSided, Specular, Shininess
- CameraComponent: Enabled, FieldOfView;  FlyCamera: Speed, FastMultiplier, Sensitivity
- ScenePortal: TargetScene, Mode (Load | Merge | Unmerge | Toggle), Radius, RequireInteract, InteractKey, Prompt, ShowDebug
- Your own scripts: their public get/set properties.

## WorldMap.json
```json
{
  "chunks": [ { "scene": "MainScene", "x": 0, "y": 0 }, { "scene": "Level2", "x": 1, "y": 0 } ],
  "groups": [ { "name": "Castle", "auto": false, "scenes": ["MainScene", "Level2"] } ]
}
```
`x`,`y` are grid cells; the cell size is the biggest scene size. A merge group lists scenes that can be merged into one bigger scene (`auto` merges them when the game starts).

## BuildSettings.json
```json
{
  "Scenes": [ { "Path": "Scenes/MainScene", "Enabled": true } ],
  "ProductName": "MyGame", "WindowWidth": 1280, "WindowHeight": 720, "StartFullscreen": false, "SelfContained": false,
  "Platform": "Standalone | WebGL", "WebEngine": "RealEngine | Lite", "Target": "Windows | Linux | MacOS", "Architecture": "X86 | X64 | Arm64",
  "DevelopmentBuild": false, "CopyPdbFiles": false, "CreateVisualStudioSolution": false, "Compression": "Default",
  "StreamWorld": true, "StreamRadius": 1, "OutputFolder": ""
}
```
The first enabled scene is the start scene; the order is the scene index used by portals and `SceneManager`.

# PART 3 - WRITING CODE (SYNTAX)

## Language
C# preview (`<LangVersion>preview</LangVersion>`) on .NET 11 (fallback .NET 8 with `-p:SharpTfm=net8.0`). Scripts are compiled by Roslyn into a collectible
assembly "SharpEngine.Scripts" each time you press Ctrl+S in the code editor; a failed compile keeps the old scripts running. All language features the
compiler supports work: records, primary constructors, collection expressions `[1, 2, 3]`, pattern matching, and from C# 15 preview `union`, `closed`,
`[with(...)]` collection arguments, extension indexers, labeled break/continue, relaxed unsafe. The two runtime types `union` needs (UnionAttribute, IUnion) are added automatically.
`unsafe` code is allowed in scripts.

## Global usings in every script (do not write them)
```
using System; using System.Collections.Generic; using System.Linq; using System.Numerics; using SharpEngine.Graphics3D;
using Color = Raylib_cs.Color;  using KeyboardKey = Raylib_cs.KeyboardKey;  using MouseButton = Raylib_cs.MouseButton;  using Rl = Raylib_cs.Raylib;
```
Vector2/3/4, Quaternion, Matrix4x4 are System.Numerics. Raylib functions are `Rl.Something(...)`, colours `Color.Red` or `new Color(255, 0, 0, 255)`.

## Component script (Scripts/Spin.cs)
```csharp
// Public properties of type float, int, bool, string, Vector3 and Color show up in the Inspector and are saved in the scene.
public class Spin : Component
{
    public float Speed { get; set; } = 1f;

    public override void Start() { }

    public override void Update(float dt)
    {
        // Transform is the object's position, rotation and scale; World is the scene; GameObject is the owner.
        Transform.Rotate(new Vector3(0f, 45f * Speed * dt, 0f));
    }
}
```
Lifecycle: `Start()` once when play starts (or when added during play), `Update(float dt)` every frame while playing. A component with
`public override bool RunInEditMode => true;` also updates in the editor (MeshGenerator does). Exceptions are caught: the component is reported in the Log and disabled instead of crashing.
Useful in a component: `Transform.Position`, `Transform.EulerAngles` (degrees), `Transform.Scale`, `Transform.Forward`, `Transform.Translate(v)`, `Transform.LookAt(v)`, `GameObject.Name`, `GameObject.GetComponent<T>()`, `GameObject.AddComponent(...)`, `World.Instantiate(name, position)`,
`KeyEvents.WasPressed(KeyboardKey.L)`, `Platform.Input.IsKeyDown(KeyboardKey.W)`, `Rl.IsKeyDown(KeyboardKey.Space)`. The editor flies a separate `World.EditorView` camera while editing; the scene's CameraComponent is used in play mode. The exact members are in part 6.

## World script (Scripts/World/WorldCode.cs) - code for the whole game
```csharp
public class MyWorld : WorldScript
{
    public override void Start() { }
    public override void OnSceneLoaded(string scene) { }                         // a scene was opened (also once at play start)
    public override void OnMergeChanged(IReadOnlyCollection<string> merged) { }  // scenes merged or split
    public override void Update(float dt) { }
    // helpers: Find(name), LoadScene(scene), Merge(...), Unmerge(...), CurrentScene, MergedScenes, Scenes (every scene/object/component of the project)
}
```

## Mesh script - code that creates triangles
```csharp
public class WaveMesh : MeshGenerator
{
    public float Size { get; set; } = 20f;
    public int Cells { get; set; } = 40;
    public float Height { get; set; } = 1.5f;
    public Color Low { get; set; } = new Color(40, 120, 60, 255);
    public Color High { get; set; } = new Color(230, 230, 200, 255);

    public override void Generate(MeshBuilder b)
    {
        b.AddGrid(Size, Size, Cells, Cells,
            (x, z) => MathF.Sin(x * 0.5f) * MathF.Cos(z * 0.5f) * Height,
            h => MeshBuilder.Lerp(Low, High, Math.Clamp(h / Height * 0.5f + 0.5f, 0f, 1f)));
    }
}
```
The mesh is rebuilt when the script compiles and whenever a public property changes. MeshBuilder: `AddVertex(position, normal, color?, uv?)` returns the vertex number,
`AddTriangle(a, b, c)`, `AddFlatTriangle`, `AddFlatQuad`, `AddBox`, `AddSphere`, `AddGrid`; `VertexCount`, `TriangleCount`. Millions of triangles are fine (GPU renderer).

## Logging
`Debug.Log(x)`, `Debug.LogWarning(x)`, `Debug.LogError(x)`, `Debug.LogException(ex)`, `Debug.Assert(cond, "text")`, or Console.WriteLine.
All of it appears in the code editor's Log panel.

## Union types (C# 15)
```csharp
public record class Cat(string Name);
public record class Dog(string Name);
public union Pet(Cat, Dog);
// Pet p = new Cat("Tom");   string n = p switch { Cat c => c.Name, Dog d => d.Name };
```


# PART 4 - EDITOR AND FEATURES

## The editor

The in-game admin editor (press F1 to hide it) and the build tools.

## 3D client (OpenGL, no server needed)
```
dotnet run --project samples\Game3D
```
Unity-style API in `src/SharpEngine.Graphics3D`: `World`, `GameObject`, `Component`, `Transform`,
`CameraComponent`, `MeshRenderer`, `Primitives` (cube, sphere, cylinder, pyramid, plane, triangle).
Hold the right mouse button and use WASD / Q / E to fly, Shift = faster, L pauses the sun.

## Admin editor (in the 3D client)
`samples\Game3D` opens with a static in-game editor (`SharpEngine.Graphics3D.Editor.AdminEditor`),
started by `App3D.Run(...)`. It starts in edit mode: scripts are paused until you press Play.

| Action | How |
| --- | --- |
| Fly camera | hold right mouse + WASD, Q/E down/up, Shift fast |
| Select | click an object in the scene or the Hierarchy |
| Move | drag the red/green/blue arrows |
| Edit values | Inspector: drag a number to change it, click it to type, Enter to confirm |
| Add / remove component | Inspector > Add Component, or the x on a component header |
| Create | toolbar: Empty, Cube, Sphere, Cylinder, Pyramid, Plane, Triangle |
| Duplicate / delete | Ctrl+D / Delete (or toolbar) |
| Play / Stop | F5 (Stop restores the scene as it was) |
| Save / Open scene | Ctrl+S / toolbar, scenes are JSON files in the project's `Scenes` folder (`Scenes` menu opens them) |
| Hide editor | F1 |

Any `Component` subclass with a public parameterless constructor shows up automatically; its public
float / int / bool / string / Vector3 / Color properties appear in the Inspector and are saved.

## Build Settings (scenes + building a player)
Open it from the toolbar (**Build Settings...**) or with Ctrl+Shift+B.

- **Scenes In Build**: *Add Open Scenes* adds the scene you are editing (it is saved first). Tick/untick scenes, reorder with Up/Down, remove with Remove.
- **Platform**: PC, Mac & Linux Standalone is buildable. Target Platform, Architecture, Development Build, Copy PDB files, Create Visual Studio Solution and Compression Method are active. UWP, WebGL, tvOS, PS4, iOS, Xbox One, Android and the greyed options (Server Build, Autoconnect Profiler, Deep Profiling, Script Debugging, Scripts Only Build) are shown but disabled.
- **Player Settings...**: product name, window size, fullscreen, bundle the .NET runtime.
- **Build** runs `dotnet publish` in the background and writes `Builds/<Product>_<rid>` (log in `Builds/last-build.log`). **Build And Run** also starts it when the target matches your OS.

Moving between scenes in the game (play mode and built player):
- `Tab` / `Shift+Tab` next / previous scene, `1`-`9` jump to a build index.
- Add a `ScenePortal` component to any object (TargetScene, Radius): fly the camera into it to load that scene.
- From code: `SceneManager.LoadScene("Level2")`, `LoadNext()`, `LoadPrevious()`.

`samples\Game3D` creates three linked demo scenes (MainScene, Level2, Level3) on first run.

## Scenes and UI scale
- Build Settings > **New Scene** creates a scene file and adds it to the build; **Add Existing** adds any scene from the Scenes folder; **Add Open Scenes** adds the one you are editing.
- **UI Scale** slider in the status bar (bottom right, 75%-200%) or Ctrl +, Ctrl -, Ctrl 0. The value is applied when you release the slider and saved in `EditorPrefs.json`.

## Textures, FBX models and dragging
- **Drag a component onto an object:** grab a component header in the Inspector (the `::` grip) or an entry of the Add Component list and drop it on a Hierarchy row or on an object in the scene. Inspector headers copy the component with its values.
- **Textures:** every mesh (Cube, Sphere, Cylinder, Pyramid, Plane, Triangle, Ground, imported models) has UVs. Set `Texture Path` on a Mesh Renderer (type it, or use the `...` button to pick from the Assets folder) and use `Tile U` / `Tile V` to repeat it. Two sample textures are created in `Assets/Textures` (`checker.png`, `bricks.png`).
- **Drop files on the window:** an image (.png .jpg .bmp .tga) dropped on an object, or on its Hierarchy row, is copied to `Assets/Textures` and applied; if nothing is under the mouse it applies to the selected object. A model (.fbx binary, .obj) dropped anywhere is copied to `Assets/Models` and appears as a new object, together with its texture if the file points at one next to it or embeds it.
- FBX: binary FBX only (export as "FBX Binary"), polygons are triangulated, node transforms, Z-up and the unit scale are applied. Animation and skinning are ignored; each mesh keeps its own diffuse texture (see "Several meshes per FBX, and folders" below).
- Builds copy `Assets` next to the player; the WebGL build embeds textures (.png/.jpg) and the models.


## Editor view: fly around and place objects before you play

While you edit (the status bar says EDIT MODE) you look through the **editor camera** ("Scene View"). It is separate from the game camera:
flying around never moves the game's Main Camera, and **Play** switches to the game camera. Stop returns to where you left the editor view.
The toolbar button **Scene View / Game View** switches to look through the game camera while editing. Game cameras are drawn as small pyramids in the scene.

| Input | What it does |
|---|---|
| Right mouse (hold) + W A S D | fly (Q / E = down / up, Shift = faster) |
| Mouse wheel while flying | change the fly speed |
| Mouse wheel | move forward / back |
| Middle mouse drag | slide the view sideways / up |
| Alt + left mouse drag | orbit around the selected object |
| F (or the **Focus (F)** button) | fly to the selected object |
| G (or **Ground (G)**) | drop the selected object onto the ground |
| **To View** | move the selected object (for example a camera) to the editor view's position and direction |
| Left mouse on an object | select it; drag the coloured arrows to move along an axis |
| Left mouse drag on the already selected object | slide it over the ground |
| Ctrl while moving, or the **Snap** button | snap to a 0.5 grid |
| Create menu | new objects appear on the ground in the middle of your view |

Put the game camera where you want it by flying there, selecting the Main Camera and pressing **To View**.

## Several meshes per FBX, and folders

**Several meshes, each with its own texture.** An FBX (or an OBJ with several `usemtl` materials) can hold many meshes. The importer keeps them apart and gives each its own texture, found through the file's own links (Material -> Texture), next to the file or in a sub-folder. A file with one mesh becomes one object. A file with several becomes a **folder** with one object per mesh (`MeshRenderer.ModelPart` says which mesh of the file each object draws; empty = the whole file). All meshes keep the positions they have in the file, so a model made of "world chunks" lines up on its own.

**Import... button (Ctrl+I)** opens the built-in file browser: pick a place on the left (Project Assets, Home, Desktop, Documents, Downloads, drives) or type a path, open folders by double-click, select files (click, Ctrl+click for several) and press **Import Selected**, or **Import This Folder + Subfolders** to bring in every model below the current folder. Double-clicking a file imports it. Only folders and importable files (.fbx .obj .png .jpg .bmp .tga) are listed.

**Drag a whole folder** onto the window to import every `.fbx`/`.obj` below it. Sub-directories become sub-folders in the Hierarchy; textures are searched in the whole directory tree. You can drop several files at once too. A file dropped while a folder is selected goes into that folder.

**Hierarchy folders.** Folders and sub-folders (`Town/Houses`) organise objects. They have no transform, only a name.
- `+ Folder` (Hierarchy header) makes a folder inside the selected folder, or wraps the selected object.
- `[-]`/`[+]` in front of a folder collapses it; click the name to select the folder.
- Object inspector: **Folder** drop-down moves the object. Folder inspector: rename, move into another folder, new sub-folder, Hide All / Show All, Delete Folder (objects move up) or Delete + Objects.
- Folders are saved in the scene file (`"folder"` on objects, `"folders"` for empty ones).
- Script API: `world.Folders.Create / Put / Move / Rename / Delete / Objects / TryGetBounds / SetActive`, and `GameObject.Folder`.

**F flies to the selection.** Select an object *or a folder* in the list (or click it in the scene) and press **F**: the editor view moves to it. For a folder the view frames everything inside it, sub-folders included.

## Exporting errors and the log

The code editor (scripts, World Code and mesh code all use the same window) has **Copy** and **Save** buttons on its error list and on the **Log** panel.
- **Copy** puts a readable text report on the clipboard (the Log panel copies only the lines its Info/Warnings/Errors filters show). Paste it into any editor or chat.
- **Save** writes `Logs/log-<date>-<time>.txt` and `Logs/latest.txt` (UTF-8 with BOM and Windows line endings, so Notepad and VS Code show it correctly).
- The report has a header (scene, project, runtime, counts), then each compile problem with the failing source line and a `^` under the column, the runtime error, and the log with stack traces indented under their entry. From code: `LogExport.Build()` / `LogExport.Save()`.
- `Logs/errors.log` (every caught error) uses the same indented layout.

## Deleting scenes, and exporting the bug log in one click

**Manage (Ctrl+Shift+M)** opens a list of every scene file. Click rows to tick them (or Select all / Select none), then **Delete Selected** and click it a second time to confirm. Double-click a row to open that scene. The scene that is open cannot be deleted. A deleted scene is moved to `Backups/DeletedScenes/<name>-<date>.json` (copy it back into `Scenes/` to undo) and is removed from the build list, the World Map and its merge group. From code: `SceneManager.Delete(name, out reason)` and `SceneManager.DeleteMany(names, out problems)`.

**Export Log (Ctrl+Shift+L, or the button in the status bar)** saves the whole readable report to `Logs/log-<date>.txt` and `Logs/latest.txt` and copies it to the clipboard. The button shows how many errors are in the log. `Logs/latest.txt` is also refreshed automatically a moment after every error, so it is always ready to send.

## Dynamic editor: tools that add to the editor itself

The admin editor is not fixed. Everything beyond its core layout is an `EditorItem` registered while it runs, by the engine's own `BuiltInTools` and by your scripts, through the same `EditorApi`. A script reload registers it all again, with no restart.

Write a tool in the code editor: **Create Editor Tool** (it lands in `Scripts/Editor/Name.cs`), press **Ctrl+S**:

```csharp
public class MyTools : EditorScript
{
    private int _count = 5;
    public override void OnRegister(EditorApi editor)
    {
        editor.AddCommand("Say hello", world => editor.Say("Hello from " + CurrentScene));       // toolbar > Tools menu
        editor.AddToolbarButton("Add Cube", world => editor.Select(world.CreatePrimitive("Cube", Color.White, new Vector3(0, 1, 0))));
        editor.AddPanel("My tool", ui =>                                                          // bottom of the Inspector, every frame
        {
            ui.Slider("Count", ref _count, 1, 50);
            if (ui.Button($"Spawn {_count} cubes")) { /* ... */ }
        });
        editor.AddObjectInspector<MeshRenderer>("Mesh tools", (ui, go) => ui.Dim(go.Name));      // only for objects with a MeshRenderer
        editor.AddShortcut(KeyboardKey.F9, world => editor.Say("pressed"), ctrl: true);          // Ctrl+F9
    }
}
```

| `EditorItem` | Added with | Appears as |
| --- | --- | --- |
| `ToolbarButton` | `AddToolbarButton` | button in the toolbar (inside **More** when the window is narrow) |
| `MenuCommand` | `AddCommand` | entry of the **Tools** menu |
| `PanelItem` | `AddPanel` | collapsible section at the bottom of the Inspector |
| `ObjectInspector` | `AddObjectInspector` / `AddObjectInspector<T>` | section for the selected object (of component type T) |
| `ShortcutItem` | `AddShortcut` | keyboard shortcut |

`PanelBuilder` (the `ui`): `Heading, Label, Dim, Separator, Space, Button, Buttons(...), Toggle, Slider (float or int), Text, Dropdown`. Each returns true when changed or clicked. An `EditorScript` also gets `OnUpdate(dt)`, `OnSelectionChanged(go)` and `OnSceneLoaded(scene)`. A tool that throws is switched off and logged; the others keep working. The engine's own tools (Frame Everything on Home, Show All Objects, Scene Statistics, Mesh Info, ...) are in `Editor/BuiltInTools.cs` and are good examples.

**Union.** The set of item kinds is a closed type: in C# 15 it is `union EditorItem(ToolbarButton, MenuCommand, PanelItem, ObjectInspector, ShortcutItem)`. The engine declares it as a closed record hierarchy (it builds on every SDK) and handles every case in one exhaustive `switch` (`EditorExtensions.Describe`). Inside your own scripts you can use a real `union` (see AI_GUIDE.md) on a .NET 11 / C# 15 toolchain.

## Scenes, portals and the world

Portals, the World Map, streaming, chunk debugger and merging scenes.

## Scene portals: interaction and debug view
- A `ScenePortal` is now interactable by default: walk (fly) within **Radius** and press **E** (`Interact Key`) to enter. A prompt ("Press E to enter Level2", or your own `Prompt` text) appears at the bottom of the screen. Untick **Require Interact** for the old behaviour (enter automatically). E also works while you hold the right mouse button to fly.
- **Show Debug** draws, in the editor, three rings for the trigger radius, a beam above the portal and a line toward the camera, plus a label `Portal -> Level2  r=2.5  d=4.3  [E]  IN RANGE`. Orange = needs the key, yellow = automatic, green = camera in range.
- WebGL: the prompt and the E key work in the browser; the rings are drawn in Development Build exports.
- Portals saved before this change have no `Require Interact` value and pick up the new default (press E).

Note on UI scale: the scale is limited automatically so the Hierarchy, Inspector and a usable 3D view always fit the window (the slider then shows e.g. `200% (fits 129%)`). Ctrl+0 resets it to 100%; the setting is stored in `EditorPrefs.json`.
Portals only work in Play mode (F5); pressing the key near one in Edit mode shows a hint in the status bar, and the debug label says "press Play F5 to use it".
Key presses for portals use Raylib's key queue (`KeyEvents`), so even a very quick tap of E is never missed, and the scene change happens in the same frame.
While a portal is showing its prompt, its key (E) is reserved: the fly camera ignores it, so pressing E to enter a portal no longer also flies you up. Use Q/E elsewhere as before.

## Portals

Portals only work in **Play mode** (F5). Walk within the ring and press E. A portal that points at the scene you are already in does nothing, and portals of streamed neighbour scenes never fire on their own (automatic portals) while you walk past.

## World Map and scene size

Every scene is its own world with its own size. Open the **World Map** from the toolbar (or Ctrl+M).

- Each scene is a chunk on a grid. New scenes get a free cell automatically; click a "+" cell to create a scene right there. The map grows as you add more scenes. Layout is stored in `WorldMap.json`.
- Drag a chunk to move it (dropping on another chunk swaps them). Double-click a chunk to open it and edit it as its own world.
- The side panel sets Width and Depth for any scene, toggles whether it is in the build, and can fit the ground to the size.
- In Scene settings, **World Size** has Width X / Depth Z, **Show Bounds** (cyan edge in the editor) and **Clamp Camera** (keeps the player camera inside the scene when playing and in the WebGL build).
- New scenes default to 60x60 with the camera clamped. Scenes saved earlier load as 200x200 and unclamped.

## World streaming (one continuous world)

While playing, the World Map is one world you can walk through. The scene you start in is the anchor and stays where it is. Neighbouring scenes are loaded into the same world at their map position (cell offset x cell size) and unloaded again when you leave them behind.

- Turn it on or off in the World Map side panel: **Stream neighbours (play)**, with a **Load radius** (1 = 3x3 chunks, 2 = 5x5). Saved in `BuildSettings.json` (`StreamWorld`, `StreamRadius`).
- Cell size is the biggest scene in the build. Give neighbouring scenes the same size for seamless ground; smaller scenes sit in the middle of their cell.
- Only scenes in the build stream, so the editor and the built player match (an empty build list streams every scene).
- Scene files load on a background thread and objects are created a few per frame. A chunk is only unloaded when it is more than radius + 1 cells away, so crossing a border back and forth never flickers.
- Neighbour cameras are ignored. Clamp Camera now keeps the camera inside the whole map. Sky, sun colour, sun strength and ambient light blend to the chunk you stand in.
- Saving never writes streamed objects into the open scene. Stopping Play removes them.
- Standalone builds ship a `WorldMap.json` with the built scenes. The WebGL build streams the same way (the HUD shows `Scene > Chunk`).
- The main scene stays loaded even when you are far away. Portals still switch scenes with a normal load, and the new scene becomes the anchor.

### Chunk debugger (F3)

F3 shows or hides the chunk debug view (on by default in the editor, off in a player build until you press F3). In the 3D view every chunk of the map gets a border for its scene and a dim square for its cell, with a name label (cell, state, object count):

- blue = the scene you opened, yellow = the chunk you stand in, green = loaded, orange = loading, grey = not loaded (or "map" in edit mode, which shows the layout without streaming).
- the white square is the area that stays loaded around you, with a cross where you stand. A status line shows the chunk you are in, loaded count and radius.

## Merging scenes into a bigger scene

Scenes can be joined into one bigger scene, temporarily, while the game runs:

- **Merge groups (World Map):** select a scene and type a name in *Merge group*. Scenes with the same name form a group (shown with a coloured border and `[name]` on the map). Tick *Merge automatically in play* and the whole group is merged as soon as the player is in any of its scenes.
- **From code:** `SceneManager.Merge("Castle")` (a scene name or a group name, several allowed), `SceneManager.Unmerge()`, `SceneManager.ToggleMerge("Castle")`.
- **Portals:** `ScenePortal.Mode` = `Load` (default), `Merge`, `Unmerge` or `Toggle`. A merge portal needs no scene change: walk up, press E, and the target scene (or group) appears next to yours.

A merged scene is loaded at its World Map position and stays loaded wherever the player is (it is not unloaded by distance). The camera may walk across the whole merged area, even when *Stream neighbours* is off. Unmerging, opening another scene or stopping Play ends the merge; the extra scenes unload and the camera returns to the open scene. Several scenes can be merged at once. The chunk debugger (F3) shows merged scenes in purple.

Merging works on the desktop player and the real-engine WebAssembly build; the Lite JavaScript web player only loads a merge portal's scene.

## Scripting and code editors

Component scripts, World Code, mesh code, C# preview and union types. All code editors share the same window (Code, F4) with completion (Ctrl+Space).

## Scripts, the code editor and scene components

**Code editor.** Click **Code (F4)** in the toolbar. It lists the project's `Scripts/*.cs` files and edits them with syntax colours, line numbers, undo/redo, Ctrl+C/X/V, Tab/Shift+Tab, Ctrl+/ (comment), Ctrl+D (duplicate line) and Alt+Up/Down (move line). Type a name and press **Create Script** for a starter component. **Ctrl+S** saves every changed file and compiles them (with Roslyn). Errors are listed under the editor with file and line; click one to jump to it. While there are errors the old scripts keep running.

**Scripts are components.** Every public class that derives from `Component` shows up in **Add Component**. Public properties of type `float`, `int`, `bool`, `string`, `Vector3` and `Color` appear in the Inspector and are saved with the scene. `Start()`, `Update(float dt)` and `OnDestroy()` work as for the built-in components. `Transform`, `GameObject` and `World` are available inside a script, and `System`, `System.Numerics`, `System.Linq`, `SharpEngine.Graphics3D`, `Color`, `KeyboardKey` and `Rl` (Raylib) need no `using`. When new scripts are applied the open scene is reloaded so its components use them. A script that throws is switched off and the error is shown in the status bar instead of crashing the game.

**Scene components.** The first row of the Hierarchy is the scene itself (`[scene] Name`). Select it and add components with **Pin Component To Scene**: they belong to the whole scene (not to an object), are saved in the scene file, and run while that scene is loaded or streamed as a neighbouring chunk. Every component of every object also has a **Pin** button that copies it to the open scene or to any other saved scene. You can also drag a component onto the `[scene]` row.

**Builds.** A standalone build copies the `Scripts` folder next to the game and compiles it when the game starts (adds a second or two). The WebGL build cannot run C#, so script components are left out with a warning.

**Developing without internet.** The engine references the `Microsoft.CodeAnalysis.CSharp` NuGet package. `-p:UseSdkRoslyn=true` uses the compiler inside the .NET SDK instead.

### When something goes wrong

The editor and the game are built to carry on instead of closing:

- Every part of a frame (update, drawing, editor panels, your `onGui`) is guarded. An error is shown in the status bar, written once to `Logs/errors.log`, and the next frame runs normally. In the editor, a Play session that fails 30 frames in a row is stopped and the scene is restored.
- A script that throws is switched off (its **Enabled** box is cleared) and the error is shown. Errors in a script's constructor, property getters/setters or `OnDestroy` skip only that component or value; the scene still loads and saves.
- A compile that fails changes nothing, and if reloading the scene after a successful compile fails, the scene is put back. The editor keeps the newest ten copies of the scene in `Backups/` (before applying scripts, and whenever an error happens while editing).
- A problem inside the code editor window closes only that window; its unsaved text is kept in memory.
- World streaming turns itself off for the rest of that play session if a chunk fails, instead of stopping the game.
- `Scripts/*.cs` must not be compiled by `dotnet build` (the editor does that). The sample project already excludes it, and creating a script adds the same `<Compile Remove="Scripts/**" />` to your own `.csproj` automatically.

A script with an endless loop cannot be interrupted: that freezes the program, so stop and run it again (your last compiled scene copy is in `Backups/`).

## World Code (code editor in the World Map)

World Map > **World Code** opens the code editor on `Scripts/World/WorldCode.cs`, a **WorldScript**: code for the whole game instead of one object or scene. One instance of each WorldScript class is created when play starts and lives until play stops, across scene changes, merges and streaming.

```csharp
public class WorldCode : WorldScript
{
    public override void Start() { }
    public override void OnSceneLoaded(string scene) { }
    public override void OnMergeChanged(IReadOnlyCollection<string> merged) { }
    public override void Update(float dt) { }
    public override void OnStop() { }
}
```

Helpers: `World`, `Find("name")`, `LoadScene`, `Merge`, `Unmerge`, `CurrentScene`, `MergedScenes`, and `Scenes` / `WorldIndex` (every scene, object, component and saved value of the project, also for scenes that are not loaded, e.g. `WorldIndex.Find("Door")`, `WorldIndex.WithComponent("ScenePortal")`). A script that throws is reported and switched off. Create more with "Create World Script". They are compiled with Ctrl+S like other scripts, and included in standalone and WebAssembly builds.

The editor knows the whole engine:
- **Ctrl+Space** or a **dot** opens suggestions: every public class, property and method of the engine (found by reflection), following the expression, e.g. `Find("Door").Transform.` lists the members of Transform. Up/Down, Enter or Tab accepts, Esc closes.
- Inside quotes, `Find("`, `LoadScene("`, `Merge("` suggest your object names, scenes and merge groups; inside `GetComponent<` the component types.
- The **Objects** panel lists every scene, object, component and saved value. Click a name to insert it; the arrow expands; the filter box searches.

## Mesh code (code editor for triangles)

* Select an object and press **Edit Mesh As Code** in its MeshRenderer: its triangles are written as a `MeshGenerator`
  script, opened in the code editor, and attached to the object when it compiles (Ctrl+S).
* Or use **Create Mesh Script** in the code editor for an example (a wavy field of triangles) and add it to any object.
* In `Generate(MeshBuilder b)` call `AddVertex`, `AddTriangle`, `AddFlatTriangle`, `AddFlatQuad`, `AddBox`, `AddSphere`, `AddGrid`.
  Public properties show in the Inspector; changing one rebuilds the mesh at once, also in edit mode.

## C# preview

Scripts compile with `LanguageVersion.Preview` (`ScriptManager.PreviewLanguage`, default on) with unsafe code allowed, and the engine and sample projects use `<LangVersion>preview</LangVersion>`.
The project references `Microsoft.CodeAnalysis.CSharp` 5.9.0 (the newest stable Roslyn on NuGet when this was written).
C# 15 preview (collection expression arguments `[with(...)]`, `union`, `closed`, extension indexers, labeled break/continue, relaxed unsafe rules) needs a compiler that has those features:
install the .NET 11 preview SDK for your own projects. `union` also needs the runtime types `UnionAttribute`/`IUnion`, which ship with .NET 11 Preview 5 and later, so set your game's target framework to net11.0 for it.

### Union types in scripts

`union` works in scripts as soon as the Roslyn compiler has it (Microsoft.CodeAnalysis.CSharp, see above). The runtime types it needs
(`UnionAttribute`, `IUnion`) are added to every script build automatically when your .NET lacks them, so it works on any .NET:

```csharp
public record class Cat(string Name);
public record class Dog(string Name);
public union Pet(Cat, Dog);

public class Feeder : Component
{
    public override void Update(float dt)
    {
        Pet pet = new Cat("Whiskers");
        string text = pet switch { Cat c => c.Name, Dog d => d.Name };
    }
}
```
Unions are for your code only: scene files and the Inspector still store plain float/int/bool/string/Vector3/Color properties.

### Targeting .NET 11 (preview)

`Directory.Build.props` sets every project to **net11.0** with C# `preview` and uses the .NET SDK's own Roslyn compiler for scripts, so you get C# 15 (`union`, `[with(...)]`, labeled break ...).
1. Install the .NET 11 SDK (preview or RC) from https://dotnet.microsoft.com/download/dotnet/11.0 . `global.json` lets preview SDKs be used.
2. `dotnet --list-sdks` should show 11.x. Then run as usual.
3. Still on the old SDK? Add `-p:SharpTfm=net8.0` to the command (for example `dotnet run --project samples\Game3D -p:SharpTfm=net8.0`).

## Debug log

Open the code editor (**Code**, F4) and press **Log** next to Objects. The log shows, newest at the bottom:
- everything your scripts print: `Debug.Log(x)`, `Debug.LogWarning(x)`, `Debug.LogError(x)`, `Debug.LogException(ex)`, `Debug.Assert(cond, "text")` or plain `Console.WriteLine`
- compiler results and every compile error as `File.cs(line,col)`; click such a line to jump to it in the editor
- every error the engine caught (scripts, World scripts, mesh generators), with its stack trace: click a line to open the details

Filters for Info / Warnings / Errors, **Clear**, and **Follow** (scroll to the newest line). The Log button shows a red count when there are errors. The log keeps the newest 3000 lines; the same line repeated is shown once as `x5`.
`DebugLog` is the same data in C# (`DebugLog.Snapshot()`, `DebugLog.Add(...)`).

## C# 15 in the code editors

All code windows (scripts, World Code, mesh code) use one editor and one compiler, so they all get the same language support:
- **Compiling:** preview language version, `union`, `closed`, `[with(...)]`, extension indexers, labeled break/continue, relaxed unsafe, whatever the Roslyn compiler in use supports (see above).
- **Colouring and completion:** `union`, `closed`, `extension`, `safe`, `required`, `scoped`, `partial`, `record`, `with`, `not/and/or` and the other newer keywords are coloured and offered by Ctrl+Space. Types you declare (for example a union) are completed after they compiled once.
- **Not covered:** the editor has no full C# parser, so it does not understand the *meaning* of new syntax (for example it cannot offer the cases of a union inside a `switch`). Errors always come from the real compiler and show in the log.

## Rendering and web export

GPU renderer, WebGL single file and the real engine as WebAssembly.

## More triangles (GPU renderer)

Meshes are uploaded to the graphics card once (split into pieces of at most 65,535 vertices), lit by a shader and skipped
when they are outside the camera's view. A million triangles is fine. The status bar shows `N tris (+M culled) | GPU`.
If the shader cannot be created the old CPU renderer is used (`| CPU`). `GpuRenderer.Enabled = false` forces the CPU path.
The browser player still uses its own renderer.

## WebGL build
In Build Settings pick **WebGL** in the Platform list, then Build (or Build And Run, which opens it in your browser).
The output folder (`Builds/<Product>_WebGL`) holds `index.html`, `player.js` and `data.js`; open `index.html`
or upload the folder to any web host. The browser player renders your scenes (meshes, lighting, sky, grid),
fly camera, scene switching (Tab / Shift+Tab / 1-9) and the built-in `ScenePortal`, `Spinner`, `Bobber` and
`SunOrbit` components. Your own C# scripts cannot run in the browser; they are listed as warnings in `Builds/last-build.log`.

## WebGL: one file, paste and play

A WebGL build's `index.html` is now self-contained, and `game.html` is an identical copy: the whole game (player, scenes, meshes, textures) in ONE file. Open it directly (no web server needed), upload just that file anywhere, or paste its full text into an online HTML editor (CodePen, JSFiddle, ...) to get a link. Build it from Build Settings > WebGL.

## The real engine in the browser (WebAssembly)

Build Settings > platform **WebGL** now has a **Web Engine** dropdown:

- **Real engine (WebAssembly)** (default): your actual C# engine runs in the browser: the same World, scenes, streaming, portals, components and your own scripts. Only the drawing (WebGL, `src/SharpEngine.Web/wwwroot/sharp.js`) and the input (DOM events) are different.
- **Lite (JavaScript)**: the small JavaScript copy from before. No C# scripts, but it opens by double-click.

How it works: the build runs `dotnet publish src/SharpEngine.Web` (the engine sources plus your Scripts/ and the game project's .cs files are compiled to WebAssembly), then adds `game/` (scenes, WorldMap.json, BuildSettings.json, textures, models). The page downloads those files into the browser's in-memory file system and starts the game.

- The first build downloads the .NET WebAssembly runtime (needs internet and a few minutes). Later builds are fast.
- The result is a static website: upload the folder to itch.io (HTML5), GitHub Pages, Netlify or any server. It cannot be opened by double-clicking (browsers do not run WebAssembly from files). **Run** in Build Settings starts a small local server and opens the page; `Play.cmd` in the output does the same with Python.
- Scripts must not call Raylib directly (`Rl.*`), because there is no window in the browser. Use `KeyEvents.WasPressed(KeyboardKey.X)` or the engine's own classes. The built-in `FlyCamera`, `ScenePortal` and the sample scripts already do.
- Your game's own .cs files are included, except the one that calls `App3D.Run` or has `Main`.

## Console / Raylib 2D engine

The original 2D engine (`src/SharpEngine`, `src/SharpEngine.Raylib`) and its samples.

## Features
- Fixed-timestep game loop (`Engine`) with variable-rate rendering and a frame limiter
- Entity/component `Scene` with deferred destruction, tags, layers
- Backend-agnostic `IRenderer` (ANSI console + headless in-memory renderers included)
- `IInput` (console keyboard, scripted input for tests/bots)
- `NetClient`: async TCP client, length-prefixed packets, events delivered on the game thread
- `EventBus`, `Vec2`, `Rect`

## Run the demo
```
dotnet run --project samples/DemoServer                 # terminal 1
dotnet run --project samples/ArenaClient                # terminal 2 (and 3, 4...)
```
Needs an 80x24 terminal. WASD/arrows move, Esc quits.

Headless (no terminal): `dotnet run --project samples/ArenaClient -- --headless --frames 240 --hold D`

## Write your own game
Derive from `Game`, build an `Engine` with your `IRenderer`/`IInput`, call `engine.Run(game)`.
To use SDL/OpenGL/etc., implement `IRenderer` and `IInput`.

## Windowed version (Raylib)
```
dotnet run --project samples\DemoServer
dotnet run --project samples\ArenaWindow
```
Uses the `Raylib-cs` NuGet package (native libraries included). Opens a 960x576 window.

# PART 5 - TROUBLESHOOTING

## Troubleshooting

| Problem | Fix |
|---|---|
| `The framework 'Microsoft.NETCore.App' version '11.0...' was not found` / SDK not found | Install the .NET 11 SDK, or add `-p:SharpTfm=net8.0` to your command |
| NuGet cannot find `Microsoft.AspNetCore.Components.WebAssembly` 11.x | Use the .NET 11 SDK with preview feeds, or build the web part with `-p:SharpTfm=net8.0` |
| Script error on a `union` line | Needs a Roslyn compiler with C# 15; use the .NET 11 SDK (scripts use the SDK's compiler) |
| Status bar says `CPU` | The shader could not be created (old graphics driver). Everything still works, only slower |
| Scene broke after an error | Look in `Backups/`, copy the newest file into `Scenes/` |
| Details of any error | `Logs/errors.log` |
| Old text from an earlier zip is still there | Extract into a fresh folder; `tar -xf` overwrites files but does not delete removed ones |

# PART 6 - FULL PUBLIC API (generated from the compiled source)

### Namespace SharpEngine

#### sealed class Engine
The client-side engine: owns the game loop (fixed-timestep update, variable-rate render), the scene, input, renderer and network client. Call Run once.
- ctor(EngineConfig config, IRenderer renderer, IInput input)
- EngineConfig Config { get; }
- IRenderer Renderer { get; }
- IInput Input { get; }
- NetClient Net { get; }
- EventBus Events { get; }
- Scene Scene { get; }
- bool IsRunning { get; }
- long Frame { get; }
- double TotalTime { get; }
- void Quit() - Request the loop to stop after the current frame. Thread-safe.
- void Run(Game game)

#### sealed class EngineConfig
- ctor()
- string Title { get; init; }
- int UpdateRate { get; init; } - Fixed simulation steps per second.
- int TargetFps { get; init; } - Frame limiter. 0 = unlimited.
- long MaxFrames { get; init; } - Stop automatically after this many frames. 0 = run until Quit().
- bool Deterministic { get; init; } - When true, every rendered frame advances the simulation by exactly one fixed step regardless of wall-clock time. Useful for tests, replays and headless runs.
- Color ClearColor { get; init; }

#### sealed class EventBus
Minimal typed publish/subscribe bus for decoupling game systems.
- ctor()
- void Publish<T>(T message)
- void Subscribe<T>(Action<T> handler)
- void Unsubscribe<T>(Action<T> handler)

#### abstract class Game
Derive from this to build a game on top of Engine.
- ctor()
- Engine Engine { get; }
- Scene Scene { get; }
- virtual void Initialize() - Called once, after the renderer is ready and before the first frame.
- virtual void RenderOverlay(IRenderer renderer) - Called after the scene has rendered; draw HUD / debug text here.
- virtual void Shutdown() - Called once when the loop ends (also on exceptions).
- virtual void Update(GameTime time) - Called every fixed step, before entity components update.

#### struct GameTime
Timing information passed to every fixed update step.
- ctor(float Delta, double Total, long Frame) - Timing information passed to every fixed update step.
- float Delta { get; init; } - Seconds per update step (constant).
- double Total { get; init; } - Simulated seconds since the engine started.
- long Frame { get; init; } - Rendered frame counter.
- void Deconstruct(out float Delta, out double Total, out long Frame)
- override bool Equals(object obj)
- bool Equals(GameTime other)
- override int GetHashCode()
- override string ToString()


### Namespace SharpEngine.Ecs

#### abstract class Component
Behaviour or data attached to an Entity.
- ctor()
- Entity Entity { get; }
- Engine Engine { get; }
- bool Enabled { get; set; }
- virtual void OnDestroy() - Runs when the entity is destroyed or the scene is cleared.
- virtual void OnRender(IRenderer renderer) - Runs every rendered frame.
- virtual void OnStart() - Runs once, right before the first OnUpdate.
- virtual void OnUpdate(GameTime time) - Runs every fixed update step.

#### sealed class Entity
- int Id { get; }
- string Name { get; set; }
- string Tag { get; set; }
- Scene Scene { get; }
- Vec2 Position { get; set; }
- int Layer { get; set; } - Draw order: lower layers are drawn first (behind).
- bool Active { get; set; }
- bool Destroyed { get; }
- IReadOnlyList<Component> Components { get; }
- T Add<T>(T component)
- void Destroy()
- T Get<T>()

#### sealed class RectRenderer : Component
Draws a filled rectangle at the entity's position.
- ctor()
- Vec2 Size { get; set; }
- Color Color { get; set; }
- Vec2 Offset { get; set; }
- override void OnRender(IRenderer renderer)

#### sealed class Scene
Container for entities. Destruction is deferred to the end of the update step.
- Engine Engine { get; }
- IReadOnlyList<Entity> Entities { get; }
- void Destroy(Entity entity)
- Entity Find(string name)
- IEnumerable<Entity> FindByTag(string tag)
- Entity Spawn(string name, Vec2 position = null) - Creates an entity. It is visible to Find() immediately and updates this step.

#### sealed class TextRenderer : Component
Draws text at the entity's position.
- ctor()
- string Text { get; set; }
- Color Color { get; set; }
- Vec2 Offset { get; set; }
- override void OnRender(IRenderer renderer)


### Namespace SharpEngine.Graphics3D

#### static class App3D
Opens an OpenGL window and runs the game loop for a World.
- static void Run(string title, int width, int height, World world, Action<World> onGui = null, bool editor = true)

#### static class Assets
The project's Assets folder (textures, models). Paths are stored relative to the project, e.g. "Assets/Textures/brick.png".
- static string[] TextureExtensions
- static string[] ModelExtensions
- static string Folder { get; }
- static string Import(string sourceFile, string subfolder) - Copies a file into Assets/<subfolder> (unless it is already there) and returns its relative path.
- static string ImportBytes(byte[] data, string fileName, string subfolder) - Writes bytes (for example a texture embedded in an FBX) into Assets/<subfolder>.
- static bool IsModel(string file)
- static bool IsTexture(string file)
- static IReadOnlyList<string> List(bool textures, bool models) - All assets of the given kinds, as relative paths with '/' separators.
- static string Resolve(string path) - Full path of an asset path (relative paths are relative to the project root).

#### sealed class BuildConfig
Everything the Build Settings window edits. Saved as BuildSettings.json next to the game project.
- ctor()
- List<BuildScene> Scenes { get; set; }
- bool StreamWorld { get; set; }
- int StreamRadius { get; set; } - How many cells around the player stay loaded (1 = 3x3 chunks, 2 = 5x5).
- string ProductName { get; set; }
- int WindowWidth { get; set; }
- int WindowHeight { get; set; }
- bool StartFullscreen { get; set; }
- bool SelfContained { get; set; }
- BuildPlatform Platform { get; set; }
- WebEngine WebEngine { get; set; } - Only for WebGL builds: the real engine as WebAssembly, or the small JavaScript player.
- TargetPlatform Target { get; set; }
- CpuArchitecture Architecture { get; set; }
- bool DevelopmentBuild { get; set; }
- bool CopyPdbFiles { get; set; }
- bool CreateVisualStudioSolution { get; set; }
- CompressionMethod Compression { get; set; }
- string OutputFolder { get; set; } - Empty = Builds/<product>_<platform>_<arch> next to the solution.
- IEnumerable<BuildScene> EnabledScenes { get; }
- void FixArchitecture() - Keeps the architecture valid after the target platform changes.
- static BuildConfig FromJson(string json)
- static CpuArchitecture HostArchitecture()
- static TargetPlatform HostPlatform()
- static string RuntimeIdentifier(TargetPlatform target, CpuArchitecture arch) - .NET runtime identifier such as win-x64, or null for an unsupported combination.
- static CpuArchitecture[] SupportedArchitectures(TargetPlatform target) - CPU architectures for which Raylib ships a native library.
- string ToJson()

#### enum BuildPlatform
What kind of player to build.
Values: Standalone, WebGL

#### sealed class BuildScene
- ctor()
- string Path { get; set; } - Scene name relative to the Scenes folder, e.g. "Scenes/MainScene".
- bool Enabled { get; set; }

#### sealed class CameraComponent : Component
Marks a GameObject as a camera. The first enabled one in the world is used.
- ctor()
- float FieldOfView { get; set; } - Vertical field of view in degrees.

#### sealed class Chunk
One scene's place in the big world: a cell of the world grid.
- ctor()
- string Scene { get; set; }
- int X { get; set; }
- int Y { get; set; }

#### abstract class Component
Base class for behaviour attached to a GameObject (like Unity's MonoBehaviour).
- ctor()
- GameObject GameObject { get; }
- Transform Transform { get; }
- World World { get; }
- bool Enabled { get; set; }
- virtual bool RunInEditMode { get; } - When true the component also updates while the editor is in edit mode (not playing).
- virtual void OnDestroy()
- virtual void Start() - Runs once, before the first Update.
- virtual void Update(float dt) - Runs every frame. dt is seconds since the last frame.

#### sealed class ComponentInfo
- ctor()
- string Type { get; init; }
- IReadOnlyDictionary<string, string> Properties { get; init; } - Saved property values as text, e.g. Speed = 90.

#### enum CompressionMethod
Values: Default, Zip

#### enum CpuArchitecture
Values: X86, X64, Arm64

#### static class Debug
Unity-style logging for scripts: Debug.Log("hello"), Debug.LogWarning(x), Debug.LogError(x).
- static void Assert(bool condition, string message = "Assertion failed") - Logs an error when condition is false.
- static void Log(object message)
- static void Log(string tag, object message)
- static void LogError(object message)
- static void LogException(Exception ex)
- static void LogWarning(object message)

#### static class DebugLog
The debug log shown in the code editor ("Log" button): everything your scripts print with Debug or Console.WriteLine, compile results, and every error the engine caught. Thread safe; keeps the newest MaxEntries lines.
- const int MaxEntries
- static int Version { get; } - Increases with every change, so a window can tell when to redraw / scroll.
- static int Infos { get; }
- static int Warnings { get; }
- static int Errors { get; }
- static int Count { get; }
- static void Add(LogLevel level, string source, string text, string detail = null)
- static void CaptureConsole() - Copies everything written to Console.Out / Console.Error into the log (the text still reaches the real console).
- static void Clear()
- static List<LogEntry> Snapshot() - A copy of the entries, oldest first.

#### sealed class DirectionalLight
Scene lighting: one directional light (the "sun") plus a flat ambient term.
- ctor()
- Vector3 Direction { get; set; }
- Color Color { get; set; }
- float Intensity { get; set; }
- float Ambient { get; set; }

#### sealed class EditorCamera
The camera you look through while editing (Unity's "scene view"). It is separate from the game camera, which only takes over when you press Play, so you can fly anywhere to place objects without moving the game's camera. It is never saved into a scene.
- ctor()
- bool Enabled { get; set; } - False = look through the game camera also while editing ("Game View").
- bool Initialised { get; set; } - True once the camera has a position (it starts at the game camera and stays where you leave it).
- float FieldOfView { get; set; }
- float Speed { get; set; } - Fly speed in units per second (mouse wheel while the right button is held changes it).
- Vector3 Position { get; set; }
- float Yaw { get; set; }
- float Pitch { get; set; }
- Vector3 EulerAngles { get; }
- Vector3 Forward { get; }
- Vector3 Right { get; }
- Vector3 Up { get; }
- void Dolly(float amount) - Moves forward or back along the view direction (mouse wheel).
- void Focus(Vector3 center, float radius) - Flies so that a sphere around center fills the view (F key).
- void Look(float dx, float dy) - Turns the view: dx/dy in degrees (mouse movement x sensitivity).
- void Move(Vector3 local, float distance) - Moves relative to the view: x = right, y = up (world), z = forward.
- void Orbit(Vector3 pivot, float dx, float dy) - Turns around pivot while keeping its distance (Alt + left mouse).
- void Pan(float dx, float dy, float scale) - Slides the view sideways / up (middle mouse drag). dx/dy are mouse pixels.
- void Set(Vector3 position, Vector3 eulerAngles)

#### static class ErrorLog
One place for "something went wrong but the program should carry on": the error is remembered for the status bar, printed once, and appended to Logs/errors.log in the project folder so it can be looked at (or sent) later.
- static string Last { get; } - The most recent error, e.g. "update: NullReferenceException: ...".
- static int Count { get; }
- static string FilePath { get; }
- static event Action<string> Reported - Raised for every report (the editor shows it in its status bar).
- static void Install() - Writes crashes that cannot be caught (other threads, fatal errors) to the log before the program ends.
- static bool IsRecoverable(Exception ex) - Everything except running out of memory can be survived by skipping the current piece of work.
- static void Report(string where, Exception ex)
- static void Reset()
- static bool Try(string where, Action action) - Runs action; any exception is reported instead of thrown. Returns true when it ran without error.
- static Exception Unwrap(Exception ex)

#### sealed class FlyCamera : Component
Unity scene-view style camera: hold the RIGHT mouse button to look around, then W/A/S/D move, Q/E down/up, Shift = faster.
- ctor()
- float Speed { get; set; }
- float FastMultiplier { get; set; }
- float Sensitivity { get; set; } - Degrees per mouse pixel.
- override void OnDestroy()
- override void Update(float dt)

#### sealed class FolderItem : HierarchyItem
A folder in the hierarchy. Path is like "Town/Houses".
- ctor(string Path, string Name, int Depth, int ObjectCount) - A folder in the hierarchy. Path is like "Town/Houses".
- override Type EqualityContract { get; }
- string Path { get; init; }
- string Name { get; init; }
- int Depth { get; init; }
- int ObjectCount { get; init; }
- void Deconstruct(out string Path, out string Name, out int Depth, out int ObjectCount)
- override bool Equals(object obj)
- bool Equals(HierarchyItem other)
- bool Equals(FolderItem other)
- override int GetHashCode()
- override bool PrintMembers(StringBuilder builder)
- override string ToString()

#### sealed class GameObject
- string Name { get; set; }
- World World { get; }
- Transform Transform { get; }
- bool Active { get; set; }
- bool Destroyed { get; }
- string Folder { get; set; } - The hierarchy folder this object is in, like "Town/Houses". Empty = scene root. See Folders.
- string Chunk { get; } - Name of the streamed chunk (scene) this object came from, or null for objects of the main scene.
- bool IsScene { get; } - True for the one hidden object that holds a scene's own components ("pinned" to the scene itself, not to an object). It is never drawn, never picked, and runs its components while its scene is loaded.
- IReadOnlyList<Component> Components { get; }
- T AddComponent<T>()
- T AddComponent<T>(T component)
- Component AddComponent(Type type)
- void Destroy()
- T GetComponent<T>()
- void RemoveComponent(Component component)

#### static class GameProject
Finds where the game's project files and scenes live (in development) or where the player was installed.
- const string ConfigFileName - Marker file that exists next to a built player and not in the development output folder.
- static bool IsPlayer { get; } - True when running a built game (BuildSettings.json sits next to the executable).
- static string Root { get; } - Folder that holds Scenes/ and BuildSettings.json.
- static string ProjectDirectory { get; } - Directory of the game's .csproj, found by walking up from the build output.
- static string CsprojPath { get; }
- static string SolutionDirectory { get; } - Folder containing the .sln (or the project folder if there is none).
- static string ScenesDirectory { get; }
- static string ConfigPath { get; }
- static void OverrideRoot(string root, bool player) - For tests: pretend the game lives in a different folder.

#### static class GpuRenderer
Draws meshes on the graphics card: every mesh is uploaded once (split into 16-bit-index chunks), lit by a shader, and skipped when it is outside the camera's view. Millions of triangles are fine; the old per-vertex CPU path stays as a fallback when the shader cannot be created.
- static bool Enabled { get; set; } - Turn off to force the old CPU renderer.
- static bool Available { get; } - False until the shader compiled (needs an open window). Stays false when the graphics driver refuses it.
- static int TrianglesDrawn { get; }
- static int TrianglesCulled { get; }
- static int MeshesDrawn { get; }
- static int MeshesCulled { get; }
- static int MeshesOnGpu { get; }
- static long GpuTriangles { get; }
- static void BeginFrame(Camera3D cam, DirectionalLight sun, float aspect) - Sets up the frame: light, camera and the six frustum planes. Call inside BeginMode3D.
- static void Clear() - Drops every uploaded mesh (call before the window closes or when the graphics context is lost).
- static void EndFrame()

#### abstract class HierarchyItem
One row of the hierarchy tree: either an object or a folder. A closed hierarchy, so a switch over it is exhaustive in spirit (this is what a C# 15 union HierarchyItem(ObjectItem, FolderItem) expresses; plain records are used so the engine builds on every SDK).
- ctor(HierarchyItem original)
- virtual Type EqualityContract { get; }
- override bool Equals(object obj)
- virtual bool Equals(HierarchyItem other)
- override int GetHashCode()
- virtual bool PrintMembers(StringBuilder builder)
- override string ToString()

#### interface IFrameSink
Receives what a frame looks like instead of drawing it: the browser player turns this into WebGL calls. (The desktop player draws with Raylib directly in Render.)
- abstract void BeginFrame(Color skyTop, Color skyBottom, Vector3 cameraPosition, Vector3 cameraTarget, Vector3 cameraUp, float fieldOfView)
- abstract void EndFrame()
- abstract void Line(Vector3 from, Vector3 to, Color color)
- abstract void Mesh(string texture, float tileU, float tileV, ReadOnlySpan<Vector3> positions, ReadOnlySpan<Color> colors, Vector2[] uvs, int[] indices) - One lit mesh in world space. texture is empty when it has no texture.

#### interface IInputSource
Where keyboard and mouse input comes from. The desktop player uses Raylib; the browser player (SharpEngine.Web) uses DOM events.
- abstract Vector2 MouseDelta { get; }
- abstract void CaptureMouse(bool capture) - Hide and lock the cursor while looking around (right mouse button held).
- abstract bool IsKeyDown(KeyboardKey key)
- abstract bool IsKeyPressed(KeyboardKey key)
- abstract bool IsMouseDown(MouseButton button)
- abstract int PopKey() - Next key that was pressed since the last call (0 when none). Used so quick taps are never missed.

#### static class KeyEvents
Key presses that cannot be missed. Raylib's IsKeyPressed only compares the key state between two frames, so a quick tap that starts and ends between two frames is lost. The key queue records every press. App3D calls Poll once per frame.
- static void Poll()
- static bool WasPressed(KeyboardKey key)

#### sealed class LogEntry
One line of the debug log. Identical lines that follow each other are merged and counted.
- ctor()
- DateTime Time { get; init; }
- LogLevel Level { get; init; }
- string Source { get; init; }
- string Text { get; init; }
- string Detail { get; init; } - Stack trace or other extra lines (shown when the entry is clicked).
- int Count { get; }

#### static class LogExport
Turns the debug log and the compile problems into one plain-text report that reads well in any editor (Notepad, VS Code, a chat window): a summary, each compile problem with the offending source line, and the log with stack traces indented under their entry. Used by the Copy and Save buttons of the code editor's Log panel.
- static string Build(IReadOnlyList<LogEntry> entries = null, string title = null) - The report as text. entries defaults to the whole log.
- static string ExportAll() - Copies the whole report to the clipboard and saves it. Returns the saved path.
- static string ForClipboardOrFile(IReadOnlyList<LogEntry> entries = null, string title = null) - The report with the line endings of this operating system (so Windows Notepad shows proper lines).
- static string Save(IReadOnlyList<LogEntry> entries = null) - Writes the report to Logs/log-yyyyMMdd-HHmmss.txt and Logs/latest.txt (UTF-8 with BOM, so every Windows editor reads it correctly). Returns the path of the timestamped file.
- static void SaveLatestSoon() - Keeps Logs/latest.txt up to date after an error (at most once every 2 seconds, never throws), so the file is already there when you want to send the bug to somebody.

#### enum LogLevel
Values: Info, Warning, Error

#### sealed class MergeGroup
Scenes that can be joined into one bigger scene while the game runs. A scene belongs to at most one group. With Auto the whole group is merged as soon as the player is in any of its scenes.
- ctor()
- string Name { get; set; }
- List<string> Scenes { get; set; }
- bool Auto { get; set; }

#### sealed class MeshBuilder
Builds meshes incrementally.
- ctor()
- int VertexCount { get; } - Vertices added so far.
- int TriangleCount { get; } - Triangles added so far.
- void AddBox(Vector3 center, Vector3 size, Color? color = null) - Adds a box (6 flat faces, 12 triangles) centred on center.
- void AddFlatQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color? color = null, Vector3? outwardFrom = null) - Adds a flat quad (a,b,c,d in order around the edge).
- void AddFlatTriangle(Vector3 a, Vector3 b, Vector3 c, Color? ca = null, Color? cb = null, Color? cc = null, Vector3? outwardFrom = null) - Adds a flat-shaded triangle. If outwardFrom is given, the normal faces away from it.
- void AddGrid(float width, float depth, int cellsX, int cellsZ, Func<float, float, float> height, Func<float, Color> colorOf = null) - Adds a height field centred on the origin: cellsX x cellsZ squares (two triangles each) of the given total size. height(x, z) gives the height at a point; colorOf(height) the colour. Normals are smooth. 500 x 500 cells is half a million triangles.
- void AddSphere(Vector3 center, float radius, int segments = 24, Color? color = null) - Adds a smooth sphere. segments = slices around (stacks are half of it): 32 gives 1,984 triangles.
- void AddTriangle(int a, int b, int c)
- int AddVertex(Vector3 position, Vector3 normal, Color? color = null, Vector2? uv = null) - Adds a vertex. Without uv the texture coordinate is a box projection along the dominant normal axis.
- MeshData Build(bool withColors = true, string name = null)
- static Color Lerp(Color a, Color b, float t)

#### sealed class MeshChunk
One GPU-sized piece of a mesh: at most MaxVertices vertices (16-bit indices).
- ctor()
- int VertexCount
- float[] Positions
- float[] Normals
- float[] UVs
- byte[] Colors
- ushort[] Indices
- int TriangleCount { get; }

#### static class MeshChunker
Splits any MeshData (any number of triangles) into chunks the GPU can draw with 16-bit indices.
- const int MaxVertices
- static List<MeshChunk> Split(MeshData mesh, int maxVertices = 65535)

#### static class MeshCode
Turns an existing mesh into the source code of a MeshGenerator script.
- const int MaxLiteralTriangles - Meshes with more triangles than this become a parametric template instead of a list of vertices.
- static string FromMesh(MeshData mesh, string className, string objectName)
- static string Template(string className)

#### sealed class MeshData
CPU-side triangle mesh: positions, normals, optional per-vertex colours, triangle indices.
- ctor()
- Vector3[] Positions { get; init; }
- Vector3[] Normals { get; init; }
- Color[] Colors { get; init; }
- Vector2[] UVs { get; init; } - Texture coordinates (0..1, v pointing down like image rows). Null = none.
- int[] Indices { get; init; }
- string Name { get; init; } - Primitive name (e.g. "Cube") so scenes can be saved and re-created. Null for custom meshes.
- ValueTuple<Vector3, Vector3> Bounds { get; } - Local-space axis-aligned bounding box.
- int TriangleCount { get; }

#### abstract class MeshGenerator : Component
A component whose C# code creates the object's triangles. Derive from it in a script, override Generate and fill the MeshBuilder; the mesh is rebuilt when the script is compiled and whenever one of the script's public properties changes (in the editor too, so you see the result while you type values in the Inspector).
- ctor()
- override bool RunInEditMode { get; }
- int TriangleCount { get; } - Triangles produced by the last successful run.
- string LastError { get; } - The exception message of the last failed run (the previous mesh stays on screen). Null when it worked.
- double LastMilliseconds { get; } - Milliseconds the last run needed.
- abstract void Generate(MeshBuilder b) - Put the triangles into b (positions are in the object's local space).
- void Rebuild() - Forces a rebuild on the next frame.
- bool Regenerate() - Runs Generate now and gives the result to the object's MeshRenderer (added when missing).
- override void Start()
- override void Update(float dt)

#### sealed class MeshRenderer : Component
Draws a MeshData with the world's directional light (Blinn-Phong, computed per vertex).
- ctor()
- MeshData Mesh { get; set; }
- string ModelPath { get; set; } - Model file in the Assets folder (.fbx / .obj), e.g. "Assets/Models/robot.fbx". Replaces the built-in mesh.
- string ModelPart { get; set; } - Which mesh of the model file to draw (an FBX can hold many, each with its own texture). Empty = all of them merged. The Import button on a model with several meshes fills this in for you.
- static string LastModelError { get; } - Why the last ModelPath assignment failed (shown by the editor).
- string TexturePath { get; set; } - Image in the Assets folder, e.g. "Assets/Textures/brick.png". Empty = no texture.
- float TileU { get; set; } - How many times the texture repeats across the mesh.
- float TileV { get; set; }
- string PrimitiveName { get; set; } - Name of the built-in mesh (Cube, Sphere, ...). Setting it swaps the mesh.
- Color Tint { get; set; }
- bool Unlit { get; set; } - Skip lighting and draw the plain colour.
- bool DoubleSided { get; set; } - Light both sides of every triangle (useful for flat shapes such as the triangle).
- float Specular { get; set; }
- float Shininess { get; set; }

#### sealed class ModelData
A model file: all parts merged into Mesh, plus each part on its own in Parts.
- ctor()
- MeshData Mesh { get; init; }
- IReadOnlyList<ModelPart> Parts { get; init; } - The separate meshes of the file, each with its own texture. Never empty.
- string TextureFile { get; init; } - Full path of a texture file found next to the model.
- byte[] EmbeddedTexture { get; init; } - A texture stored inside the model file (FBX Video/Content).
- string EmbeddedTextureName { get; init; }

#### static class ModelLoader
Reads .fbx (binary) and .obj models into MeshData and imports them into the Assets folder.
- static List<string> TextureRoots { get; } - Extra folders searched for textures a model names but that are not next to it (set while importing a whole folder).
- static void ClearCache()
- static MeshData Combine(IReadOnlyList<MeshData> meshes) - Joins meshes into one (vertex data is copied, indices are shifted).
- static ValueTuple<string, string> Import(string sourceFile) - Copies a model into Assets/Models, plus its texture into Assets/Textures when one is found. Returns the relative model path and texture path (null when the model has none).
- static ValueTuple<string, IReadOnlyList<ImportedPart>> ImportParts(string sourceFile, string subfolder = "Models") - Like Import but keeps every mesh of the file separate: copies the model into Assets/Models and each part's texture into Assets/Textures. Textures are looked up next to the file and in TextureRoots.
- static MeshData Load(string assetPath, string part = "") - Loads (and caches) the mesh of a model asset path such as "Assets/Models/robot.fbx".
- static ModelData Read(string fullPath)

#### sealed class ModelPart
One mesh of a model file (an FBX geometry or an OBJ material group) with its own texture.
- ctor()
- string Name { get; init; } - Unique within the file; used by ModelPart.
- MeshData Mesh { get; init; }
- string TextureFile { get; init; }
- byte[] EmbeddedTexture { get; init; }
- string EmbeddedTextureName { get; init; }

#### sealed class ObjectInfo
- ctor()
- string Name { get; init; }
- string Scene { get; init; }
- Vector3 Position { get; init; }
- bool Active { get; init; }
- IReadOnlyList<ComponentInfo> Components { get; init; }

#### sealed class ObjectItem : HierarchyItem
An object in the hierarchy.
- ctor(GameObject Object) - An object in the hierarchy.
- override Type EqualityContract { get; }
- GameObject Object { get; init; }
- void Deconstruct(out GameObject Object)
- override bool Equals(object obj)
- bool Equals(HierarchyItem other)
- bool Equals(ObjectItem other)
- override int GetHashCode()
- override bool PrintMembers(StringBuilder builder)
- override string ToString()

#### static class Platform
Holds the active IInputSource. Defaults to Raylib.
- static IInputSource Input { get; set; }

#### static class PngWriter
Writes small RGB PNG files without any graphics library (used for the sample textures).
- static void Write(string path, int width, int height, Func<int, int, ValueTuple<byte, byte, byte>> pixel)

#### static class Primitives
Ready-made meshes (like Unity's GameObject > 3D Object menu). All are unit sized and centred on the origin.
- static string[] Names - Names accepted by TryCreate.
- static MeshData CheckerPlane(int tiles, Color a, Color b2) - 1x1 ground divided into tiles x tiles squares coloured like a checkerboard.
- static MeshData ColorTriangle() - A single flat triangle in the XY plane with red, green and blue corners (visible from both sides).
- static MeshData Cube() - 1x1x1 cube.
- static MeshData Cylinder(int segments = 32) - Cylinder, diameter 1, height 1.
- static MeshData Ground() - The default checkerboard floor (named so it can be saved).
- static MeshData Plane() - 1x1 quad in the XZ plane facing +Y.
- static MeshData Pyramid() - Four-sided pyramid, base 1x1, height 1.
- static MeshData Sphere(int segments = 32, int rings = 16) - Smooth UV sphere, diameter 1.
- static bool TryCreate(string name, out MeshData mesh)

#### sealed class SceneFolders
The folders and sub-folders that organise a scene's objects. Folders only exist for organising: they have no transform. Paths use '/' ("Town/Houses"). An object sits in the folder named by Folder ("" = scene root).
- IReadOnlyList<string> All { get; } - Every folder path (including parents of used folders), sorted.
- string Create(string path) - Creates a folder (and its parents). Returns the cleaned path.
- void Delete(string path, bool destroyObjects = false) - Deletes a folder. Its objects (and sub-folders) move up to the parent folder, or are destroyed when destroyObjects.
- bool Exists(string path)
- static bool IsInside(string path, string folder)
- string Move(string from, string to) - Moves/renames a folder (with everything inside it) to a new path. Returns the new path.
- static string NameOf(string path)
- static string Normalize(string path) - Cleans a path: '/' separators, no empty parts, no surrounding spaces.
- IEnumerable<GameObject> Objects(string folder, bool recursive = true) - Objects in a folder; with recursive also those in its sub-folders.
- static string ParentOf(string path)
- void Put(GameObject go, string folder) - Puts an object in a folder (created when missing). "" = scene root.
- string Rename(string path, string newName)
- void SetActive(string folder, bool active) - Switches a folder's objects on or off.
- List<ValueTuple<HierarchyItem, int>> Tree(Func<string, bool> isCollapsed) - The tree as a flat list in drawing order: folders first, then objects, children indented by Depth. Collapsed folders hide their contents.
- bool TryGetBounds(string folder, out Vector3 min, out Vector3 max) - The box around everything in a folder (and its sub-folders). False when it is empty.
- string UniquePath(string parent, string baseName = "Folder") - A name like "Folder", "Folder 2" that is free inside parent.

#### sealed class SceneInfo
- ctor()
- string Name { get; init; }
- int CellX { get; init; }
- int CellY { get; init; }
- bool OnMap { get; init; }
- float SizeX { get; init; }
- float SizeZ { get; init; }
- string Group { get; init; }
- IReadOnlyList<ObjectInfo> Objects { get; init; }

#### static class SceneManager
Scenes are JSON files in the game's Scenes folder. The Build Settings list decides which ones ship in the player and in what order (build index 0 is the first scene). Game code and ScenePortal call LoadScene to move the player between scenes.
- static BuildConfig Config { get; }
- static string CurrentName { get; set; } - Name of the scene currently in the world (no folder, no extension).
- static string ScenesDirectory { get; }
- static string DeletedDirectory { get; } - Where deleted scene files are kept so a mistake can be undone.
- static IReadOnlyList<string> BuildScenes { get; } - Scenes that will be part of the build, in order (the index is the build index).
- static int CurrentBuildIndex { get; }
- static IReadOnlyCollection<string> MergedScenes { get; } - Scenes merged into the open one right now (empty when not merged).
- static event Action<string> SceneLoaded - Raised after a scene was loaded into the world.
- static IReadOnlyList<string> AvailableScenes()
- static string BuildPathFor(string name)
- static void CreateEmpty(World world, string name) - A world with just a Main Camera, ready to start a new scene.
- static string CreateSceneFile(string baseName = "NewScene") - Writes a new scene file (camera + ground) without touching the open scene. Returns its name.
- static bool Delete(string pathOrName, out string reason) - Deletes a scene: the file moves to DeletedDirectory (copy it back to Scenes/ to undo), and the scene is taken out of the build list, the world map and its merge group. The scene that is open now cannot be deleted. Returns false (with a reason) when it was not deleted.
- static int DeleteMany(IEnumerable<string> names, out List<string> problems) - Deletes several scenes. Returns how many were deleted; problems lists the ones that were not.
- static bool Exists(string pathOrName)
- static string FileFor(string pathOrName)
- static void HandleHotkeys() - Hotkeys for the player: Tab / Shift+Tab step through the build, 1-9 jump to a build index.
- static bool LoadFirst(World world) - Starts the player: loads build index 0.
- static void LoadNext()
- static void LoadPrevious()
- static void LoadScene(string nameOrPath) - Switches to a scene by name at the end of the current frame. Safe to call from components.
- static void LoadScene(int buildIndex) - Switches to a scene by its Build Settings index.
- static void Merge(string[] scenesOrGroups) - Joins other scenes (or a merge group of the World Map, by name) to the open scene at the end of the frame: they are loaded at their map positions and stay loaded, so the open scene and these become one bigger scene. Works while playing. Safe to call from components.
- static string NameOf(string pathOrName) - "Scenes/Main", "Main.json" and "Main" all become "Main".
- static void Open(World world, string pathOrName) - Loads a scene file into the world right now (use LoadScene from game code).
- static void Process(World world) - Applies a requested scene change. Call once per frame outside of component updates.
- static void Reset() - Forget the cached config (tests).
- static string SanitizeName(string name) - Letters, digits, space, '-' and '_' only. Returns null if nothing usable is left.
- static void Save(World world, string name = null)
- static void SaveConfig()
- static void ToggleMerge(string sceneOrGroup) - Merges when not merged, splits when merged.
- static string UniqueName(string baseName)
- static void Unmerge() - Splits the merged scenes again (the bigger scene shrinks back to the open scene).

#### sealed class ScenePortal : Component
Doorway to another scene. The camera is the player. With RequireInteract (default) the player walks up to it and presses the interact key (E); without it the portal fires as soon as the camera is close. It ignores the first moments after a scene loads so you don't bounce straight back.
- ctor()
- string TargetScene { get; set; } - Scene name or build path to load. For the merge modes: a scene or a merge group of the World Map.
- string Mode { get; set; } - What the portal does: "Load" (go to the scene), "Merge" (join the scene or group to this one so they become one bigger scene), "Unmerge" (split everything again) or "Toggle" (merge, and the next use splits).
- float Radius { get; set; } - How close the camera must be, in world units.
- bool RequireInteract { get; set; } - True: press the interact key while inside the radius. False: enter automatically.
- string InteractKey { get; set; } - Key name from Raylib's KeyboardKey list ("E", "F", "Enter", "Space", ...).
- string Prompt { get; set; } - Text shown on screen while in range. Empty = "Press E to enter <scene>".
- bool ShowDebug { get; set; } - Draw the debug rings and label in the editor.
- float LastDistance { get; } - Distance to the camera measured on the last update (-1 = unknown). Used by the debug label.
- bool InRange { get; } - True while the camera is inside the radius.
- static string ActivePrompt { get; } - Prompt of the nearest portal that is in range and waiting for the key (drawn by App3D).
- static KeyboardKey? ReservedKey { get; } - The key of the prompt that was showing last frame. Other code (the fly camera) must not use it for its own purpose while a portal is waiting for it, otherwise pressing E to enter a portal would also fly the camera up.
- KeyboardKey Key { get; }
- string PromptText { get; }
- static Func<KeyboardKey, bool> InteractPressed { get; set; } - Checks whether the interact key was pressed this frame (tests replace it).
- static void ResetPrompt()
- override void Update(float dt)

#### enum TargetPlatform
Values: Windows, MacOS, Linux

#### static class TextureCache
Loads image files as GPU textures once and keeps them (needs the window to be open).
- static string LastError { get; } - Last problem (missing file, ...) so the editor can show it.
- static void Clear() - Forget everything so changed files are loaded again.
- static void Refresh() - Reloads textures whose file changed on disk (cheap; call now and then).
- static bool TryGet(string path, out Texture2D texture)

#### sealed class Transform
Position, rotation and scale of a GameObject (right-handed, Y up, forward = -Z). EulerAngles are degrees: X = pitch, Y = yaw, Z = roll.
- ctor()
- Vector3 Position { get; set; }
- Vector3 EulerAngles { get; set; }
- Vector3 Scale { get; set; }
- Quaternion Rotation { get; }
- Vector3 Forward { get; }
- Vector3 Right { get; }
- Vector3 Up { get; }
- Matrix4x4 WorldMatrix { get; } - Local-to-world matrix (System.Numerics row-vector convention).
- void LookAt(Vector3 target) - Turns so that Forward points at the target (roll is reset to 0).
- void Rotate(Vector3 eulerDeltaDegrees)
- void Translate(Vector3 delta)

#### enum WebEngine
Which player a WebGL build contains.
Values: RealEngine, Lite

#### sealed class World
The scene: owns all GameObjects, the light and the sky, and draws everything with OpenGL.
- ctor()
- const float MinSize
- const float MaxSize
- IReadOnlyList<GameObject> Objects { get; }
- DirectionalLight Sun { get; }
- Color SkyTop { get; set; }
- Color SkyBottom { get; set; }
- bool ShowGrid { get; set; }
- bool ShowAxes { get; set; }
- bool ShowChunkDebug { get; set; } - Draw chunk borders, names and the loaded area (F3 in the app).
- float SizeX { get; set; } - Width (X) of this scene's world in units. The scene is a rectangle centred on the origin.
- float SizeZ { get; set; } - Depth (Z) of this scene's world in units.
- bool ShowBounds { get; set; } - Draw the scene boundary in the editor.
- bool ClampCamera { get; set; } - While playing, keep the camera inside the scene boundary.
- EditorCamera EditorView { get; } - The camera used while editing. The game camera takes over in play mode.
- bool UsesEditorView { get; } - True while the editor camera is the one that draws (edit mode with Scene View on).
- float Time { get; } - Seconds since the world started updating.
- int TrianglesDrawn { get; }
- int TrianglesCulled { get; } - Triangles skipped this frame because they were outside the camera's view (graphics-card renderer only).
- bool PlayMode { get; set; } - When false (editor edit mode) only components with RunInEditMode update, and Time stands still.
- IReadOnlyList<WorldScript> WorldScripts { get; } - The running world scripts (empty when not playing).
- WorldStreamer Streamer { get; } - Loads the neighbouring scenes of the World Map around the player (see WorldStreamer).
- SceneFolders Folders { get; } - The folders and sub-folders that organise this scene's objects.
- event Action<World, Camera3D> Render3D - Raised inside the 3D pass after the meshes are drawn (used by the editor for gizmos).
- void Clear() - Removes every GameObject immediately and resets the clock.
- bool Collect(IFrameSink sink) - Describes the frame to a IFrameSink instead of drawing it with Raylib (the browser player uses this). Returns false when there is no camera.
- GameObject CreateMesh(string name, MeshData mesh, Color tint, Vector3 position = null) - Shortcut: a GameObject with a MeshRenderer already attached.
- GameObject CreatePrimitive(string primitive, Color tint, Vector3 position = null) - Creates a GameObject with a built-in mesh ("Cube", "Sphere", ...).
- void Destroy(GameObject go)
- void EnsureEditorView() - Gives the editor camera a starting place: the game camera's, or a spot above the origin.
- GameObject Find(string name)
- GameObject FindSceneObject() - The object that holds this scene's own components, or null when none was created yet.
- void Flush() - Applies pending spawns and destroys now (normally done by Update).
- GameObject GetSceneObject() - The scene's own component holder (created on first use). Pin components to the scene by adding them here.
- GameObject Instantiate(string name, Vector3 position = null)
- GameObject Raycast(Ray ray, out float distance) - Nearest active object whose bounding box is hit by the ray (objects without a mesh use a small box).
- void Render() - Draws sky, grid and meshes. Call between BeginDrawing and EndDrawing.
- bool TryGetCamera(out Camera3D camera) - Camera of the first active GameObject that has an enabled CameraComponent.
- void Update(float dt)
- static ValueTuple<Vector3, Vector3> WorldBounds(GameObject go) - World-space bounding box of an object (slightly padded so flat meshes can be hit).

#### static class WorldIndex
A read-only catalogue of the whole project: every scene with its objects, their components and saved values, read from the scene files (so it also knows scenes that are not loaded). The code editor's completion and "Objects" panel use it, and world scripts can ask it questions such as "which scene holds the door?".
- static IReadOnlyList<SceneInfo> Scenes { get; } - All scenes of the project, in name order. Reloaded when a scene file or the World Map changed.
- static IEnumerable<ObjectInfo> Objects { get; }
- static IReadOnlyList<ObjectInfo> Find(string objectName) - Every object with this name, in any scene.
- static void Refresh()
- static void Reset() - Forget the cache (tests, a different project).
- static SceneInfo Scene(string name)
- static IReadOnlyList<ObjectInfo> WithComponent(string componentType) - Every object that has a component of this type, in any scene.

#### static class WorldLayout
The world is a grid of chunks and every chunk is one scene (its own editable world with its own size). Adding a scene adds a chunk, so the world grows as the game grows. Saved as WorldMap.json in the project.
- const string FileName
- static string FilePath { get; }
- static IReadOnlyList<Chunk> Chunks { get; }
- static IReadOnlyList<MergeGroup> Groups { get; } - Merge groups (see MergeGroup).
- static Chunk At(int x, int y)
- static float CellSize(Func<string, ValueTuple<float, float>> sizeOf = null) - Edge length of one grid cell: the biggest scene decides, so every scene fits in its cell.
- static Chunk Ensure(string scene) - Gives a scene a chunk if it has none. Called whenever a scene is saved or created.
- static ValueTuple<int, int, int, int> Extent() - Cell range that contains every chunk (inclusive). An empty world is one cell.
- static MergeGroup GroupNamed(string name)
- static MergeGroup GroupOf(string scene) - The group a scene belongs to, or null.
- static Chunk Of(string scene)
- static void Place(string scene, int x, int y) - Puts a scene on a specific cell (creating its chunk if needed). An occupied cell swaps places.
- static void Remove(string scene) - Takes a scene out of the world grid and out of its merge group (the scene file itself is not touched).
- static void Reset() - Forget everything (a different project folder, or tests).
- static IReadOnlyList<string> ResolveMerge(string nameOrGroup) - Scenes a merge request stands for: the members of a group of that name, or the scene itself.
- static void Save()
- static void SetGroupAuto(string groupName, bool auto)
- static void SetGroupOf(string scene, string groupName) - Puts a scene into a group (created on first use). An empty name takes the scene out of its group.
- static void SetSizeInFile(string scene, float x, float z) - Edits the size stored in a scene file (for a scene that is not open).
- static ValueTuple<float, float> SizeOf(string scene) - Width and depth of a scene, read from its file (the open scene should be asked of the World instead).
- static bool Sync() - Makes the layout match the Scenes folder: every scene file has a chunk (new ones go to the next free cell), chunks of deleted files disappear. Returns true when something changed.
- static ValueTuple<float, float> WorldSize(Func<string, ValueTuple<float, float>> sizeOf = null) - Total size of the world in units (all chunks together).
- static void WriteSubset(string path, IEnumerable<string> scenes) - Writes a WorldMap.json that only holds the given scenes (what a build ships).

#### abstract class WorldScript
Code that belongs to the whole world instead of one object or scene. One instance of every WorldScript class is created when the game starts playing and lives until it stops, across scene changes, merges and streaming. Write them in the World Map's code editor ("World Code"). The editor's completion list knows every scene, object and component of the project.
- ctor()
- World World { get; } - The world (scene) the game is running in.
- bool Enabled { get; set; } - Turn the script off (it stops getting Update calls). Scripts that throw are switched off automatically.
- string CurrentScene { get; } - Name of the scene that is open now.
- IReadOnlyCollection<string> MergedScenes { get; } - Scenes merged into the open scene right now.
- static IReadOnlyList<SceneInfo> Scenes { get; } - Everything the project contains: every scene, object and component, also of scenes that are not loaded.
- GameObject Find(string name) - An object of the open scene (or of a streamed or merged scene) by name.
- void LoadScene(string scene)
- void Merge(string[] scenesOrGroups)
- virtual void OnMergeChanged(IReadOnlyCollection<string> merged) - Scenes were merged into, or split from, the open scene. merged lists what is merged now.
- virtual void OnSceneLoaded(string scene) - A scene was opened (also once when play starts, for the first scene).
- virtual void OnStop() - Play stops.
- virtual void Start() - Runs once when play starts.
- void Unmerge()
- virtual void Update(float dt) - Runs every frame while playing. dt is seconds since the last frame.

#### static class WorldScriptRegistry
Finds the WorldScript classes of the program (built in and compiled from Scripts/).
- static IReadOnlyList<Type> Types { get; }

#### sealed class WorldStreamer
Turns the World Map into one continuous world while playing.  The scene you opened is the anchor: it stays where it is. Every other scene is a chunk on the World Map grid, and while the player is near it the streamer loads that scene's objects into the same world, moved to the chunk's place on the grid (cell offset x cell size). Chunks that the player has left behind are unloaded again.  Rules: - Cell size is the biggest scene on the map (CellSize), the same size the World Map draws. - Cells within Radius of the player's cell are loaded (1 = 3x3). A loaded chunk is only unloaded when it is more than Radius + 1 cells away, so walking back and forth over a border never makes a chunk flicker. - Scene files are read and parsed on a background thread; the objects are then created a few per frame so a load never stalls. - Cameras of neighbouring scenes are ignored, the player keeps the camera of the main scene. - While a chunk's neighbours are streamed the camera is kept inside the whole map instead of inside the main scene, and sky, sun colour, sun strength and ambient light blend smoothly to those of the chunk the player stands in. - Saving a scene never writes streamed objects into it: they belong to their own scene file.
- ctor(World world)
- bool Enabled { get; set; } - Turn streaming on or off (the Build Settings value is applied by App3D).
- bool Failed { get; set; } - Set when streaming threw an error: it stays off until the next scene load or Play so one bad chunk cannot stop the game.
- int Radius { get; set; } - Cells kept loaded around the player. 1 = 3x3 chunks, 2 = 5x5.
- IReadOnlyCollection<string> MergedScenes { get; } - Scenes that are merged into the open one right now. They are loaded wherever the player is and are never unloaded, so together with the open scene they act as one bigger scene. Temporary: it ends with Unmerge, when another scene is opened or when play stops.
- bool Background { get; set; } - Parse scene files on a background thread (turn off for tests).
- int ObjectsPerFrame { get; set; } - How many objects of a loading chunk are created per frame.
- float BlendSpeed { get; set; } - Seconds-to-1/e for the sky and light blend between chunks.
- bool Active { get; } - True while the streamer controls the world (enabled, playing, and the open scene is on the map).
- string AnchorScene { get; }
- string CurrentChunk { get; } - Scene the player is standing in, or null when not streaming.
- float CellSize { get; }
- int PendingCount { get; }
- IEnumerable<string> LoadedScenes { get; }
- ValueTuple<int, int, int> DebugPlayer { get; } - Player cell and loaded radius for the debug view (radius -1 when not streaming).
- float DebugCellSize { get; }
- void Begin() - Starts streaming around the scene that is open now. Called automatically on the first update.
- ValueTuple<int, int> CellAt(Vector3 p) - Map cell that contains a world position.
- Vector3 CellCenter(int x, int y) - World position of the middle of a map cell.
- static Color DebugColor(string state, bool here)
- IReadOnlyList<DebugChunk> Describe() - Every chunk around the open scene with its state: main (the open scene), here (the player stands in it), loaded, merged (joined into the open scene), loading, far (streamed scene that is not loaded right now) or map (only shown, not playing).
- void DrawDebug() - Draws chunk borders in the 3D view (call inside BeginMode3D).
- void DrawDebugOverlay() - Names and a status line on screen (call after the 3D pass).
- void FinishLoading() - Blocks until every queued chunk is in the world.
- bool IsMerged(string scene)
- int Merge(string[] namesOrGroups) - Merges scenes (or a merge group by name) into the open scene. Returns how many scenes joined.
- ValueTuple<int, int, int, int>? MergedExtent() - Total size in cells of the open scene plus the merged scenes (what the bigger scene covers).
- void Reset() - Drops all loaded chunks (called when the world is cleared or play stops).
- void Unmerge() - Splits everything that was merged. Chunks that are too far away unload; with streaming off the camera returns to the open scene.
- void Update(float dt)


### Namespace SharpEngine.Graphics3D.Editor

#### static class AdminEditor
Static in-game admin editor (like a mini Unity Editor): toolbar, hierarchy, inspector, scene picking, move gizmo, play/stop with restore, and save/load to scene.json. Call Attach once, then Update and DrawGui every frame (App3D.Run does this for you).
- static bool Visible { get; set; }
- static GameObject Selected { get; set; } - The selected object. Selecting an object clears the selected folder.
- static string SelectedFolder { get; set; } - The folder selected in the hierarchy (its path), or null. A folder can be focused with F like an object.
- static bool MouseOverUi { get; }
- static bool ModalOpen { get; } - True while a dialog covers the editor.
- static bool SnapToGrid { get; set; } - Snap to a grid while moving things: the Snap button, or hold Ctrl.
- static float SnapStep { get; set; }
- static void Attach(World world) - Hooks the editor into a world. Starts in edit mode (scripts paused).
- static void BackupScene(World world, string reason) - Writes a copy of the open scene to Backups/ (the newest ten are kept). Never throws.
- static void DrawGui(World world)
- static void Update(World world)

#### static class BuildPipeline
Builds a standalone player: dotnet publish for the chosen platform, then the enabled scenes and BuildSettings.json next to it. Runs on a background thread; the UI polls IsBuilding and Status.
- static bool IsBuilding { get; }
- static string Status { get; }
- static bool StatusIsError { get; }
- static string LastOutputDirectory { get; }
- static Func<PublishRequest, Action<string>, int> Publisher { get; set; } - Compiles the game. Returns the process exit code (0 = success). The default runs dotnet publish; tests replace it.
- static Func<string, string> OpenInBrowser { get; set; } - Starts the default browser on the page (tests replace this).
- static Func<string, string, string, Action<string>, int> WasmPublisher { get; set; } - Runs "dotnet publish" for the web project. Returns the exit code. Tests replace it.
- static string WebProjectOverride { get; set; } - Set to use a SharpEngine.Web.csproj from a different place (tests).
- static string FindWebProject() - Finds src/SharpEngine.Web/SharpEngine.Web.csproj next to the game (searching upward from the project and solution folders).
- static string ResolveOutputDirectory(BuildConfig cfg)
- static string SafeFileName(string name)
- static string ServeLocally(string folder) - Serves a folder on http://localhost:<port>/ (replaces an earlier server). Returns the address.
- static bool Start(bool runAfter) - Starts a build on a background thread. Returns false (and sets a message) if it cannot start.

#### static class BuildSettingsWindow
The "Build Settings" dialog: scenes in the build, target platform, options, and the Build / Build And Run buttons. Drawn by AdminEditor.
- static bool IsOpen { get; }
- static void Close()
- static void Draw(World world)
- static void Open()

#### sealed class BuiltInTools : EditorScript
The editor's own optional tools. They are registered through EditorApi exactly like a script's tools, which shows what the API can do (and proves it works).
- ctor()
- override void OnRegister(EditorApi editor)

#### static class CodeEditorWindow
The in-game code editor. Lists the project's Scripts/*.cs files, edits them with syntax colours, and compiles them (Ctrl+S) so the classes show up as components you can add to an object or pin to a scene.
- static bool IsOpen { get; }
- static Func<ValueTuple<string, bool>> Compile { get; set; } - Set by the editor: saves, compiles and applies the scripts. Returns a message and whether it worked.
- static void Close()
- static void Draw(World world)
- static void Open(string file = null)
- static void OpenWorld() - Opens Scripts/World/WorldCode.cs (created from a template the first time): code for the whole world.

#### static class CompletionEngine
Suggestions for the code editor. It knows the whole engine (every public class, property and method, found by reflection) and the whole project (every scene, object, component and merge group, from WorldIndex): - after a dot: the members of whatever the expression before it is, e.g. World.Find("Door").Transform. - inside quotes of Find("..."), LoadScene("..."), Merge("...") and friends: object, scene and group names - inside GetComponent<...>: component types - otherwise: variables of the file, members of the class it derives from, engine types, keywords
- static IReadOnlyDictionary<string, Type> Types { get; } - Every type that scripts can use by its short name (engine, Raylib colours and keys, System, collections, numerics).
- static CompletionResult Complete(string text, int line, int col, bool explicitRequest) - Suggestions for the caret position, or null when nothing fits. explicitRequest is true for Ctrl+Space.
- static string Friendly(Type t)
- static bool InsideString(string before) - True when the caret (after before, the text of the line up to it) is inside a string literal.
- static Type ResolveTypeName(string name) - "GameObject", "float", "List<Vector3>", "int[]", "Dictionary<string, GameObject>?" to a Type.

#### sealed class CompletionItem
- ctor(string Text, string Insert, CompletionKind Kind, string Detail)
- string Text { get; init; }
- string Insert { get; init; }
- CompletionKind Kind { get; init; }
- string Detail { get; init; }
- void Deconstruct(out string Text, out string Insert, out CompletionKind Kind, out string Detail)
- override bool Equals(object obj)
- bool Equals(CompletionItem other)
- override int GetHashCode()
- override string ToString()

#### enum CompletionKind
Values: Method, Property, Field, Type, Keyword, Variable, Object, Scene, Group, Component

#### sealed class CompletionResult
- ctor()
- int ReplaceStart { get; init; } - Column where the text that gets replaced starts.
- string Prefix { get; init; }
- bool InString { get; init; }
- List<CompletionItem> Items { get; init; }

#### static class ComponentRegistry
Finds every Component type in the program and the properties the editor can show and save. Supported property types: float, int, bool, string, Vector3, Color.
- static IReadOnlyList<Type> Types { get; } - All concrete components with a public parameterless constructor (built in and from Scripts), sorted by name.
- static Type Find(string name)
- static bool IsSupported(Type t)
- static string Pretty(string name) - "DegreesPerSecond" becomes "Degrees Per Second".
- static PropertyInfo[] Properties(Type type) - Public read/write properties of a supported type (base-class ones such as Enabled first).

#### static class CSharpHighlighter
Cheap C# syntax colouring: one pass per line, remembering whether a /* comment is still open at the end of it.
- static bool Classify(string line, bool inBlockComment, Token[] kinds) - Fills kinds (one per character of the line). Returns the open-block-comment state for the next line.

#### sealed class EditorApi
What an EditorScript uses to add things to the editor.
- World World { get; }
- GameObject Selected { get; } - The selected object (null when none or a folder is selected).
- string SelectedFolder { get; } - The selected hierarchy folder (like "Town/Houses") or null.
- MenuCommand AddCommand(string name, Action<World> run) - An entry in the toolbar's Tools menu.
- ObjectInspector AddObjectInspector(string title, Action<PanelBuilder, GameObject> draw) - A section in the Inspector for every selected object.
- ObjectInspector AddObjectInspector<T>(string title, Action<PanelBuilder, GameObject> draw) - A section in the Inspector for every selected object.
- PanelItem AddPanel(string title, Action<PanelBuilder> draw) - A section at the bottom of the Inspector. Its draw runs every frame, so it can show live values.
- ShortcutItem AddShortcut(KeyboardKey key, Action<World> run, bool ctrl = false, bool shift = false, string name = "") - A keyboard shortcut.
- ToolbarButton AddToolbarButton(string label, Action<World> run) - A button in the toolbar.
- void FocusSelection() - Flies the editor view to the selection (same as pressing F).
- void Say(string message) - Shows a message in the editor's status bar.
- void Select(GameObject go)

#### static class EditorExtensions
The registry the admin editor draws from.
- static IReadOnlyList<EditorItem> Items { get; }
- static IReadOnlyList<EditorScript> Scripts { get; }
- static string Describe(EditorItem item) - One exhaustive switch over every kind of item: what the editor shows for each.
- static void Reload() - Forget everything and register all tools again on the next frame.
- static IReadOnlyList<Type> ScriptTypes() - Every EditorScript class: the engine's own and the ones compiled from Scripts/.
- static void Update(World world, float dt) - Once per frame: (re)registers tools after a script reload and lets them update.

#### abstract class EditorItem
One thing a tool adds to the editor. A closed set: ToolbarButton, MenuCommand, PanelItem, ObjectInspector or ShortcutItem. In C# 15 this is exactly union EditorItem(ToolbarButton, MenuCommand, PanelItem, ObjectInspector, ShortcutItem); the engine uses a closed record hierarchy so it builds on every SDK, and the editor handles every case with one exhaustive switch.
- ctor(EditorItem original)
- virtual Type EqualityContract { get; }
- string Owner { get; } - The tool that registered it (class name), for the error log.
- bool Faulted { get; } - Set when its code threw; it is skipped until the scripts are reloaded.
- override bool Equals(object obj)
- virtual bool Equals(EditorItem other)
- override int GetHashCode()
- virtual bool PrintMembers(StringBuilder builder)
- override string ToString()

#### abstract class EditorScript
Base class of editor tools. Write them in Scripts/Editor (Code editor: Create Editor Script). One instance is created when the editor starts and again every time the scripts are recompiled; register what you need in OnRegister.
- ctor()
- World World { get; } - The scene being edited.
- bool Enabled { get; set; } - Turn the tool off (no more OnUpdate calls). A tool that throws is switched off automatically.
- string CurrentScene { get; } - Name of the open scene.
- virtual void OnRegister(EditorApi editor) - Called once after the tool was created. Add buttons, commands, panels and shortcuts to editor.
- virtual void OnSceneLoaded(string scene) - A scene was opened.
- virtual void OnSelectionChanged(GameObject selected) - The selected object changed (null = nothing selected).
- virtual void OnUpdate(float dt) - Every frame while the editor runs (edit mode and play mode).

#### static class FileBrowserWindow
The in-editor file system: browse the computer's folders and import models (.fbx .obj), textures and whole directories into the scene without dragging. Open with the toolbar's Import... button or Ctrl+I.
- static bool IsOpen { get; }
- static void Close()
- static void Draw(World world)
- static void Open() - Opens the browser (at the last folder, or the Assets folder the first time).

#### static class Gui
Tiny immediate-mode UI drawn with Raylib: panels, buttons, checkboxes, drag-or-type number fields, text fields, vector/colour fields and scroll areas. Call BeginFrame once per frame.
- static Color PanelBg
- static Color PanelHeader
- static Color Border
- static Color FieldBg
- static Color ButtonBg
- static Color Hover
- static Color Accent
- static Color TextCol
- static Color TextDim
- const int FontSize
- const float Row
- const float MinScale
- const float MaxScale
- static float Scale { get; set; } - User-chosen interface scale (1 = 100%). Applied between BeginUi and EndUi.
- static float EffectiveScale { get; } - The scale actually used this frame (large dialogs may need to shrink it so they fit).
- static float ScreenWidth { get; } - Window size in interface units (pixels / scale). Lay panels out with these.
- static float ScreenHeight { get; }
- static Vector2 RawMouse { get; } - Mouse position in window pixels (use this for picking in the 3D view).
- static Vector2 Mouse { get; } - Mouse position in interface units.
- static bool LeftPressed { get; }
- static bool LeftDown { get; }
- static bool LeftReleased { get; }
- static bool Disabled { get; set; } - When true every widget ignores the mouse (used for the panels behind a modal dialog).
- static bool KeyboardCaptured { get; set; } - Set by dialogs so Esc closes them instead of quitting the game.
- static bool WantsKeyboard { get; } - True while a text field is being typed into, a list is open, or a dialog owns the keyboard.
- static bool Busy { get; } - True while a widget is being dragged or edited (so the 3D view should ignore the mouse).
- static bool PopupOpen { get; } - True while a drop-down list is open.
- static bool IsEditing { get; } - True while text is being typed into a field.
- static bool IsDragging { get; } - True while a slider or other widget has the mouse captured.
- static void BeginFrame()
- static void BeginScroll(Rectangle view, ref float scroll, float contentHeight) - Starts a clipped, mouse-wheel scrollable area. Draw content starting at (view.X, view.Y - scroll) and pass the content height used on the previous frame.
- static void BeginUi() - Starts drawing in interface units (scaled by EffectiveScale).
- static bool Button(Rectangle r, string text, bool selected = false, bool enabled = true)
- static bool Checkbox(Rectangle r, string key, string label, ref bool value, bool enabled = true, bool tick = false)
- static bool ColorField(Rectangle r, string key, ref Color c)
- static bool Dropdown(Rectangle r, string key, string[] options, ref int index, bool enabled = true) - Drop-down list. The new value is applied one frame after the pick.
- static void EndFrame() - Draws the open list above everything else. Call once at the end of the UI frame.
- static void EndScroll(Rectangle view)
- static void EndUi()
- static string FitText(string s, float maxWidth) - Shortens text (with the end cut off) so it is at most maxWidth wide.
- static bool FloatField(Rectangle r, string key, ref float value, float speed = 0.05, bool integer = false)
- static bool IsHot(Rectangle r) - True when the mouse is over the rectangle and nothing (dialog, open list, scroll clip) blocks it.
- static void LoadPrefs()
- static int MeasureSized(string text, int size)
- static int Menu(Rectangle r, string key, string text, string[] items) - A button that opens a list of commands. Returns the chosen item index (one frame later), or -1.
- static void Panel(Rectangle r, string title)
- static void SavePrefs()
- static void SetScope(int scope) - Widgets in different scopes can reuse the same key (e.g. one scope per component).
- static bool Slider(Rectangle r, string key, ref float value, float min, float max) - Horizontal slider. Returns true on the frame the value changes.
- static void Text(float x, float y, string text, Color color)
- static void TextCentered(Rectangle r, string text, Color color)
- static bool TextField(Rectangle r, string key, ref string value, string placeholder = "")
- static void TextLeft(Rectangle r, string text, Color color, float pad = 6)
- static void TextSized(float x, float y, string text, int size, Color color, bool bold = false)
- static bool Vec3Field(Rectangle r, string key, ref Vector3 v, float speed = 0.05)

#### sealed class MenuCommand : EditorItem
An entry in the toolbar's Tools menu.
- ctor(string Name, Action<World> Run) - An entry in the toolbar's Tools menu.
- override Type EqualityContract { get; }
- string Name { get; init; }
- Action<World> Run { get; init; }
- void Deconstruct(out string Name, out Action<World> Run)
- override bool Equals(object obj)
- bool Equals(EditorItem other)
- bool Equals(MenuCommand other)
- override int GetHashCode()
- override bool PrintMembers(StringBuilder builder)
- override string ToString()

#### sealed class ObjectInspector : EditorItem
A section in the Inspector for the selected object. Component limits it to objects that have that component (null = every object).
- ctor(Type Component, string Title, Action<PanelBuilder, GameObject> Draw) - A section in the Inspector for the selected object. Component limits it to objects that have that component (null = every object).
- override Type EqualityContract { get; }
- Type Component { get; init; }
- string Title { get; init; }
- Action<PanelBuilder, GameObject> Draw { get; init; }
- bool Collapsed { get; set; }
- void Deconstruct(out Type Component, out string Title, out Action<PanelBuilder, GameObject> Draw)
- override bool Equals(object obj)
- bool Equals(EditorItem other)
- bool Equals(ObjectInspector other)
- override int GetHashCode()
- override bool PrintMembers(StringBuilder builder)
- override string ToString()

#### sealed class PanelBuilder
Draws labelled controls one below the other inside a panel. Every method returns true when the user changed something.
- float Y { get; } - Where the next control is drawn.
- float Width { get; } - Width available for controls.
- bool Button(string text) - A full-width button. True on the frame it is clicked.
- int Buttons(string[] labels) - Several buttons side by side. Returns the index of the clicked one, or -1.
- void Dim(string text)
- bool Dropdown(string label, string[] options, ref int index)
- void Heading(string text)
- void Label(string text, bool dim = false)
- void Separator()
- bool Slider(string label, ref float value, float min, float max)
- bool Slider(string label, ref int value, int min, int max)
- void Space(float height = 6)
- bool Text(string label, ref string value)
- bool Toggle(string label, ref bool value)

#### sealed class PanelItem : EditorItem
A collapsible section at the bottom of the Inspector, always visible. Draw runs every frame.
- ctor(string Title, Action<PanelBuilder> Draw) - A collapsible section at the bottom of the Inspector, always visible. Draw runs every frame.
- override Type EqualityContract { get; }
- string Title { get; init; }
- Action<PanelBuilder> Draw { get; init; }
- bool Collapsed { get; set; }
- void Deconstruct(out string Title, out Action<PanelBuilder> Draw)
- override bool Equals(object obj)
- bool Equals(EditorItem other)
- bool Equals(PanelItem other)
- override int GetHashCode()
- override bool PrintMembers(StringBuilder builder)
- override string ToString()

#### sealed class PublishRequest
What the compiler step needs to know. Filled in by BuildPipeline.
- ctor(string CsprojPath, string RuntimeId, string Configuration, string OutputDirectory, bool SelfContained, bool KeepSymbols) - What the compiler step needs to know. Filled in by BuildPipeline.
- string CsprojPath { get; init; }
- string RuntimeId { get; init; }
- string Configuration { get; init; }
- string OutputDirectory { get; init; }
- bool SelfContained { get; init; }
- bool KeepSymbols { get; init; }
- void Deconstruct(out string CsprojPath, out string RuntimeId, out string Configuration, out string OutputDirectory, out bool SelfContained, out bool KeepSymbols)
- override bool Equals(object obj)
- bool Equals(PublishRequest other)
- override int GetHashCode()
- override string ToString()

#### static class SceneManagerWindow
Manage Scenes: every scene file in a list with check boxes, so many scenes can be opened, selected and deleted at once. Deleted scenes are moved to Backups/DeletedScenes (not erased), and are taken out of the build list, the world map and merge groups. Open with the toolbar's Manage button or Ctrl+Shift+M.
- static bool IsOpen { get; }
- static void Close()
- static void Draw(World world)
- static void Open()

#### static class SceneSerializer
Saves and loads a World as JSON (objects, transforms, components and scene settings).
- static string AddSceneComponent(string sceneJson, Component source) - Pins a copy of a component to the scene itself inside scene file text (for a scene that is not open). Returns the new file text.
- static GameObject Clone(World world, GameObject source) - Copies a GameObject (components included) and offsets it slightly.
- static void Load(World world, string json) - Replaces the contents of the world with the scene in json.
- static string Save(World world)

#### sealed class ShortcutItem : EditorItem
A keyboard shortcut (works while the editor is visible and no text box has focus).
- ctor(KeyboardKey Key, bool Ctrl, bool Shift, string Name, Action<World> Run) - A keyboard shortcut (works while the editor is visible and no text box has focus).
- override Type EqualityContract { get; }
- KeyboardKey Key { get; init; }
- bool Ctrl { get; init; }
- bool Shift { get; init; }
- string Name { get; init; }
- Action<World> Run { get; init; }
- void Deconstruct(out KeyboardKey Key, out bool Ctrl, out bool Shift, out string Name, out Action<World> Run)
- override bool Equals(object obj)
- bool Equals(EditorItem other)
- bool Equals(ShortcutItem other)
- override int GetHashCode()
- override bool PrintMembers(StringBuilder builder)
- override string ToString()

#### sealed class TextBuffer
The text of one file as lines, with a caret, a selection and undo. All editing rules of the code editor live here (no drawing, no input), so they can be tested without a window.
- ctor()
- const int TabSize
- int Line { get; }
- int Col { get; }
- int AnchorLine { get; }
- int AnchorCol { get; }
- int Version { get; } - Changes with every edit (cached syntax colours check it).
- bool Dirty { get; } - True when the text differs from what was last saved.
- int LineCount { get; }
- Func<double> Clock { get; set; } - Seconds clock used to join typing into one undo step (tests replace it).
- string Text { get; set; }
- bool HasSelection { get; }
- ValueTuple<ValueTuple<int, int>, ValueTuple<int, int>> Selection { get; }
- string SelectedText { get; }
- void Backspace(bool word = false)
- string Copy() - Ctrl+C without a selection copies the whole line.
- string Cut() - Ctrl+X without a selection cuts the whole line; returns the text that was removed.
- void Delete(bool word = false)
- void DocumentEnd(bool select = false)
- void DocumentStart(bool select = false)
- void Down(bool select = false, int lines = 1)
- void DuplicateLine() - Ctrl+D: copies the caret line below itself.
- void End(bool select = false)
- string GetLine(int i)
- void Home(bool select = false) - First press goes to the first non-blank character, the next to column 0.
- void Indent() - Tab: inserts spaces up to the next tab stop, or indents every selected line.
- void Insert(string text, bool typing = true) - Types or pastes text at the caret (replacing the selection). Line breaks in the text start new lines.
- void Left(bool select = false, bool word = false)
- void MarkSaved()
- void MoveLine(int delta) - Alt+Up/Down: moves the caret line.
- void MoveTo(int line, int col, bool select = false)
- void NewLine() - Enter: keeps the indent of the line, adds one level after "{", and puts a closing "}" on its own line when between braces.
- void Outdent() - Shift+Tab: removes one indent level from the caret line or every selected line.
- bool Redo()
- void Right(bool select = false, bool word = false)
- void SelectAll()
- void SelectWordAt(int line, int col)
- void ToggleComment() - Ctrl+/ : comments or uncomments the caret line or every selected line.
- void Type(char c) - Types one character. A "}" typed on a line that only holds indentation steps back one indent level first.
- bool Undo()
- void Up(bool select = false, int lines = 1)

#### enum Token
Values: Plain, Keyword, Type, String, Comment, Number, Punctuation

#### sealed class ToolbarButton : EditorItem
A button in the top toolbar.
- ctor(string Label, Action<World> Run) - A button in the top toolbar.
- override Type EqualityContract { get; }
- string Label { get; init; }
- Action<World> Run { get; init; }
- void Deconstruct(out string Label, out Action<World> Run)
- override bool Equals(object obj)
- bool Equals(EditorItem other)
- bool Equals(ToolbarButton other)
- override int GetHashCode()
- override bool PrintMembers(StringBuilder builder)
- override string ToString()

#### static class WebExporter
WebGL build: writes a folder with index.html, player.js and data.js (scenes + meshes) that runs in any browser. Open index.html directly or host the folder on any web server.
- static string[] SupportedComponents - Components the browser player understands. Other (C#) scripts cannot run in the browser.
- static List<string> Export(string outDir, BuildConfig cfg, IReadOnlyList<string> sceneNames) - Writes the player. Returns warnings (e.g. scripts that are left out).

#### static class WorldMapWindow
The world as a grid of chunks. Every chunk is one scene with its own size; adding a scene adds a chunk and the map grows. Select a chunk to change its size, drag it to another cell, or open it to edit that scene's world.
- static bool IsOpen { get; }
- static void Close()
- static void Draw(World world)
- static void Open()


### Namespace SharpEngine.Graphics3D.Scripting

#### sealed class ScriptBuild
The outcome of compiling the Scripts folder. Nothing changes in the running program until Apply.
- ctor()
- bool Success { get; }
- List<ScriptDiagnostic> Diagnostics { get; }
- IReadOnlyList<string> TypeNames { get; }
- IReadOnlyList<string> WorldTypeNames { get; }
- double Milliseconds { get; }
- int Files { get; }
- IEnumerable<ScriptDiagnostic> Errors { get; }

#### sealed class ScriptDiagnostic
One compiler message (error or warning) of a script.
- ctor(string File, int Line, int Column, string Id, string Message, bool IsError) - One compiler message (error or warning) of a script.
- string File { get; init; }
- int Line { get; init; }
- int Column { get; init; }
- string Id { get; init; }
- string Message { get; init; }
- bool IsError { get; init; }
- void Deconstruct(out string File, out int Line, out int Column, out string Id, out string Message, out bool IsError)
- override bool Equals(object obj)
- bool Equals(ScriptDiagnostic other)
- override int GetHashCode()
- override string ToString()

#### static class ScriptManager
C# scripts: every .cs file in the project's Scripts folder is compiled (with Roslyn) into one in-memory assembly, and each public class that derives from Component becomes a component you can add to an object or pin to a scene. Public properties of type float, int, bool, string, Vector3 and Color show up in the Inspector and are saved with the scene. The player compiles the same files when the game starts (the build copies the Scripts folder next to the program).
- const string FolderName
- const string WorldFolderName
- const string EditorFolderName
- static bool PreviewLanguage { get; set; } - Compile scripts with C# preview features (default). Set false to stay on the latest released C# version.
- static string Directory { get; }
- static IReadOnlyList<Type> Types { get; } - The component classes found in the compiled scripts.
- static IReadOnlyList<Type> WorldTypes { get; } - The WorldScript classes found in the compiled scripts.
- static IReadOnlyList<Type> EditorTypes { get; } - The EditorScript classes found in the compiled scripts.
- static ScriptBuild Last { get; } - The last compile (startup or the editor), for the code window.
- static string RuntimeError { get; } - Most recent exception thrown by a script while the game ran, shown in the editor.
- static string WorldCodePath { get; } - Scripts/World/WorldCode.cs, the file the World Map's "World Code" button opens (created on first use).
- static bool IsStale { get; } - True when a script file changed since the scripts were last compiled.
- static event Action Applied - Raised after new scripts replaced the old ones.
- static void Apply(ScriptBuild build) - Makes a successful build the running scripts. Components made from the old scripts must be recreated afterwards (the editor reloads the scene).
- static ScriptBuild Build() - Compiles every script in the Scripts folder. Does not change anything that is running; call Apply to use it.
- static void ClearRuntimeError()
- static string CreateEditorScript(string name)
- static string CreateMeshScript(string name, MeshData mesh = null, string objectName = null) - Creates Scripts/Name.cs holding a MeshGenerator: the code of mesh when given, otherwise a starter example.
- static string CreateScript(string name, bool world = false, string content = null) - Creates Scripts/Name.cs with a starter component. Returns the file path.
- static string EditorTemplateFor(string className)
- static void EnsureLoaded() - Compiles and applies the scripts the first time anything needs the component list (game start).
- static string EnsureWorldCode() - The world code file, created with the starter template when it does not exist yet.
- static IReadOnlyList<string> Files()
- static string FindScriptFile(string className) - The script file that declares class className, or null.
- static bool IsScript(Type t)
- static bool IsScript(object o)
- static IReadOnlyList<string> MissingFrom(ScriptBuild build, IEnumerable<string> used) - Names of components in used that come from scripts but are missing from the new build (they would be lost if the new scripts were applied).
- static bool ProtectProject(string csprojPath = null) - dotnet compiles every .cs file next to a .csproj, so Scripts/*.cs would end up inside the game itself (and fail: the scripts rely on the editor's implicit usings). Makes sure the game's project file leaves the folder out. Returns true when the project file was changed.
- static void ReportRuntimeError(Component c, Exception ex)
- static void ReportWorldScriptError(WorldScript s, Exception ex)
- static void Reset() - Forget everything (tests, a different project folder).
- static string SanitizeName(string name) - "Spin2" is fine, "2spin" and "my script" are not. Returns null when nothing usable is left.
- static string TemplateFor(string className)
- static string WorldTemplateFor(string className)


### Namespace SharpEngine.Inputs

#### sealed class ConsoleInput : IInput
Keyboard input from a terminal. Terminals only report key-down (with auto-repeat), so a key counts as "held" for HoldTimeoutMs after its last event.
- ctor()
- int HoldTimeoutMs { get; set; }
- bool IsDown(Key key)
- void Poll()
- bool WasPressed(Key key)

#### interface IInput
- abstract bool IsDown(Key key) - True while the key is held.
- abstract void Poll() - Refresh state. The engine calls this before every fixed update step.
- abstract bool WasPressed(Key key) - True only on the step in which the key went down.

#### enum Key
Values: Unknown, Up, Down, Left, Right, W, A, S, D, Space, Enter, Escape, Q, Tab

#### sealed class ScriptedInput : IInput
Input driven by a function of the poll index (= update step). Used for tests, bots and replays.
- ctor(Func<long, IReadOnlyCollection<Key>> script)
- bool IsDown(Key key)
- void Poll()
- bool WasPressed(Key key)


### Namespace SharpEngine.Maths

#### struct Rect
Axis-aligned rectangle (origin = top-left).
- ctor(float X, float Y, float Width, float Height) - Axis-aligned rectangle (origin = top-left).
- float X { get; init; }
- float Y { get; init; }
- float Width { get; init; }
- float Height { get; init; }
- float Right { get; }
- float Bottom { get; }
- bool Contains(Vec2 p)
- void Deconstruct(out float X, out float Y, out float Width, out float Height)
- override bool Equals(object obj)
- bool Equals(Rect other)
- override int GetHashCode()
- bool Intersects(Rect o)
- override string ToString()

#### struct Vec2
Immutable 2D vector.
- ctor(float X, float Y) - Immutable 2D vector.
- static Vec2 Zero
- static Vec2 One
- float X { get; init; }
- float Y { get; init; }
- float Length { get; }
- Vec2 Normalized { get; }
- static Vec2 Clamp(Vec2 v, Vec2 min, Vec2 max)
- void Deconstruct(out float X, out float Y)
- static float Distance(Vec2 a, Vec2 b)
- static float Dot(Vec2 a, Vec2 b)
- override bool Equals(object obj)
- bool Equals(Vec2 other)
- override int GetHashCode()
- static Vec2 Lerp(Vec2 a, Vec2 b, float t)
- override string ToString()


### Namespace SharpEngine.Networking

#### sealed class NetClient : IDisposable
Asynchronous TCP client for games. All socket work happens on background tasks; results are queued and delivered on the game thread when the engine calls Poll, so event handlers never need locks.
- ctor()
- bool IsConnected { get; }
- event Action Connected
- event Action<string> Disconnected
- event Action<Packet> PacketReceived
- void Connect(string host, int port) - Starts connecting in the background; returns immediately.
- void Disconnect()
- void Dispose()
- void Poll() - Dispatches queued events on the calling thread. The engine calls this every frame.
- bool Send(ushort id, byte[] payload) - Queues a packet for sending. Never blocks. Returns false if not connected.
- bool Send(ushort id, PacketWriter writer) - Queues a packet for sending. Never blocks. Returns false if not connected.

#### struct Packet
A framed message: a 16-bit message id plus an opaque payload.
- ctor(ushort Id, byte[] Payload) - A framed message: a 16-bit message id plus an opaque payload.
- ushort Id { get; init; }
- byte[] Payload { get; init; }
- void Deconstruct(out ushort Id, out byte[] Payload)
- override bool Equals(object obj)
- bool Equals(Packet other)
- override int GetHashCode()
- override string ToString()

#### static class PacketFraming
Wire format: [int32 length, big-endian][uint16 id, big-endian][payload...] where length = 2 + payload size.
- const int MaxPayload
- static byte[] Encode(ushort id, ReadOnlySpan<byte> payload)
- static Task<Packet?> ReadAsync(Stream stream, CancellationToken ct) - Reads one packet. Returns null on a clean end-of-stream.

#### sealed class PacketReader
Reads values written by PacketWriter, in the same order.
- ctor(byte[] payload)
- bool ReadBool()
- byte ReadByte()
- float ReadFloat()
- int ReadInt()
- string ReadString()

#### sealed class PacketWriter
Fluent builder for packet payloads (little-endian primitives, UTF-8 strings).
- ctor()
- byte[] ToArray()
- PacketWriter Write(byte v)
- PacketWriter Write(bool v)
- PacketWriter Write(int v)
- PacketWriter Write(float v)
- PacketWriter Write(string v)


### Namespace SharpEngine.Rendering

#### struct Color
- ctor(byte R, byte G, byte B, byte A = 255)
- static Color Black
- static Color White
- static Color Red
- static Color Green
- static Color Blue
- static Color Yellow
- static Color Magenta
- static Color Cyan
- static Color Orange
- static Color DarkGray
- static Color Gray
- byte R { get; init; }
- byte G { get; init; }
- byte B { get; init; }
- byte A { get; init; }
- void Deconstruct(out byte R, out byte G, out byte B, out byte A)
- override bool Equals(object obj)
- bool Equals(Color other)
- override int GetHashCode()
- override string ToString()

#### sealed class ConsoleRenderer : FrameBufferRenderer, IRenderer, IDisposable, IPixelPresenter
Renders to a terminal using 24-bit ANSI colour escape sequences.
- ctor(int width = 80, int height = 24)
- override void Dispose()
- override void Initialize(string title)
- override void Present()

#### sealed class FrameBuffer
A grid of character cells with foreground and background colours.
- ctor(int width, int height)
- int Width { get; }
- int Height { get; }
- ref Cell this[int x, int y] { get; }
- void Clear(Color bg)
- void DrawText(float x, float y, string text, Color color)
- void FillRect(float x, float y, float w, float h, Color color)
- void SetCell(int x, int y, char ch, Color fg, Color bg)
- string ToText() - Plain-text dump for logs and tests. Coloured blank cells show as '.'.

#### abstract class FrameBufferRenderer : IRenderer, IDisposable, IPixelPresenter
Base class for renderers that draw into a FrameBuffer and then present it.
- ctor(int width, int height)
- FrameBuffer Buffer { get; }
- int Width { get; }
- int Height { get; }
- int PixelWidth { get; }
- int PixelHeight { get; }
- void BeginFrame(Color clear)
- virtual void Dispose()
- void DrawPixels(Color[] pixels)
- void DrawText(float x, float y, string text, Color color)
- void EndFrame()
- void FillRect(float x, float y, float width, float height, Color color)
- virtual void Initialize(string title)
- abstract void Present()

#### sealed class HeadlessRenderer : FrameBufferRenderer, IRenderer, IDisposable, IPixelPresenter
Draws into memory only. Perfect for tests, servers and CI.
- ctor(int width = 80, int height = 24)
- long FramesPresented { get; }
- string LastFrame { get; }
- override void Present()

#### interface IPixelPresenter
Optional renderer capability: show a full-screen pixel image (used by the 3D rasterizer). Create the Rasterizer at PixelWidth x PixelHeight.
- abstract int PixelWidth { get; }
- abstract int PixelHeight { get; }
- abstract void DrawPixels(Color[] pixels) - Draws a PixelWidth x PixelHeight image over the whole screen.

#### interface IRenderer : IDisposable
Backend-agnostic drawing surface. Coordinates are in the renderer's own units (character cells for the console backends). Implement this to plug in SDL, OpenGL, Vulkan, etc.
- abstract int Width { get; }
- abstract int Height { get; }
- abstract void BeginFrame(Color clear)
- abstract void DrawText(float x, float y, string text, Color color)
- abstract void EndFrame()
- abstract void FillRect(float x, float y, float width, float height, Color color)
- abstract void Initialize(string title)

#### sealed class Rasterizer
Small software 3D rasterizer: perspective-correct colour interpolation, depth buffer, no external dependencies. Renders into Pixels which a renderer then presents.
- ctor(int width, int height)
- int Width { get; }
- int Height { get; }
- Color[] Pixels { get; }
- float[] Depth { get; }
- float Aspect { get; }
- void Clear(Color color)
- void DrawTriangle(Vector3 a, Vector3 b, Vector3 c, Color ca, Color cb, Color cc, in Matrix4x4 mvp) - Draws one triangle. mvp uses System.Numerics conventions (row vectors: model * view * projection). Both faces are drawn. Triangles with a vertex behind the near plane are skipped (no clipping), which is fine for objects kept in front of the camera.
