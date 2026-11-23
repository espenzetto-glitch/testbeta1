using System.Diagnostics;
using SharpEngine.Ecs;
using SharpEngine.Inputs;
using SharpEngine.Networking;
using SharpEngine.Rendering;

namespace SharpEngine;

/// <summary>
/// The client-side engine: owns the game loop (fixed-timestep update, variable-rate render),
/// the scene, input, renderer and network client. Call <see cref="Run"/> once.
/// </summary>
public sealed class Engine
{
    private const double MaxFrameTime = 0.25; // avoids the "spiral of death" after a long stall

    private volatile bool _running;

    public Engine(EngineConfig config, IRenderer renderer, IInput input)
    {
        Config = config ?? throw new ArgumentNullException(nameof(config));
        Renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        Input = input ?? throw new ArgumentNullException(nameof(input));
        Scene = new Scene(this);
    }

    public EngineConfig Config { get; }
    public IRenderer Renderer { get; }
    public IInput Input { get; }
    public NetClient Net { get; } = new();
    public EventBus Events { get; } = new();
    public Scene Scene { get; }

    public bool IsRunning => _running;
    public long Frame { get; private set; }
    public double TotalTime { get; private set; }

    /// <summary>Request the loop to stop after the current frame. Thread-safe.</summary>
    public void Quit() => _running = false;

    public void Run(Game game)
    {
        ArgumentNullException.ThrowIfNull(game);
        game.Attach(this);
        _running = true;

        var clock = Stopwatch.StartNew();
        double step = 1.0 / Config.UpdateRate;
        double last = 0, accumulator = 0;

        try
        {
            Renderer.Initialize(Config.Title);
            game.Initialize();
            last = clock.Elapsed.TotalSeconds;

            while (_running)
            {
                double frameStart = clock.Elapsed.TotalSeconds;

                Net.Poll(); // network events are dispatched on this thread

                accumulator += Config.Deterministic
                    ? step
                    : Math.Min(frameStart - last, MaxFrameTime);
                last = frameStart;

                while (accumulator >= step - 1e-9 && _running)
                {
                    Input.Poll();
                    var time = new GameTime((float)step, TotalTime, Frame);
                    game.Update(time);
                    Scene.Update(time);
                    TotalTime += step;
                    accumulator -= step;
                }

                Renderer.BeginFrame(Config.ClearColor);
                Scene.Render(Renderer);
                game.RenderOverlay(Renderer);
                Renderer.EndFrame();

                Frame++;
                if (Config.MaxFrames > 0 && Frame >= Config.MaxFrames) Quit();

                LimitFrameRate(clock, frameStart);
            }
        }
        finally
        {
            _running = false;
            try { game.Shutdown(); }
            finally
            {
                Scene.Clear();
                Net.Dispose();
                Renderer.Dispose();
            }
        }
    }

    private void LimitFrameRate(Stopwatch clock, double frameStart)
    {
        if (Config.TargetFps <= 0) return;
        double target = frameStart + 1.0 / Config.TargetFps;
        while (true)
        {
            double remaining = target - clock.Elapsed.TotalSeconds;
            if (remaining <= 0) break;
            if (remaining > 0.002) Thread.Sleep(1);
            else Thread.SpinWait(50);
        }
    }
}
