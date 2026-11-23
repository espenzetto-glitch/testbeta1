// Code for the scene "NewScene". It is a world script, so it lives across scene changes, but every
// callback below only fires while this scene is the open one. Press Ctrl+Space for suggestions.
public class Scene_NewScene : WorldScript
{
    private const string SceneName = "NewScene";

    private bool Active => string.Equals(CurrentScene, SceneName, StringComparison.OrdinalIgnoreCase);

    // The scene was opened (also once when play starts, if it is the first scene).
    public override void OnSceneLoaded(string scene)
    {
        if (!string.Equals(scene, SceneName, StringComparison.OrdinalIgnoreCase)) return;
    }

    public override void Update(float dt)
    {
        if (!Active) return;
    }
}