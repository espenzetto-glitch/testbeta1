using System.Numerics;
using System.Runtime.InteropServices;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace SharpEngine.Graphics3D;

/// <summary>
/// Draws meshes on the graphics card: every mesh is uploaded once (split into 16-bit-index chunks), lit by a shader,
/// and skipped when it is outside the camera's view. Millions of triangles are fine; the old per-vertex CPU path
/// stays as a fallback when the shader cannot be created.
/// </summary>
public static unsafe class GpuRenderer
{
    private sealed class Gpu
    {
        public readonly List<Mesh> Chunks = new();
        public int Triangles;
        public int LastUsed;
    }

    private const string Vs = """
        #version 330
        in vec3 vertexPosition; in vec2 vertexTexCoord; in vec3 vertexNormal; in vec4 vertexColor;
        // matrices arrive as 4 rows each and are used as v * M (the System.Numerics convention): no raylib matrix layout involved
        uniform vec4 mvpRows[4]; uniform vec4 worldRows[4]; uniform vec4 normalRows[3]; uniform vec2 tile;
        out vec3 fPos; out vec3 fNormal; out vec2 fUV; out vec4 fColor;
        void main()
        {
            vec4 p = vec4(vertexPosition, 1.0);
            fPos = (p.x * worldRows[0] + p.y * worldRows[1] + p.z * worldRows[2] + worldRows[3]).xyz;
            fNormal = (vertexNormal.x * normalRows[0] + vertexNormal.y * normalRows[1] + vertexNormal.z * normalRows[2]).xyz;
            fUV = vertexTexCoord * tile;
            fColor = vertexColor;
            gl_Position = p.x * mvpRows[0] + p.y * mvpRows[1] + p.z * mvpRows[2] + mvpRows[3];
        }
        """;

    private const string Fs = """
        #version 330
        in vec3 fPos; in vec3 fNormal; in vec2 fUV; in vec4 fColor;
        uniform sampler2D texture0; uniform vec4 colDiffuse;
        uniform vec3 toLight; uniform vec3 sunColor; uniform vec3 viewPos;
        uniform float ambient; uniform float specular; uniform float shininess;
        uniform int unlit; uniform int doubleSided;
        out vec4 finalColor;
        void main()
        {
            vec4 baseCol = texture(texture0, fUV) * fColor * colDiffuse;
            if (unlit != 0) { finalColor = baseCol; return; }
            vec3 n = normalize(fNormal);
            vec3 v = normalize(viewPos - fPos);
            if (doubleSided != 0 && dot(n, v) < 0.0) n = -n;
            float diff = max(dot(n, toLight), 0.0);
            vec3 col = baseCol.rgb * (vec3(ambient) + sunColor * diff);
            if (diff > 0.0 && specular > 0.0)
            {
                vec3 h = normalize(toLight + v);
                col += sunColor * pow(max(dot(n, h), 0.0), shininess) * specular;
            }
            finalColor = vec4(col, baseCol.a);
        }
        """;

    private static readonly Dictionary<MeshData, Gpu> Cache = new(ReferenceEqualityComparer.Instance);
    private static Shader _shader;
    private static Material _material;
    private static bool _tried, _ready;
    private static int _lMvp, _lWorld, _lNormal, _lTile, _lToLight, _lSun, _lView, _lAmb, _lSpec, _lShine, _lUnlit, _lDouble;
    private static int _frame;
    private static readonly Plane[] Frustum = new Plane[6];
    private static bool _frustumOk;

    /// <summary>Turn off to force the old CPU renderer.</summary>
    public static bool Enabled { get; set; } = true;

    /// <summary>False until the shader compiled (needs an open window). Stays false when the graphics driver refuses it.</summary>
    public static bool Available => Enabled && Ready();

    public static int TrianglesDrawn { get; private set; }
    public static int TrianglesCulled { get; private set; }
    public static int MeshesDrawn { get; private set; }
    public static int MeshesCulled { get; private set; }
    public static int MeshesOnGpu => Cache.Count;
    public static long GpuTriangles { get { long t = 0; foreach (var g in Cache.Values) t += g.Triangles; return t; } }

