using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Text;

namespace SharpEngine.Graphics3D;

/// <summary>A mesh read from a model file plus the texture the file points at (if any).</summary>
public sealed class ModelData
{
    public MeshData Mesh { get; init; } = null!;
    /// <summary>Full path of a texture file found next to the model.</summary>
    public string? TextureFile { get; init; }
    /// <summary>A texture stored inside the model file (FBX Video/Content).</summary>
    public byte[]? EmbeddedTexture { get; init; }
    public string? EmbeddedTextureName { get; init; }
}

/// <summary>Reads .fbx (binary) and .obj models into <see cref="MeshData"/> and imports them into the Assets folder.</summary>
public static class ModelLoader
{
    private static readonly Dictionary<string, MeshData> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ModelData Read(string fullPath)
    {
        string ext = Path.GetExtension(fullPath).ToLowerInvariant();
        return ext switch
        {
            ".fbx" => FbxReader.Read(fullPath),
            ".obj" => ObjReader.Read(fullPath),
            _ => throw new NotSupportedException($"Model format '{ext}' is not supported (use .fbx or .obj).")
        };
    }

    /// <summary>Loads (and caches) the mesh of a model asset path such as "Assets/Models/robot.fbx".</summary>
    public static MeshData Load(string assetPath)
    {
        if (Cache.TryGetValue(assetPath, out var cached)) return cached;
        var mesh = Read(Assets.Resolve(assetPath)).Mesh;
        Cache[assetPath] = mesh;
        return mesh;
    }

    public static void ClearCache() => Cache.Clear();

    /// <summary>
    /// Copies a model into Assets/Models, plus its texture into Assets/Textures when one is found.
    /// Returns the relative model path and texture path (null when the model has none).
    /// </summary>
    public static (string Model, string? Texture) Import(string sourceFile)
    {
        var data = Read(sourceFile);                      // fail early on bad files
        string model = Assets.Import(sourceFile, "Models");
        Cache.Remove(model);
        string? texture = null;
        if (data.TextureFile is { } tf) texture = Assets.Import(tf, "Textures");
        else if (data.EmbeddedTexture is { } bytes)
            texture = Assets.ImportBytes(bytes, data.EmbeddedTextureName ?? Path.GetFileNameWithoutExtension(sourceFile) + ".png", "Textures");
        return (model, texture);
    }

    internal static string SniffImageExtension(byte[] b) =>
        b.Length > 3 && b[0] == 0x89 && b[1] == 0x50 ? ".png" :
        b.Length > 2 && b[0] == 0xFF && b[1] == 0xD8 ? ".jpg" :
        b.Length > 1 && b[0] == 0x42 && b[1] == 0x4D ? ".bmp" : ".png";

    internal static string? FindTexture(string modelDir, string recorded)
    {
        if (string.IsNullOrWhiteSpace(recorded)) return null;
        string unified = recorded.Replace('\\', '/');
        string fileName = unified[(unified.LastIndexOf('/') + 1)..];
        var tries = new List<string>();
        if (!Path.IsPathRooted(unified) && !unified.Contains(':')) tries.Add(Path.Combine(modelDir, unified.Replace('/', Path.DirectorySeparatorChar)));
        tries.Add(Path.Combine(modelDir, fileName));
        foreach (var t in tries) if (File.Exists(t)) return t;
        try
        {
            return Directory.EnumerateFiles(modelDir, fileName, new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3, MatchCasing = MatchCasing.CaseInsensitive })
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }
}

// =================================================================================== FBX ==

internal sealed class FbxNode
{
    public string Name = "";
    public List<object> Props = new();
    public List<FbxNode> Children = new();

    public FbxNode? Child(string name) => Children.FirstOrDefault(c => c.Name == name);
    public IEnumerable<FbxNode> All(string name) => Children.Where(c => c.Name == name);
    public string? Str(int i) => i < Props.Count ? Props[i] as string : null;
}

internal static class FbxReader
{
    private const string Magic = "Kaydara FBX Binary  \0";

