using System.Numerics;
using System.Reflection;
using System.Text;
using Raylib_cs;

namespace SharpEngine.Graphics3D;

/// <summary>
/// A component whose C# code creates the object's triangles. Derive from it in a script, override <see cref="Generate"/> and
/// fill the <see cref="MeshBuilder"/>; the mesh is rebuilt when the script is compiled and whenever one of the script's
/// public properties changes (in the editor too, so you see the result while you type values in the Inspector).
/// </summary>
public abstract class MeshGenerator : Component
{
    private string? _key;
    private static readonly Dictionary<Type, MemberInfo[]> Members = new();

    public override bool RunInEditMode => true;

    /// <summary>Put the triangles into <paramref name="b"/> (positions are in the object's local space).</summary>
    public abstract void Generate(MeshBuilder b);

    /// <summary>Triangles produced by the last successful run.</summary>
    public int TriangleCount { get; private set; }

    /// <summary>The exception message of the last failed run (the previous mesh stays on screen). Null when it worked.</summary>
    public string? LastError { get; private set; }

    /// <summary>Milliseconds the last run needed.</summary>
    public double LastMilliseconds { get; private set; }

    /// <summary>Forces a rebuild on the next frame.</summary>
    public void Rebuild() => _key = null;

    public override void Start() => Regenerate(Snapshot());

    public override void Update(float dt)
    {
        var key = Snapshot();
        if (key != _key) Regenerate(key);
    }

    /// <summary>Runs <see cref="Generate"/> now and gives the result to the object's MeshRenderer (added when missing).</summary>
    public bool Regenerate() => Regenerate(Snapshot());

    private bool Regenerate(string key)
    {
        _key = key;   // do not retry every frame when the code throws
        var watch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var b = new MeshBuilder();
            Generate(b);
            var mesh = b.Build(true, null);
            var mr = GameObject.GetComponent<MeshRenderer>() ?? GameObject.AddComponent(new MeshRenderer());
            mr.Mesh = mesh;
            TriangleCount = mesh.TriangleCount;
            LastError = null;
            LastMilliseconds = watch.Elapsed.TotalMilliseconds;
            return true;
        }
        catch (Exception ex) when (ErrorLog.IsRecoverable(ex))
        {
            LastError = $"{GetType().Name}.Generate: {ErrorLog.Unwrap(ex).Message}";
            ErrorLog.Report(LastError, ex);
            return false;
        }
    }

    // a text of every public property / field the script declares: a change means "rebuild"
    private string Snapshot()
    {
        var type = GetType();
        MemberInfo[] members;
        lock (Members)
        {
            if (!Members.TryGetValue(type, out members!))
                Members[type] = members = type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Where(m => m is FieldInfo { IsInitOnly: false } or PropertyInfo { CanRead: true, CanWrite: true })
                    .Where(m => m is not PropertyInfo p || p.GetIndexParameters().Length == 0)
                    .ToArray();
        }
        var sb = new StringBuilder();
        foreach (var m in members)
        {
            try { sb.Append(m is FieldInfo f ? f.GetValue(this) : ((PropertyInfo)m).GetValue(this)).Append('|'); }
            catch (Exception ex) when (ex is TargetInvocationException or ArgumentException) { }
        }
        return sb.ToString();
    }
}

/// <summary>Turns an existing mesh into the source code of a <see cref="MeshGenerator"/> script.</summary>
public static class MeshCode
{
    /// <summary>Meshes with more triangles than this become a parametric template instead of a list of vertices.</summary>
    public const int MaxLiteralTriangles = 1500;

    public static string FromMesh(MeshData? mesh, string className, string objectName)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"// Mesh code for \"{objectName}\". Edit the code, then press Ctrl+S: the triangles are rebuilt.");
        sb.AppendLine("// Public properties show up in the Inspector; changing one rebuilds the mesh at once.");
        sb.AppendLine("// MeshBuilder: AddVertex, AddTriangle, AddFlatTriangle, AddFlatQuad, AddBox, AddSphere, AddGrid ...");
        sb.AppendLine($"public class {className} : MeshGenerator");
        sb.AppendLine("{");
        if (mesh is null || mesh.TriangleCount == 0 || mesh.TriangleCount > MaxLiteralTriangles)
        {
            if (mesh is { TriangleCount: > MaxLiteralTriangles })
                sb.AppendLine($"    // The original mesh has {mesh.TriangleCount:N0} triangles: too many to list as text, so this is a fresh example.");
            sb.AppendLine(ParametricBody);
        }
        else
        {
            sb.AppendLine("    public override void Generate(MeshBuilder b)");
            sb.AppendLine("    {");
            sb.AppendLine("        // vertices: position, normal, colour");
            for (int i = 0; i < mesh.Positions.Length; i++)
            {
                var p = mesh.Positions[i];
                var n = i < mesh.Normals.Length ? mesh.Normals[i] : Vector3.UnitY;
                var c = mesh.Colors is { } cols && i < cols.Length ? cols[i] : Color.White;
                sb.AppendLine($"        b.AddVertex(new Vector3({F(p.X)}, {F(p.Y)}, {F(p.Z)}), new Vector3({F(n.X)}, {F(n.Y)}, {F(n.Z)}), new Color({c.R}, {c.G}, {c.B}, {c.A}));");
            }
            sb.AppendLine();
            sb.AppendLine("        // triangles: three vertex numbers each");
            for (int t = 0; t < mesh.TriangleCount; t++)
                sb.AppendLine($"        b.AddTriangle({mesh.Indices[t * 3]}, {mesh.Indices[t * 3 + 1]}, {mesh.Indices[t * 3 + 2]});");
            sb.AppendLine("    }");
        }
        sb.AppendLine("}");
        return sb.ToString().Replace("\r\n", "\n");
    }

    public static string Template(string className)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// Mesh code: C# that creates triangles. Press Ctrl+S to compile, then add this component to an object.");
        sb.AppendLine("// Public properties show up in the Inspector; changing one rebuilds the mesh at once.");
        sb.AppendLine($"public class {className} : MeshGenerator");
        sb.AppendLine("{");
        sb.AppendLine(ParametricBody);
        sb.AppendLine("}");
        return sb.ToString().Replace("\r\n", "\n");
    }

    private const string ParametricBody = """
            // A wavy field of triangles. Try Size 100 and Cells 700: that is almost a million triangles.
            public float Size { get; set; } = 20f;
            public int Cells { get; set; } = 40;
            public float Height { get; set; } = 1.5f;
            public float Waves { get; set; } = 3f;
            public Color Low { get; set; } = new Color(40, 120, 60, 255);
            public Color High { get; set; } = new Color(230, 230, 200, 255);

            public override void Generate(MeshBuilder b)
            {
                b.AddGrid(Size, Size, Cells, Cells,
                    (x, z) => MathF.Sin(x * Waves / Size * 6.28f) * MathF.Cos(z * Waves / Size * 6.28f) * Height,
                    h => MeshBuilder.Lerp(Low, High, Math.Clamp(h / Height * 0.5f + 0.5f, 0f, 1f)));
            }
        """;

    private static string F(float v) => v.ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture) + "f";
}
