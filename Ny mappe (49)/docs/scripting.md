# Scripting and code editors

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