    private static bool Ready()
    {
        if (_ready) return true;
        if (_tried) return false;
        _tried = true;
        try
        {
            if (!Rl.IsWindowReady()) { _tried = false; return false; }
            _shader = Rl.LoadShaderFromMemory(Vs, Fs);
            if (_shader.Id == 0 || !Rl.IsShaderValid(_shader)) { Console.Error.WriteLine("[gpu] shader did not compile; using the CPU renderer"); return false; }
            _material = Rl.LoadMaterialDefault();
            _material.Shader = _shader;
            _lMvp = Rl.GetShaderLocation(_shader, "mvpRows");
            _lWorld = Rl.GetShaderLocation(_shader, "worldRows");
            _lNormal = Rl.GetShaderLocation(_shader, "normalRows");
            _lTile = Rl.GetShaderLocation(_shader, "tile");
            _lToLight = Rl.GetShaderLocation(_shader, "toLight");
            _lSun = Rl.GetShaderLocation(_shader, "sunColor");
            _lView = Rl.GetShaderLocation(_shader, "viewPos");
            _lAmb = Rl.GetShaderLocation(_shader, "ambient");
            _lSpec = Rl.GetShaderLocation(_shader, "specular");
            _lShine = Rl.GetShaderLocation(_shader, "shininess");
            _lUnlit = Rl.GetShaderLocation(_shader, "unlit");
            _lDouble = Rl.GetShaderLocation(_shader, "doubleSided");
            return _ready = true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            Console.Error.WriteLine("[gpu] " + ex.Message);
            return false;
        }
    }

    // ---- frame ------------------------------------------------------------------------------

    /// <summary>Sets up the frame: light, camera and the six frustum planes. Call inside BeginMode3D.</summary>
    public static void BeginFrame(Camera3D cam, DirectionalLight sun, float aspect)
    {
        _frame++;
        TrianglesDrawn = TrianglesCulled = MeshesDrawn = MeshesCulled = 0;
        if (!Available) return;

        _viewProj = Matrix4x4.CreateLookAt(cam.Position, cam.Target, cam.Up) * Projection(cam.FovY * MathF.PI / 180f, MathF.Max(aspect, 0.01f), 0.01f, 1000f);
        SetFrustum(_viewProj);

        var toLight = Vector3.Normalize(-sun.Direction);
        var sunCol = new Vector3(sun.Color.R, sun.Color.G, sun.Color.B) / 255f * sun.Intensity;
        var pos = cam.Position;
        Rl.SetShaderValue(_shader, _lToLight, &toLight, ShaderUniformDataType.Vec3);
        Rl.SetShaderValue(_shader, _lSun, &sunCol, ShaderUniformDataType.Vec3);
        Rl.SetShaderValue(_shader, _lView, &pos, ShaderUniformDataType.Vec3);
        float amb = sun.Ambient;
        Rl.SetShaderValue(_shader, _lAmb, &amb, ShaderUniformDataType.Float);
        _last = default;
    }

    private static Matrix4x4 _viewProj = Matrix4x4.Identity;

    /// <summary>OpenGL perspective matrix for row vectors (v * M), clip depth -w..w.</summary>
    internal static Matrix4x4 Projection(float fovY, float aspect, float near, float far)
    {
        float f = 1f / MathF.Tan(fovY / 2f);
        return new Matrix4x4(
            f / aspect, 0, 0, 0,
            0, f, 0, 0,
            0, 0, (far + near) / (near - far), -1,
            0, 0, 2f * far * near / (near - far), 0);
    }

    public static void EndFrame()
    {
        if (_frame % 120 != 0 || Cache.Count == 0) return;
        // free meshes nobody drew for a while (deleted objects, regenerated meshes)
        foreach (var key in Cache.Where(kv => _frame - kv.Value.LastUsed > 240).Select(kv => kv.Key).ToList())
        {
            Free(Cache[key]);
            Cache.Remove(key);
        }
    }