    public static ModelData Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length > 5 && Encoding.ASCII.GetString(bytes, 0, 5) == "; FBX")
            throw new NotSupportedException("ASCII FBX files are not supported. Export the model as \"FBX Binary\".");
        var root = Parse(bytes);
        return Extract(root, Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
    }

    // ---- container -----------------------------------------------------------------------

    public static FbxNode Parse(byte[] data)
    {
        if (data.Length < 27 || Encoding.ASCII.GetString(data, 0, Magic.Length) != Magic)
            throw new InvalidDataException("Not a binary FBX file.");
        using var r = new BinaryReader(new MemoryStream(data));
        r.BaseStream.Position = 23;
        uint version = r.ReadUInt32();
        var root = new FbxNode();
        int header = version >= 7500 ? 25 : 13;
        while (r.BaseStream.Position + header <= data.Length)
        {
            var n = ReadNode(r, version, data.Length);
            if (n is null) break;
            root.Children.Add(n);
        }
        return root;
    }

    private static FbxNode? ReadNode(BinaryReader r, uint version, long fileLength)
    {
        bool wide = version >= 7500;
        long end = wide ? (long)r.ReadUInt64() : r.ReadUInt32();
        long numProps = wide ? (long)r.ReadUInt64() : r.ReadUInt32();
        long propLen = wide ? (long)r.ReadUInt64() : r.ReadUInt32();
        int nameLen = r.ReadByte();
        if (end == 0) return null;
        if (end > fileLength || end < r.BaseStream.Position) throw new InvalidDataException("Corrupt FBX file (bad record offset).");

        var node = new FbxNode { Name = Encoding.UTF8.GetString(r.ReadBytes(nameLen)) };
        long propsStart = r.BaseStream.Position;
        for (long i = 0; i < numProps; i++) node.Props.Add(ReadProperty(r));
        r.BaseStream.Position = propsStart + propLen;

        while (r.BaseStream.Position < end)
        {
            var child = ReadNode(r, version, fileLength);
            if (child is null) break;
            node.Children.Add(child);
        }
        r.BaseStream.Position = end;
        return node;
    }

    private static object ReadProperty(BinaryReader r)
    {
        char t = (char)r.ReadByte();
        switch (t)
        {
            case 'Y': return (long)r.ReadInt16();
            case 'C': return r.ReadByte() != 0;
            case 'I': return (long)r.ReadInt32();
            case 'F': return (double)r.ReadSingle();
            case 'D': return r.ReadDouble();
            case 'L': return r.ReadInt64();
            case 'S': return Encoding.UTF8.GetString(r.ReadBytes(r.ReadInt32()));
            case 'R': return r.ReadBytes(r.ReadInt32());
            case 'f': case 'd': case 'l': case 'i': case 'b':
            {
                int count = r.ReadInt32(), enc = r.ReadInt32(), compLen = r.ReadInt32();
                int size = t is 'd' or 'l' ? 8 : t is 'b' ? 1 : 4;
                byte[] raw;
                if (enc == 0) raw = r.ReadBytes(count * size);
                else
                {
                    using var z = new ZLibStream(new MemoryStream(r.ReadBytes(compLen)), CompressionMode.Decompress);
                    using var ms = new MemoryStream();
                    z.CopyTo(ms);
                    raw = ms.ToArray();
                }
                if (raw.Length < count * size) throw new InvalidDataException("Corrupt FBX array.");
                switch (t)
                {
                    case 'f': { var a = new double[count]; for (int k = 0; k < count; k++) a[k] = BitConverter.ToSingle(raw, k * 4); return a; }
                    case 'd': { var a = new double[count]; for (int k = 0; k < count; k++) a[k] = BitConverter.ToDouble(raw, k * 8); return a; }
                    case 'l': { var a = new long[count]; for (int k = 0; k < count; k++) a[k] = BitConverter.ToInt64(raw, k * 8); return a; }
                    case 'i': { var a = new int[count]; for (int k = 0; k < count; k++) a[k] = BitConverter.ToInt32(raw, k * 4); return a; }
                    default: return raw;
                }
            }
            default: throw new InvalidDataException($"Unknown FBX property type '{t}'.");
        }
    }

    // ---- scene ---------------------------------------------------------------------------

    private static double Num(object? o) => o switch { double d => d, long l => l, int i => i, _ => 0 };

    private static Vector3 Prop3(FbxNode model, string name, Vector3 fallback)
    {
        var p70 = model.Child("Properties70");
        var p = p70?.All("P").FirstOrDefault(c => c.Str(0) == name);
        if (p is null || p.Props.Count < 7) return fallback;
        return new Vector3((float)Num(p.Props[4]), (float)Num(p.Props[5]), (float)Num(p.Props[6]));
    }

    private static Matrix4x4 LocalMatrix(FbxNode model)
    {
        var t = Prop3(model, "Lcl Translation", Vector3.Zero);
        var rot = Prop3(model, "Lcl Rotation", Vector3.Zero) * (MathF.PI / 180f);
        var s = Prop3(model, "Lcl Scaling", Vector3.One);
        return Matrix4x4.CreateScale(s) * Matrix4x4.CreateRotationX(rot.X) * Matrix4x4.CreateRotationY(rot.Y) *
               Matrix4x4.CreateRotationZ(rot.Z) * Matrix4x4.CreateTranslation(t);
    }

    private static ModelData Extract(FbxNode root, string dir)
    {
        var objects = root.Child("Objects") ?? throw new InvalidDataException("FBX file has no Objects section.");

        var models = new Dictionary<long, FbxNode>();
        foreach (var m in objects.All("Model")) if (m.Props.Count > 0) models[(long)Num(m.Props[0])] = m;

        var parentOf = new Dictionary<long, long>();
        if (root.Child("Connections") is { } conns)
            foreach (var c in conns.All("C"))
                if (c.Props.Count >= 3 && c.Str(0) == "OO") parentOf[(long)Num(c.Props[1])] = (long)Num(c.Props[2]);

        int upAxis = 1;
        double unit = 1;
        if (root.Child("GlobalSettings")?.Child("Properties70") is { } gs)
            foreach (var p in gs.All("P"))
            {
                if (p.Str(0) == "UpAxis" && p.Props.Count > 4) upAxis = (int)Num(p.Props[4]);
                if (p.Str(0) == "UnitScaleFactor" && p.Props.Count > 4) unit = Num(p.Props[4]);
            }
        float scale = (float)(unit <= 0 ? 1 : unit) * 0.01f;      // FBX units are centimetres

        var b = new MeshBuilder();
        int meshes = 0;
        foreach (var geo in objects.All("Geometry"))
        {
            if (geo.Str(2) != "Mesh") continue;
            if (geo.Child("Vertices")?.Props.FirstOrDefault() is not double[] verts) continue;
            if (geo.Child("PolygonVertexIndex")?.Props.FirstOrDefault() is not int[] poly) continue;

            var world = Matrix4x4.Identity;
            long id = (long)Num(geo.Props[0]);
            if (parentOf.TryGetValue(id, out long m) )
                for (int depth = 0; depth < 32 && models.TryGetValue(m, out var model); depth++)
                {
                    world *= LocalMatrix(model);
                    if (!parentOf.TryGetValue(m, out m)) break;
                }

            AddGeometry(b, geo, verts, poly, world, upAxis, scale);
            meshes++;
        }
        if (meshes == 0) throw new InvalidDataException("The FBX file contains no polygon meshes.");

        // texture: the first Texture object, found on disk next to the model, or embedded in a Video object
        string? file = null;
        foreach (var tex in objects.All("Texture"))
        {
            string? rel = tex.Child("RelativeFilename")?.Str(0);
            string? abs = tex.Child("FileName")?.Str(0);
            file = ModelLoader.FindTexture(dir, rel ?? "") ?? ModelLoader.FindTexture(dir, abs ?? "");
            if (file is not null) break;
        }
        byte[]? embedded = null; string? embeddedName = null;
        if (file is null)
            foreach (var vid in objects.All("Video"))
                if (vid.Child("Content")?.Props.FirstOrDefault() is byte[] { Length: > 16 } bytes)
                {
                    embedded = bytes;
                    string n = (vid.Child("RelativeFilename")?.Str(0) ?? vid.Child("Filename")?.Str(0) ?? "").Replace('\\', '/');
                    n = n[(n.LastIndexOf('/') + 1)..];
                    embeddedName = n.Length > 0 && Path.HasExtension(n) ? n : "embedded" + ModelLoader.SniffImageExtension(bytes);
                    break;
                }

        return new ModelData { Mesh = b.Build(true), TextureFile = file, EmbeddedTexture = embedded, EmbeddedTextureName = embeddedName };
    }

    private static int Key(string? mapping, int pv, int vertex, int polygon) => mapping switch
    {
        "ByVertice" or "ByVertex" => vertex,
        "ByPolygon" => polygon,
        "AllSame" => 0,
        _ => pv
    };

    private static void AddGeometry(MeshBuilder b, FbxNode geo, double[] verts, int[] poly, Matrix4x4 world, int upAxis, float scale)
    {
        var nodeN = geo.Child("LayerElementNormal");
        var normals = nodeN?.Child("Normals")?.Props.FirstOrDefault() as double[];
        var nIndex = nodeN?.Child("NormalsIndex")?.Props.FirstOrDefault() as int[];
        string? nMap = nodeN?.Child("MappingInformationType")?.Str(0), nRef = nodeN?.Child("ReferenceInformationType")?.Str(0);

        var nodeU = geo.Child("LayerElementUV");
        var uvs = nodeU?.Child("UV")?.Props.FirstOrDefault() as double[];
        var uIndex = nodeU?.Child("UVIndex")?.Props.FirstOrDefault() as int[];
        string? uMap = nodeU?.Child("MappingInformationType")?.Str(0), uRef = nodeU?.Child("ReferenceInformationType")?.Str(0);

        Vector3 Position(int v)
        {
            if (v < 0 || v * 3 + 2 >= verts.Length) return Vector3.Zero;
            var p = Vector3.Transform(new Vector3((float)verts[v * 3], (float)verts[v * 3 + 1], (float)verts[v * 3 + 2]), world);
            if (upAxis == 2) p = new Vector3(p.X, p.Z, -p.Y);
            return p * scale;
        }

        var polygon = new List<(int Pv, int Vertex)>();
        int polyIndex = 0;
        for (int pv = 0; pv < poly.Length; pv++)
        {
            int raw = poly[pv];
            bool last = raw < 0;
            polygon.Add((pv, last ? ~raw : raw));
            if (!last) continue;

            if (polygon.Count >= 3)
            {
                // flat normal of the polygon, used when the file has none
                var p0 = Position(polygon[0].Vertex);
                var flat = Vector3.Cross(Position(polygon[1].Vertex) - p0, Position(polygon[2].Vertex) - p0);
                flat = flat.LengthSquared() > 1e-18f ? Vector3.Normalize(flat) : Vector3.UnitY;

                int first = -1;
                var ids = new int[polygon.Count];
                for (int k = 0; k < polygon.Count; k++)
                {
                    var (ppv, vtx) = polygon[k];
                    var n = flat;
                    if (normals is not null)
                    {
                        int key = Key(nMap, ppv, vtx, polyIndex);
                        if (nRef is "IndexToDirect" or "Index" && nIndex is not null) key = key < nIndex.Length ? nIndex[key] : -1;
                        if (key >= 0 && key * 3 + 2 < normals.Length)
                        {
                            var nv = Vector3.TransformNormal(new Vector3((float)normals[key * 3], (float)normals[key * 3 + 1], (float)normals[key * 3 + 2]), world);
                            if (upAxis == 2) nv = new Vector3(nv.X, nv.Z, -nv.Y);
                            if (nv.LengthSquared() > 1e-12f) n = Vector3.Normalize(nv);
                        }
                    }
                    var uv = new Vector2(0, 0);
                    bool haveUv = false;
                    if (uvs is not null)
                    {
                        int key = Key(uMap, ppv, vtx, polyIndex);
                        if (uRef is "IndexToDirect" or "Index" && uIndex is not null) key = key < uIndex.Length ? uIndex[key] : -1;
                        if (key >= 0 && key * 2 + 1 < uvs.Length)
                        {
                            uv = new Vector2((float)uvs[key * 2], 1f - (float)uvs[key * 2 + 1]);   // FBX v starts at the bottom
                            haveUv = true;
                        }
                    }
                    ids[k] = b.AddVertex(Position(vtx), n, null, haveUv || uvs is not null ? uv : null);
                    if (first < 0) first = ids[k];
                }
                for (int k = 1; k + 1 < ids.Length; k++) b.AddTriangle(ids[0], ids[k], ids[k + 1]);
            }
            polygon.Clear();
            polyIndex++;
        }
    }
}

