# Getting started

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
