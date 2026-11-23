# Architecture

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
