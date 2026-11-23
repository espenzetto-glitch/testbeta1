using System.Numerics;
using Raylib_cs;

namespace SharpEngine.Graphics3D.Editor;

/// <summary>
/// The editor's own optional tools. They are registered through <see cref="EditorApi"/> exactly like a script's tools, which
/// shows what the API can do (and proves it works).
/// </summary>
public sealed class BuiltInTools : EditorScript
{
    public override void OnRegister(EditorApi editor)
    {
        editor.AddCommand("Frame Everything", world => FrameEverything(editor, world));
        editor.AddShortcut(KeyboardKey.Home, world => FrameEverything(editor, world), name: "Frame everything");

        editor.AddCommand("Show All Objects", world =>
        {
            foreach (var o in world.Objects.Where(o => !o.IsScene && !o.Destroyed)) o.Active = true;
            editor.Say("All objects are active");
        });
        editor.AddCommand("Hide Selected Folder", world =>
        {
            if (editor.SelectedFolder is { } f) { world.Folders.SetActive(f, false); editor.Say($"Hid folder {f}"); }
            else editor.Say("Select a folder in the Hierarchy first");
        });
        editor.AddCommand("Show Selected Folder", world =>
        {
            if (editor.SelectedFolder is { } f) { world.Folders.SetActive(f, true); editor.Say($"Showed folder {f}"); }
            else editor.Say("Select a folder in the Hierarchy first");
        });
        editor.AddCommand("Copy Scene Report", world =>
        {
            Raylib.SetClipboardText(SceneReport(world));
            editor.Say("Scene report copied to the clipboard");
        });

        // live numbers, drawn every frame
        editor.AddPanel("Scene Statistics", ui =>
        {
            var world = editor.World;
            ui.Dim($"Objects {world.Objects.Count(o => !o.IsScene && !o.Destroyed)}   Folders {world.Folders.All.Count}");
            ui.Dim($"Triangles drawn {world.TrianglesDrawn:N0}   culled {world.TrianglesCulled:N0}");
            ui.Dim($"Editor tools {EditorExtensions.Items.Count}   scripts {EditorExtensions.Scripts.Count}");
        });

        // only shown for objects that have a MeshRenderer
        editor.AddObjectInspector<MeshRenderer>("Mesh Info", (ui, go) =>
        {
            var mr = go.GetComponent<MeshRenderer>()!;
            if (mr.Mesh is { } m)
            {
                ui.Dim($"{m.Indices.Length / 3:N0} triangles, {m.Positions.Length:N0} vertices");
                var (min, max) = World.WorldBounds(go);
                var size = max - min;
                ui.Dim($"Size {size.X:0.##} x {size.Y:0.##} x {size.Z:0.##}");
            }
            if (ui.Button("Drop To Ground")) AdminEditor.DropToGround(go);
        });
    }

    private static void FrameEverything(EditorApi editor, World world)
    {
        bool any = false;
        Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        foreach (var o in world.Objects)
        {
            if (o.IsScene || o.Destroyed || !o.Active) continue;
            var (lo, hi) = World.WorldBounds(o);
            min = Vector3.Min(min, lo); max = Vector3.Max(max, hi); any = true;
        }
        if (!any) { editor.Say("Nothing to frame"); return; }
        world.EnsureEditorView();
        world.EditorView.Focus((min + max) * 0.5f, Vector3.Distance(min, max) * 0.5f);
        editor.Say("Framed the whole scene");
    }

    private static string SceneReport(World world)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Scene {SceneManager.CurrentName}");
        foreach (var f in world.Folders.All) sb.AppendLine($"  folder {f}: {world.Folders.Objects(f).Count()} object(s)");
        foreach (var o in world.Objects.Where(o => !o.IsScene && !o.Destroyed))
            sb.AppendLine($"  {(o.Folder.Length > 0 ? o.Folder + "/" : "")}{o.Name}  [{string.Join(", ", o.Components.Select(c => c.GetType().Name))}]");
        return sb.ToString();
    }
}
