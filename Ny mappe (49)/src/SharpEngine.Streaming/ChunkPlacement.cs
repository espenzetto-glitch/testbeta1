using System.Numerics;

namespace SharpEngine.Streaming;

/// <summary>A cell on the World Map grid.</summary>
public readonly record struct Cell(int X, int Y)
{
    public int ChebyshevTo(Cell other) => Math.Max(Math.Abs(X - other.X), Math.Abs(Y - other.Y));
    public override string ToString() => $"({X},{Y})";
}

/// <summary>Where a scene's local (0,0,0) sits inside its cell.</summary>
public enum ChunkPivot
{
    /// <summary>Scene origin is the middle of the cell (ground plane centred on the origin).</summary>
    Center,
    /// <summary>Scene origin is the minimum corner of the cell.</summary>
    Corner,
}

/// <summary>Axis-aligned rectangle on the XZ plane.</summary>
public readonly record struct Rect2(float MinX, float MinZ, float MaxX, float MaxZ)
{
    public bool Contains(float x, float z) => x >= MinX && x <= MaxX && z >= MinZ && z <= MaxZ;

    public (float X, float Z) ClosestPoint(float x, float z) =>
        (Math.Clamp(x, MinX, MaxX), Math.Clamp(z, MinZ, MaxZ));

    public float DistanceTo(float x, float z)
    {
        var (cx, cz) = ClosestPoint(x, z);
        float dx = x - cx, dz = z - cz;
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    public Rect2 Union(Rect2 o) =>
        new(MathF.Min(MinX, o.MinX), MathF.Min(MinZ, o.MinZ), MathF.Max(MaxX, o.MaxX), MathF.Max(MaxZ, o.MaxZ));

    public Rect2 Expand(float m) => new(MinX - m, MinZ - m, MaxX + m, MaxZ + m);
}

/// <summary>Pure maths that turns map cells into world positions. Scene files are never touched.</summary>
public static class ChunkPlacement
{
    /// <summary>A chunk at cell (x, y) sits at (x * cellSize, 0, y * cellSize).</summary>
    public static Vector3 Offset(Cell cell, float cellSize) =>
        new(cell.X * cellSize, 0f, cell.Y * cellSize);

    /// <summary>The cell that contains a world position.</summary>
    public static Cell CellAt(float x, float z, float cellSize, ChunkPivot pivot)
    {
        float half = pivot == ChunkPivot.Center ? cellSize * 0.5f : 0f;
        return new Cell((int)MathF.Floor((x + half) / cellSize), (int)MathF.Floor((z + half) / cellSize));
    }

    /// <summary>The full square a cell owns in the world.</summary>
    public static Rect2 CellRect(Cell cell, float cellSize, ChunkPivot pivot)
    {
        var o = Offset(cell, cellSize);
        return pivot == ChunkPivot.Center
            ? new Rect2(o.X - cellSize * 0.5f, o.Z - cellSize * 0.5f, o.X + cellSize * 0.5f, o.Z + cellSize * 0.5f)
            : new Rect2(o.X, o.Z, o.X + cellSize, o.Z + cellSize);
    }

    /// <summary>The area a scene of the given size actually covers once placed.</summary>
    public static Rect2 SceneRect(Cell cell, float width, float depth, float cellSize, ChunkPivot pivot)
    {
        var o = Offset(cell, cellSize);
        return pivot == ChunkPivot.Center
            ? new Rect2(o.X - width * 0.5f, o.Z - depth * 0.5f, o.X + width * 0.5f, o.Z + depth * 0.5f)
            : new Rect2(o.X, o.Z, o.X + width, o.Z + depth);
    }
}
