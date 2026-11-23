// Editor tool: adds buttons, menu commands, panels and shortcuts to the admin editor. Compile with Ctrl+S, no restart needed.
// Everything registered here disappears and is registered again every time the scripts are recompiled.
public class test : EditorScript
{
    private int _count = 5;

    public override void OnRegister(EditorApi editor)
    {
        // Tools menu entry (toolbar > Tools)
        editor.AddCommand("Say hello", world => editor.Say("Hello from " + CurrentScene));

        // a button in the toolbar
        editor.AddToolbarButton("Add Cube", world =>
        {
            var go = world.CreatePrimitive("Cube", Color.White, new Vector3(0, 1, 0));
            editor.Select(go);
        });

        // a section at the bottom of the Inspector, drawn every frame
        editor.AddPanel("My tool", ui =>
        {
            ui.Slider("Count", ref _count, 1, 50);
            if (ui.Button($"Spawn {_count} cubes"))
                for (int i = 0; i < _count; i++)
                    editor.World.CreatePrimitive("Cube", Color.White, new Vector3(i * 2f, 1, 0));
        });

        // a section for selected objects that have a MeshRenderer
        editor.AddObjectInspector<MeshRenderer>("Mesh tools", (ui, go) =>
        {
            ui.Dim(go.Name);
            if (ui.Button("Reset scale")) go.Transform.Scale = Vector3.One;
        });

        // Ctrl+F9
        editor.AddShortcut(KeyboardKey.F9, world => editor.Say("Shortcut pressed"), ctrl: true);
    }

    public override void OnSelectionChanged(GameObject? selected)
    {
    }
}