using System.Text;

namespace SharpEngine.Rendering;

/// <summary>A grid of character cells with foreground and background colours.</summary>
public sealed class FrameBuffer
{
    public struct Cell
    {
        public char Ch;
        public Color Fg;
        public Color Bg;
    }

    private readonly Cell[] _cells;
    private Color _clear = Color.Black;
    private readonly Color _bg3d = new(14, 16, 32);

    public FrameBuffer(int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "Size must be positive.");
        Width = width;
        Height = height;
        _cells = new Cell[width * height];
    }

    public int Width { get; }
    public int Height { get; }

    public ref readonly Cell this[int x, int y] => ref _cells[y * Width + x];

    public void Clear(Color bg)
    {
        _clear = bg;
        for (int i = 0; i < _cells.Length; i++)
            _cells[i] = new Cell { Ch = ' ', Fg = Color.White, Bg = bg };
    }

    public void SetCell(int x, int y, char ch, Color fg, Color bg)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return;
        _cells[y * Width + x] = new Cell { Ch = ch, Fg = fg, Bg = bg };
    }

    public void FillRect(float x, float y, float w, float h, Color color)
    {
        if (color.A == 0) return;
        int x0 = (int)MathF.Round(x), y0 = (int)MathF.Round(y);
        int x1 = x0 + Math.Max(1, (int)MathF.Round(w));
        int y1 = y0 + Math.Max(1, (int)MathF.Round(h));

        for (int cy = Math.Max(0, y0); cy < Math.Min(Height, y1); cy++)
            for (int cx = Math.Max(0, x0); cx < Math.Min(Width, x1); cx++)
            {
                ref var c = ref _cells[cy * Width + cx];
                c.Ch = ' ';
                c.Bg = color;
            }
    }

    public void DrawText(float x, float y, string text, Color color)
    {
        int cy = (int)MathF.Round(y);
        if (cy < 0 || cy >= Height) return;
        int cx = (int)MathF.Round(x);
        foreach (char ch in text)
        {
            if (cx >= Width) break;
            if (cx >= 0)
            {
                ref var c = ref _cells[cy * Width + cx];
                c.Ch = ch;
                c.Fg = color;
            }
            cx++;
        }
    }

    /// <summary>Plain-text dump for logs and tests. Coloured blank cells show as '.'.</summary>
    public string ToText()
    {
        var sb = new StringBuilder(Width * Height + Height);
        for (int y = 0; y < Height; y++)
        {
            int rowStart = sb.Length;
            for (int x = 0; x < Width; x++)
            {
                var c = _cells[y * Width + x];
                if (c.Ch == '\u2580') // 3D half-block cell: show '#' where something other than background is drawn
                    sb.Append(c.Fg != c.Bg || (c.Bg != _bg3d && c.Bg != _clear) ? '#' : ' ');
                else
                    sb.Append(c.Ch != ' ' ? c.Ch : (c.Bg != _clear ? '.' : ' '));
            }
            while (sb.Length > rowStart && sb[^1] == ' ') sb.Length--;
            if (y < Height - 1) sb.Append('\n');
        }
        return sb.ToString();
    }
}