// =================================================================================== OBJ ==

internal static class ObjReader
{
    public static ModelData Read(string path)
    {
        var pos = new List<Vector3>(); var uv = new List<Vector2>(); var nor = new List<Vector3>();
        var b = new MeshBuilder();
        string dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        string? mtl = null;
        var inv = CultureInfo.InvariantCulture;
        float F(string s) => float.Parse(s, NumberStyles.Float, inv);

        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            var t = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            switch (t[0])
            {
                case "v" when t.Length >= 4: pos.Add(new Vector3(F(t[1]), F(t[2]), F(t[3]))); break;
                case "vt" when t.Length >= 3: uv.Add(new Vector2(F(t[1]), 1f - F(t[2]))); break;
                case "vn" when t.Length >= 4: nor.Add(new Vector3(F(t[1]), F(t[2]), F(t[3]))); break;
                case "mtllib" when t.Length >= 2: mtl ??= line[7..].Trim(); break;
                case "f" when t.Length >= 4:
                {
                    int count = t.Length - 1;
                    var parts = new (int V, int T, int N)[count];
                    for (int i = 0; i < count; i++)
                    {
                        var f = t[i + 1].Split('/');
                        int Idx(string s, int len) => s.Length == 0 ? -1 : (int.Parse(s, inv) is var n && n < 0 ? len + n : n - 1);
                        parts[i] = (Idx(f[0], pos.Count), f.Length > 1 ? Idx(f[1], uv.Count) : -1, f.Length > 2 ? Idx(f[2], nor.Count) : -1);
                    }
                    if (parts.Any(p => p.V < 0 || p.V >= pos.Count)) throw new InvalidDataException("OBJ face refers to a missing vertex.");
                    var flat = Vector3.Cross(pos[parts[1].V] - pos[parts[0].V], pos[parts[2].V] - pos[parts[0].V]);
                    flat = flat.LengthSquared() > 1e-18f ? Vector3.Normalize(flat) : Vector3.UnitY;
                    var ids = new int[count];
                    for (int i = 0; i < count; i++)
                    {
                        var p = parts[i];
                        var n = p.N >= 0 && p.N < nor.Count ? Vector3.Normalize(nor[p.N]) : flat;
                        Vector2? tc = p.T >= 0 && p.T < uv.Count ? uv[p.T] : null;
                        ids[i] = b.AddVertex(pos[p.V], n, null, tc);
                    }
                    for (int i = 1; i + 1 < count; i++) b.AddTriangle(ids[0], ids[i], ids[i + 1]);
                    break;
                }
            }
        }
        var mesh = b.Build(true);
        if (mesh.Indices.Length == 0) throw new InvalidDataException("The OBJ file contains no faces.");

        string? texture = null;
        if (mtl is not null && File.Exists(Path.Combine(dir, mtl)))
            foreach (var l in File.ReadLines(Path.Combine(dir, mtl)))
            {
                var s = l.Trim();
                if (s.StartsWith("map_Kd", StringComparison.OrdinalIgnoreCase))
                {
                    texture = ModelLoader.FindTexture(dir, s[6..].Trim().Split(' ').Last());
                    if (texture is not null) break;
                }
            }
        return new ModelData { Mesh = mesh, TextureFile = texture };
    }
}
