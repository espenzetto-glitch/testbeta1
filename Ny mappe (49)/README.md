# SharpEngine

SharpEngine is a C# game engine with a 2D core and a Unity-style 3D engine. It includes an in-game editor, scenes and portals, world streaming, C# scripting, GPU rendering, and desktop and browser builds.

## Features

- 2D game loop, scenes, input, networking and Raylib rendering
- 3D worlds made from `GameObject`, `Transform` and `Component` types
- Scene hierarchy, inspector, code editor, world map and build tools
- C# scripting with hot compilation and automatic error logging
- GPU rendering with a CPU fallback
- Desktop builds and WebGL/WebAssembly browser builds

## Requirements

- Windows, macOS or Linux
- .NET 11 SDK for C# 15 features such as `union` types and `[with(...)]`
- .NET 8 can be used with `-p:SharpTfm=net8.0`; C# 15 features are unavailable
- OpenGL 3.3 or newer for the GPU renderer

Check the installed SDKs:

```powershell
dotnet --list-sdks
```

## Run the 3D editor

From the repository root:

```powershell
dotnet run --project samples\Game3D
```

With .NET 8:

```powershell
dotnet run --project samples\Game3D -p:SharpTfm=net8.0
```

## Controls

- `Right mouse + W/A/S/D/Q/E`: fly the editor camera
- `Shift`: move faster
- `Middle mouse`: pan
- `Alt + left mouse`: orbit
- `F`: focus selected object
- `G`: drop to ground
- `Left mouse`: select or drag
- `F5`: enter Play mode
- `F1`: hide or show the editor
- `F3`: open the chunk debugger
- `F4`: open the code editor
- `Ctrl+S`: save or compile scripts
- `Ctrl+I`: import models, textures or folders
- `F2` or `` ` ``: open the console

## Project structure

| Path | Purpose |
|---|---|
| `src/SharpEngine` | Core 2D engine |
| `src/SharpEngine.Raylib` | Raylib renderer and input |
| `src/SharpEngine.Graphics3D` | 3D engine and editor |
| `src/SharpEngine.Streaming` | World streaming implementation |
| `src/SharpEngine.Web` | WebAssembly browser player |
| `samples/Game3D` | Main 3D editor and demo |
| `samples/ArenaClient` | 2D arena client |
| `samples/ArenaWindow` | 2D Raylib window demo |
| `samples/DemoServer` | 2D demo server |
| `docs/` | User and API documentation |
| `tests/` | Engine and streaming tests |

Game data is stored beside the Game3D project:

- `Scenes/`: scene JSON files
- `Scripts/`: C# component and world scripts
- `Assets/`: textures and models
- `WorldMap.json`: scene positions and merge groups
- `BuildSettings.json`: platform and build settings
- `Logs/`: runtime error logs
- `Backups/`: automatic scene backups

## Build and test

Build the solution:

```powershell
dotnet build SharpEngine.sln
```

Run the C# tests:

```powershell
dotnet test tests\SharpEngine.Streaming.Tests\SharpEngine.Streaming.Tests.csproj
```

Run the JavaScript streaming tests:

```powershell
node tests\streaming.test.mjs
```

## Documentation

- [Getting started](docs/getting-started.md)
- [Editor guide](docs/editor.md)
- [Scenes and worlds](docs/scenes-and-worlds.md)
- [Scripting](docs/scripting.md)
- [Rendering and export](docs/rendering-and-export.md)
- [Architecture](docs/architecture.md)
- [Console and 2D engine](docs/console-engine.md)
- [AI guide](docs/AI_GUIDE.md)
- [Complete engine reference](docs/ENGINE_REFERENCE.md)
- [Troubleshooting](docs/troubleshooting.md)
