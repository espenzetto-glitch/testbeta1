using System.Numerics;
using System.Reflection;
using System.Text.RegularExpressions;
using Raylib_cs;

namespace SharpEngine.Graphics3D.Editor;

/// <summary>
/// Finds every Component type in the program and the properties the editor can show and save.
/// Supported property types: float, int, bool, string, Vector3, Color.
/// </summary>
public static class ComponentRegistry
{
    private static readonly Dictionary<Type, PropertyInfo[]> PropertyCache = new();
    private static List<Type>? _types;

    /// <summary>All concrete components with a public parameterless constructor, sorted by name.</summary>
    public static IReadOnlyList<Type> Types => _types ??= Discover();

    public static Type? Find(string name) => Types.FirstOrDefault(t => t.Name == name);

    public static bool IsSupported(Type t) =>
        t == typeof(float) || t == typeof(int) || t == typeof(bool) ||
        t == typeof(string) || t == typeof(Vector3) || t == typeof(Color);

    /// <summary>Public read/write properties of a supported type (base-class ones such as Enabled first).</summary>
    public static PropertyInfo[] Properties(Type type)
    {
        lock (PropertyCache)
        {
            if (PropertyCache.TryGetValue(type, out var cached)) return cached;
            var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.CanWrite
                            && p.GetMethod!.IsPublic && p.SetMethod!.IsPublic
                            && p.GetIndexParameters().Length == 0
                            && IsSupported(p.PropertyType))
                .OrderBy(p => p.DeclaringType == typeof(Component) ? 0 : 1)
                .ToArray();
            return PropertyCache[type] = props;
        }
    }

    /// <summary>"DegreesPerSecond" becomes "Degrees Per Second".</summary>
    public static string Pretty(string name) => Regex.Replace(name, "(?<=[a-z0-9])(?=[A-Z])", " ");

    private static List<Type> Discover()
    {
        var found = new List<Type>();
        var baseAssembly = typeof(Component).Assembly;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.IsDynamic) continue;
            bool relevant = asm == baseAssembly ||
                            asm.GetReferencedAssemblies().Any(n => n.Name == baseAssembly.GetName().Name);
            if (!relevant) continue;

            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t is not null).ToArray()!; }

            foreach (var t in types)
                if (t.IsPublic && !t.IsAbstract && typeof(Component).IsAssignableFrom(t) &&
                    t.GetConstructor(Type.EmptyTypes) is not null)
                    found.Add(t);
        }
        return found.OrderBy(t => t.Name).ToList();
    }
}
