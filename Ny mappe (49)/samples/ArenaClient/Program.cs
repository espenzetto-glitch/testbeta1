// Usage:
//   dotnet run --project samples/ArenaClient -- [--host 127.0.0.1] [--port 7777]
// Headless / CI mode (no terminal needed, prints the final frame):
//   dotnet run --project samples/ArenaClient -- --headless --frames 240 --hold D
using Demo;
using SharpEngine;
using SharpEngine.Inputs;
using SharpEngine.Rendering;

string host = "127.0.0.1";
int port = 7777;
bool headless = false;
int frames = 0;
var held = new List<Key>();

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--host": host = args[++i]; break;
        case "--port": port = int.Parse(args[++i]); break;
        case "--headless": headless = true; break;
        case "--frames": frames = int.Parse(args[++i]); break;
        case "--hold": held.Add(Enum.Parse<Key>(args[++i], ignoreCase: true)); break;
        default:
            Console.Error.WriteLine($"Unknown argument: {args[i]}");
            return 1;
    }
}

HeadlessRenderer? headlessRenderer = null;
IRenderer renderer;
IInput input;

if (headless)
{
    if (frames == 0) frames = 240;
    renderer = headlessRenderer = new HeadlessRenderer(80, 24);
    // idle for 20 frames (time to connect), then hold the requested keys
    input = new ScriptedInput(step => step >= 20 ? held : Array.Empty<Key>());
}
else
{
    renderer = new ConsoleRenderer(80, 24);
    input = new ConsoleInput();
}

var config = new EngineConfig
{
    Title = "SharpEngine Arena",
    TargetFps = 60,
    UpdateRate = 60,
    MaxFrames = frames,
    Deterministic = headless,
};

var engine = new Engine(config, renderer, input);
Console.CancelKeyPress += (_, e) => { e.Cancel = true; engine.Quit(); };

engine.Run(new ArenaGame(host, port));

if (headlessRenderer is not null)
    Console.WriteLine(headlessRenderer.LastFrame);

return 0;
