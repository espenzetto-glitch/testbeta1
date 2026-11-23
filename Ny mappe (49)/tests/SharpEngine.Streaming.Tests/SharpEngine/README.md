# SharpEngine

A small client-side game engine in C# (.NET 8).

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

## WebGL build
In Build Settings pick **WebGL** in the Platform list, then Build (or Build And Run, which opens it in your browser).
The output folder (`Builds/<Product>_WebGL`) holds `index.html`, `player.js` and `data.js`; open `index.html`
or upload the folder to any web host. The browser player renders your scenes (meshes, lighting, sky, grid),
fly camera, scene switching (Tab / Shift+Tab / 1-9) and the built-in `ScenePortal`, `Spinner`, `Bobber` and
`SunOrbit` components. Your own C# scripts cannot run in the browser; they are listed as warnings in `Builds/last-build.log`.

## Scenes and UI scale
- Build Settings > **New Scene** creates a scene file and adds it to the build; **Add Existing** adds any scene from the Scenes folder; **Add Open Scenes** adds the one you are editing.
- **UI Scale** slider in the status bar (bottom right, 75%-200%) or Ctrl +, Ctrl -, Ctrl 0. The value is applied when you release the slider and saved in `EditorPrefs.json`.

## Textures, FBX models and dragging
- **Drag a component onto an object:** grab a component header in the Inspector (the `::` grip) or an entry of the Add Component list and drop it on a Hierarchy row or on an object in the scene. Inspector headers copy the component with its values.
- **Textures:** every mesh (Cube, Sphere, Cylinder, Pyramid, Plane, Triangle, Ground, imported models) has UVs. Set `Texture Path` on a Mesh Renderer (type it, or use the `...` button to pick from the Assets folder) and use `Tile U` / `Tile V` to repeat it. Two sample textures are created in `Assets/Textures` (`checker.png`, `bricks.png`).
- **Drop files on the window:** an image (.png .jpg .bmp .tga) dropped on an object, or on its Hierarchy row, is copied to `Assets/Textures` and applied; if nothing is under the mouse it applies to the selected object. A model (.fbx binary, .obj) dropped anywhere is copied to `Assets/Models` and appears as a new object, together with its texture if the file points at one next to it or embeds it.
- FBX: binary FBX only (export as "FBX Binary"), polygons are triangulated, node transforms, Z-up and the unit scale are applied. Animation, skinning and materials other than the first texture are ignored.
- Builds copy `Assets` next to the player; the WebGL build embeds textures (.png/.jpg) and the models.

## Scene portals: interaction and debug view
- A `ScenePortal` is now interactable by default: walk (fly) within **Radius** and press **E** (`Interact Key`) to enter. A prompt ("Press E to enter Level2", or your own `Prompt` text) appears at the bottom of the screen. Untick **Require Interact** for the old behaviour (enter automatically). E also works while you hold the right mouse button to fly.
- **Show Debug** draws, in the editor, three rings for the trigger radius, a beam above the portal and a line toward the camera, plus a label `Portal -> Level2  r=2.5  d=4.3  [E]  IN RANGE`. Orange = needs the key, yellow = automatic, green = camera in range.
- WebGL: the prompt and the E key work in the browser; the rings are drawn in Development Build exports.
- Portals saved before this change have no `Require Interact` value and pick up the new default (press E).

Note on UI scale: the scale is limited automatically so the Hierarchy, Inspector and a usable 3D view always fit the window (the slider then shows e.g. `200% (fits 129%)`). Ctrl+0 resets it to 100%; the setting is stored in `EditorPrefs.json`.
Portals only work in Play mode (F5); pressing the key near one in Edit mode shows a hint in the status bar, and the debug label says "press Play F5 to use it".
Key presses for portals use Raylib's key queue (`KeyEvents`), so even a very quick tap of E is never missed, and the scene change happens in the same frame.
While a portal is showing its prompt, its key (E) is reserved: the fly camera ignores it, so pressing E to enter a portal no longer also flies you up. Use Q/E elsewhere as before.

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
