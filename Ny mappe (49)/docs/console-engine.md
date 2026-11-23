# Console / Raylib 2D engine

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
