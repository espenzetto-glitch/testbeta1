// Windowed arena client.
//   dotnet run --project samples\ArenaWindow -- [--host 127.0.0.1] [--port 7777]
using Demo;
using SharpEngine;
using SharpEngine.Backends.RaylibBackend;

string host = "127.0.0.1";
int port = 7777;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--host": host = args[++i]; break;
        case "--port": port = int.Parse(args[++i]); break;
        default:
            Console.Error.WriteLine($"Unknown argument: {args[i]}");
            return 1;
    }
}

var renderer = new RaylibRenderer(80, 24, cellWidth: 12, cellHeight: 24);
var engine = new Engine(
    new EngineConfig { Title = "SharpEngine Arena", UpdateRate = 60, TargetFps = 0 },
    renderer,
    new RaylibInput());

renderer.CloseRequested += engine.Quit;
engine.Run(new ArenaGame(host, port));
return 0;
