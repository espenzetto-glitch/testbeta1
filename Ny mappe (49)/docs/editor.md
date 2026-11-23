# The editor

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

**Manage (Ctrl+Shift+M)** opens a list of every scene file. Click rows to tick them (or Select all / Select none), then **Delete Selected** and click it a second time to confirm. Double-click a row to open that scene. The **Code** button on a row opens that scene's own code (`Scripts/World/Scene_<Name>.cs`, a WorldScript created from a template the first time) in the code editor; scenes that have code show `[code]`. Ctrl+S compiles it like any script. The scene that is open cannot be deleted. A deleted scene is moved to `Backups/DeletedScenes/<name>-<date>.json` (copy it back into `Scenes/` to undo) and is removed from the build list, the World Map and its merge group. From code: `SceneManager.Delete(name, out reason)` and `SceneManager.DeleteMany(names, out problems)`.

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
