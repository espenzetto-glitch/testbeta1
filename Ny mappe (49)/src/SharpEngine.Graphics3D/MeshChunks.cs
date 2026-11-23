using System.Numerics;

namespace SharpEngine.Graphics3D;

/// <summary>One GPU-sized piece of a mesh: at most <see cref="MeshChunker.MaxVertices"/> vertices (16-bit indices).</summary>
public sealed class MeshChunk
{
    public int VertexCount;
    public float[] Positions = Array.Empty<float>();
    public float[] Normals = Array.Empty<float>();
    public float[] UVs = Array.Empty<float>();
    public byte[] Colors = Array.Empty<byte>();
    public ushort[] Indices = Array.Empty<ushort>();
    public int TriangleCount => Indices.Length / 3;
}

/// <summary>Splits any <see cref="MeshData"/> (any number of triangles) into chunks the GPU can draw with 16-bit indices.</summary>
public static class MeshChunker
{
    public const int MaxVertices = 65535;

    public static List<MeshChunk> Split(MeshData mesh, int maxVertices = MaxVertices)
    {
        var result = new List<MeshChunk>();
        int vc = mesh.Positions.Length;
        int[] idx = mesh.Indices;
        int triCount = idx.Length / 3;
        if (triCount == 0 || vc == 0) return result;
        maxVertices = Math.Clamp(maxVertices, 3, MaxVertices);

        // vertex -> index inside the current chunk (valid when stamp matches)
        var local = new int[vc];
        var stamp = new int[vc];
        int chunkId = 0;

        var order = new List<int>();   // original vertex per chunk vertex
        var tris = new List<ushort>();

        void Flush()
        {
            if (tris.Count == 0) { order.Clear(); return; }
            var c = new MeshChunk { VertexCount = order.Count };
            c.Positions = new float[order.Count * 3];
            c.Normals = new float[order.Count * 3];
            c.UVs = new float[order.Count * 2];
            c.Colors = new byte[order.Count * 4];
            for (int k = 0; k < order.Count; k++)
            {
                int v = order[k];
                var p = mesh.Positions[v];
                c.Positions[k * 3] = p.X; c.Positions[k * 3 + 1] = p.Y; c.Positions[k * 3 + 2] = p.Z;
                var n = v < mesh.Normals.Length ? mesh.Normals[v] : Vector3.UnitY;
                c.Normals[k * 3] = n.X; c.Normals[k * 3 + 1] = n.Y; c.Normals[k * 3 + 2] = n.Z;
                if (mesh.UVs is { } uv && v < uv.Length) { c.UVs[k * 2] = uv[v].X; c.UVs[k * 2 + 1] = uv[v].Y; }
                if (mesh.Colors is { } col && v < col.Length)
                { c.Colors[k * 4] = col[v].R; c.Colors[k * 4 + 1] = col[v].G; c.Colors[k * 4 + 2] = col[v].B; c.Colors[k * 4 + 3] = col[v].A; }
                else { c.Colors[k * 4] = c.Colors[k * 4 + 1] = c.Colors[k * 4 + 2] = c.Colors[k * 4 + 3] = 255; }
            }
            c.Indices = tris.ToArray();
            result.Add(c);
            order.Clear(); tris.Clear();
            chunkId++;
        }

        for (int t = 0; t < triCount; t++)
        {
            int a = idx[t * 3], b = idx[t * 3 + 1], c2 = idx[t * 3 + 2];
            if ((uint)a >= (uint)vc || (uint)b >= (uint)vc || (uint)c2 >= (uint)vc) continue; // broken triangle: skip it

            int fresh = 0;
            if (stamp[a] != chunkId + 1) fresh++;
            if (stamp[b] != chunkId + 1 && b != a) fresh++;
            if (stamp[c2] != chunkId + 1 && c2 != a && c2 != b) fresh++;
            if (order.Count + fresh > maxVertices) Flush();

            foreach (int v in new[] { a, b, c2 })
            {
                if (stamp[v] != chunkId + 1) { stamp[v] = chunkId + 1; local[v] = order.Count; order.Add(v); }
                tris.Add((ushort)local[v]);
            }
        }
        Flush();
        return result;
    }
}