    /// <summary>Drops every uploaded mesh (call before the window closes or when the graphics context is lost).</summary>
    public static void Clear()
    {
        foreach (var g in Cache.Values) Free(g);
        Cache.Clear();
        if (_ready) { Rl.UnloadShader(_shader); _ready = false; _tried = false; }
    }

    private static void Free(Gpu g) { foreach (var m in g.Chunks) Rl.UnloadMesh(m); g.Chunks.Clear(); }

    // ---- culling ----------------------------------------------------------------------------

    internal static void SetFrustum(Matrix4x4 m)
    {
        // row-vector convention: clip = v * m, so plane k uses column k
        Frustum[0] = Norm(m.M14 + m.M11, m.M24 + m.M21, m.M34 + m.M31, m.M44 + m.M41); // left
        Frustum[1] = Norm(m.M14 - m.M11, m.M24 - m.M21, m.M34 - m.M31, m.M44 - m.M41); // right
        Frustum[2] = Norm(m.M14 + m.M12, m.M24 + m.M22, m.M34 + m.M32, m.M44 + m.M42); // bottom
        Frustum[3] = Norm(m.M14 - m.M12, m.M24 - m.M22, m.M34 - m.M32, m.M44 - m.M42); // top
        Frustum[4] = Norm(m.M14 + m.M13, m.M24 + m.M23, m.M34 + m.M33, m.M44 + m.M43); // near
        Frustum[5] = Norm(m.M14 - m.M13, m.M24 - m.M23, m.M34 - m.M33, m.M44 - m.M43); // far
        _frustumOk = true;
    }

    private static Plane Norm(float a, float b, float c, float d)
    {
        float l = MathF.Sqrt(a * a + b * b + c * c);
        return l > 1e-9f ? new Plane(a / l, b / l, c / l, d / l) : new Plane(0, 0, 0, 1);
    }

    /// <summary>True when the box (local bounds moved by <paramref name="world"/>) is completely outside the current view.</summary>
    internal static bool IsOutside(Vector3 min, Vector3 max, Matrix4x4 world)
    {
        if (!_frustumOk) return false;
        var lo = new Vector3(float.MaxValue);
        var hi = new Vector3(float.MinValue);
        for (int i = 0; i < 8; i++)
        {
            var c = Vector3.Transform(new Vector3((i & 1) == 0 ? min.X : max.X, (i & 2) == 0 ? min.Y : max.Y, (i & 4) == 0 ? min.Z : max.Z), world);
            lo = Vector3.Min(lo, c); hi = Vector3.Max(hi, c);
        }
        foreach (var p in Frustum)
        {
            var v = new Vector3(p.Normal.X >= 0 ? hi.X : lo.X, p.Normal.Y >= 0 ? hi.Y : lo.Y, p.Normal.Z >= 0 ? hi.Z : lo.Z);
            if (Vector3.Dot(p.Normal, v) + p.D < 0f) return true;
        }
        return false;
    }

    // ---- drawing ----------------------------------------------------------------------------

    private struct Last { public float Tu, Tv, Spec, Shine; public int Unlit, Dbl; public bool Set; }
    private static Last _last;

