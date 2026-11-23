using System.Text;

namespace SharpEngine.Rendering;

/// <summary>Base class for renderers that draw into a <see cref="FrameBuffer"/> and then present it.</summary>
public abstract class FrameBufferRenderer : IRenderer, IPixelPresenter
{
    protected FrameBufferRenderer(int width, int height) => Buffer = new FrameBuffer(width, height);

    protected FrameBuffer Buffer { get; }

    public int Width => Buffer.Width;
    public int Height => Buffer.Height;

    // Each character cell shows two vertically stacked pixels using the half-block character.
    public int PixelWidth => Buffer.Width;
    public int PixelHeight => Buffer.Height * 2;

    public void DrawPixels(Color[] pixels)
    {
        if (pixels.Length != PixelWidth * PixelHeight)
            throw new ArgumentException($"Expected {PixelWidth}x{PixelHeight} pixels.", nameof(pixels));

        for (int y = 0; y < Buffer.Height; y++)
            for (int x = 0; x < Buffer.Width; x++)
            {
                var top = pixels[(2 * y) * PixelWidth + x];
                var bottom = pixels[(2 * y + 1) * PixelWidth + x];
                Buffer.SetCell(x, y, '▀', top, bottom); // upper half block: fg = top, bg = bottom
            }
    }

    public virtual void Initialize(string title) { }
    public void BeginFrame(Color clear) => Buffer.Clear(clear);
    public void FillRect(float x, float y, float width, float height, Color color) => Buffer.FillRect(x, y, width, height, color);
    public void DrawText(float x, float y, string text, Color color) => Buffer.DrawText(x, y, text, color);
    public void EndFrame() => Present();
    public virtual void Dispose() { }

    protected abstract void Present();
}

/// <summary>Draws into memory only. Perfect for tests, servers and CI.</summary>
public sealed class HeadlessRenderer : FrameBufferRenderer
{
    public HeadlessRenderer(int width = 80, int height = 24) : base(width, height) { }

    public long FramesPresented { get; private set; }
    public string LastFrame => Buffer.ToText();

    protected override void Present() => FramesPresented++;
}

/// <summary>Renders to a terminal using 24-bit ANSI colour escape sequences.</summary>
public sealed class ConsoleRenderer : FrameBufferRenderer
{
    private readonly StringBuilder _sb = new();
    private readonly bool _interactive = !Console.IsOutputRedirected;

    public ConsoleRenderer(int width = 80, int height = 24) : base(width, height) { }

    public override void Initialize(string title)
    {
        if (!_interactive) return;

        try
        {
            if (Console.WindowWidth < Width || Console.WindowHeight < Height)
                throw new InvalidOperationException(
                    $"Terminal is {Console.WindowWidth}x{Console.WindowHeight}; this game needs at least {Width}x{Height}. Enlarge the window and retry.");
        }
        catch (IOException) { /* size not queryable; carry on */ }

        Console.OutputEncoding = new UTF8Encoding(false); // half-block characters need UTF-8
        EnableWindowsAnsi();

        // alternate screen buffer, hide cursor, set window title
        Console.Out.Write($"\x1b[?1049h\x1b[?25l\x1b]0;{title}\x07");
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern IntPtr GetStdHandle(int handle);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool GetConsoleMode(IntPtr handle, out uint mode);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool SetConsoleMode(IntPtr handle, uint mode);

    /// <summary>Makes classic cmd.exe understand ANSI colour codes (no-op on other platforms).</summary>
    private static void EnableWindowsAnsi()
    {
        if (!OperatingSystem.IsWindows()) return;
        var h = GetStdHandle(-11); // STD_OUTPUT_HANDLE
        if (GetConsoleMode(h, out uint mode))
            SetConsoleMode(h, mode | 0x0004); // ENABLE_VIRTUAL_TERMINAL_PROCESSING
    }

    protected override void Present()
    {
        if (!_interactive) return;

        _sb.Clear();
        _sb.Append("\x1b[H");
        Color? fg = null, bg = null;

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                var c = Buffer[x, y];
                if (bg != c.Bg)
                {
                    _sb.Append("\x1b[48;2;").Append(c.Bg.R).Append(';').Append(c.Bg.G).Append(';').Append(c.Bg.B).Append('m');
                    bg = c.Bg;
                }
                if (c.Ch != ' ' && fg != c.Fg)
                {
                    _sb.Append("\x1b[38;2;").Append(c.Fg.R).Append(';').Append(c.Fg.G).Append(';').Append(c.Fg.B).Append('m');
                    fg = c.Fg;
                }
                _sb.Append(c.Ch);
            }
            if (y < Height - 1) _sb.Append("\x1b[0m\r\n");
            fg = bg = null;
        }

        Console.Out.Write(_sb.ToString());
    }

    public override void Dispose()
    {
        if (_interactive)
            Console.Out.Write("\x1b[0m\x1b[?25h\x1b[?1049l"); // reset colours, show cursor, leave alt screen
    }
}
