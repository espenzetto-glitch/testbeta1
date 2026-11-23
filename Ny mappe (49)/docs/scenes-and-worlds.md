# Scenes, portals and the world

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