    /// <summary>Draws one mesh object. Returns the triangles drawn (0 when culled or not drawable).</summary>
    internal static int Draw(MeshData mesh, Matrix4x4 world, Color tint, string texturePath, float tu, float tv,
                             bool unlit, bool doubleSided, float specular, float shininess)
    {
        if (!Available || mesh.Indices.Length < 3) return 0;
        int tris = mesh.Indices.Length / 3;

        var (bmin, bmax) = mesh.Bounds;
        if (IsOutside(bmin, bmax, world)) { TrianglesCulled += tris; MeshesCulled++; return 0; }

        if (!Cache.TryGetValue(mesh, out var gpu)) Cache[mesh] = gpu = Upload(mesh);
        gpu.LastUsed = _frame;
        if (gpu.Chunks.Count == 0) return 0;

        bool textured = mesh.UVs is { Length: > 0 } && texturePath.Length > 0 && TextureCache.TryGet(texturePath, out _);
        var map = &_material.Maps[(int)MaterialMapIndex.Albedo];
        map->Color = tint;
        if (textured) { TextureCache.TryGet(texturePath, out var tex); map->Texture = tex; }
        else map->Texture = new Texture2D { Id = Rlgl.GetTextureIdDefault(), Width = 1, Height = 1, Mipmaps = 1, Format = PixelFormat.UncompressedR8G8B8A8 };

        if (!textured) { tu = 1f; tv = 1f; }
        if (!_last.Set || _last.Tu != tu || _last.Tv != tv)
        { var t = new Vector2(tu, tv); Rl.SetShaderValue(_shader, _lTile, &t, ShaderUniformDataType.Vec2); }
        if (!_last.Set || _last.Spec != specular) Rl.SetShaderValue(_shader, _lSpec, &specular, ShaderUniformDataType.Float);
        if (!_last.Set || _last.Shine != shininess) Rl.SetShaderValue(_shader, _lShine, &shininess, ShaderUniformDataType.Float);
        int u = unlit ? 1 : 0, d = doubleSided ? 1 : 0;
        if (!_last.Set || _last.Unlit != u) Rl.SetShaderValue(_shader, _lUnlit, &u, ShaderUniformDataType.Int);
        if (!_last.Set || _last.Dbl != d) Rl.SetShaderValue(_shader, _lDouble, &d, ShaderUniformDataType.Int);
        _last = new Last { Tu = tu, Tv = tv, Spec = specular, Shine = shininess, Unlit = u, Dbl = d, Set = true };

        // matrices go to the shader as plain rows, so raylib's own (transposed) Matrix layout never matters
        var mvp = world * _viewProj;
        Matrix4x4.Invert(world, out var inv);
        var nrm = Matrix4x4.Transpose(inv);
        SetRows(_lMvp, mvp, 4);
        SetRows(_lWorld, world, 4);
        SetRows(_lNormal, nrm, 3);
        foreach (var chunk in gpu.Chunks) Rl.DrawMesh(chunk, _material, Matrix4x4.Identity);
        TrianglesDrawn += gpu.Triangles;
        MeshesDrawn++;
        return gpu.Triangles;
    }

    private static void SetRows(int loc, in Matrix4x4 m, int rows)
    {
        float* f = stackalloc float[16];
        f[0] = m.M11; f[1] = m.M12; f[2] = m.M13; f[3] = m.M14;
        f[4] = m.M21; f[5] = m.M22; f[6] = m.M23; f[7] = m.M24;
        f[8] = m.M31; f[9] = m.M32; f[10] = m.M33; f[11] = m.M34;
        f[12] = m.M41; f[13] = m.M42; f[14] = m.M43; f[15] = m.M44;
        Rl.SetShaderValueV(_shader, loc, f, ShaderUniformDataType.Vec4, rows);
    }

    private static Gpu Upload(MeshData mesh)
    {
        var gpu = new Gpu();
        foreach (var c in MeshChunker.Split(mesh))
        {
            var m = new Mesh(c.VertexCount, c.TriangleCount);
            m.AllocVertices(); m.AllocNormals(); m.AllocTexCoords(); m.AllocColors(); m.AllocIndices();
            for (int i = 0; i < c.Positions.Length; i++) m.Vertices[i] = c.Positions[i];
            for (int i = 0; i < c.Normals.Length; i++) m.Normals[i] = c.Normals[i];
            for (int i = 0; i < c.UVs.Length; i++) m.TexCoords[i] = c.UVs[i];
            for (int i = 0; i < c.Colors.Length; i++) m.Colors[i] = c.Colors[i];
            for (int i = 0; i < c.Indices.Length; i++) m.Indices[i] = c.Indices[i];
            Rl.UploadMesh(ref m, false);
            gpu.Chunks.Add(m);
            gpu.Triangles += c.TriangleCount;
        }
        return gpu;
    }
}
