using System.Numerics;
using System.Reflection;
using System.Text.RegularExpressions;

namespace SharpEngine.Graphics3D.Editor;

public enum CompletionKind { Method, Property, Field, Type, Keyword, Variable, Object, Scene, Group, Component }

public sealed record CompletionItem(string Text, string Insert, CompletionKind Kind, string Detail);

public sealed class CompletionResult
{
    /// <summary>Column where the text that gets replaced starts.</summary>
    public int ReplaceStart { get; init; }
    public string Prefix { get; init; } = "";
    public bool InString { get; init; }
    public List<CompletionItem> Items { get; init; } = new();
}

/// <summary>
/// Suggestions for the code editor. It knows the whole engine (every public class, property and method, found by reflection) and
/// the whole project (every scene, object, component and merge group, from <see cref="WorldIndex"/>):
///  - after a dot: the members of whatever the expression before it is, e.g. <c>World.Find("Door").Transform.</c>
///  - inside quotes of Find("..."), LoadScene("..."), Merge("...") and friends: object, scene and group names
///  - inside GetComponent&lt;...&gt;: component types
///  - otherwise: variables of the file, members of the class it derives from, engine types, keywords
/// </summary>
public static class CompletionEngine
{
    private static readonly string[] Keywords =
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "class", "const", "continue", "default", "do", "double",
        "else", "enum", "false", "finally", "float", "for", "foreach", "if", "in", "int", "interface", "is", "long", "namespace", "new", "null",
        "object", "out", "override", "private", "protected", "public", "readonly", "ref", "return", "static", "string", "struct", "switch",
        "this", "throw", "true", "try", "typeof", "using", "var", "virtual", "void", "while",
        // C# 12-15: records, unions, closed hierarchies, extension blocks, newer modifiers
        "record", "union", "closed", "extension", "required", "scoped", "partial", "with", "safe", "unsafe", "async", "await", "yield"
    };

    private static readonly Dictionary<string, string> Aliases = new()
    {
        ["int"] = "Int32", ["uint"] = "UInt32", ["long"] = "Int64", ["short"] = "Int16", ["byte"] = "Byte", ["float"] = "Single", ["double"] = "Double",
        ["bool"] = "Boolean", ["string"] = "String", ["char"] = "Char", ["object"] = "Object", ["void"] = "Void"
    };

    private static readonly string[] ObjectMethods = { "Find", "FindObject", "FindObjects", "GetObject", "WithName" };
    private static readonly string[] SceneMethods = { "LoadScene", "Merge", "Unmerge", "ToggleMerge", "ResolveMerge", "IsMerged", "Scene", "Open", "Exists", "FileFor", "GroupOf", "GroupNamed", "SetGroupOf", "OnMap" };
    private static readonly string[] ComponentMethods = { "WithComponent", "FindComponent" };

    private static Dictionary<string, Type>? _types;

    /// <summary>Every type that scripts can use by its short name (engine, Raylib colours and keys, System, collections, numerics).</summary>
    public static IReadOnlyDictionary<string, Type> Types
    {
        get
        {
            if (_types is not null) return _types;
            var map = new Dictionary<string, Type>(StringComparer.Ordinal);
            void AddFrom(Assembly asm, Func<Type, bool> accept)
            {
                Type[] types;
                try { types = asm.GetExportedTypes(); }
                catch (Exception ex) when (ex is NotSupportedException or FileNotFoundException or ReflectionTypeLoadException) { return; }
                foreach (var t in types)
                {
                    if (t.IsNested || !accept(t) || t.IsSpecialName) continue;
                    string name = t.Name.Contains('`') ? t.Name[..t.Name.IndexOf('`')] : t.Name;
                    if (name.StartsWith('<') || map.ContainsKey(name)) continue;
                    map[name] = t;
                }
            }
            AddFrom(typeof(Component).Assembly, t => t.Namespace is { } ns && ns.StartsWith("SharpEngine.Graphics3D", StringComparison.Ordinal) && !ns.EndsWith("Editor", StringComparison.Ordinal));
            AddFrom(typeof(Raylib_cs.Color).Assembly, t => t.Namespace == "Raylib_cs" && (t.IsEnum || t.IsValueType || t.Name == "Raylib"));
            AddFrom(typeof(Vector3).Assembly, t => t.Namespace == "System.Numerics");
            AddFrom(typeof(object).Assembly, t => t.Namespace is "System" or "System.Collections.Generic" or "System.IO" or "System.Text" or "System.Linq" && !t.Name.StartsWith("Func`") && !t.Name.StartsWith("Action`")
                                                  || t.FullName == "System.Func`2" || t.FullName == "System.Action`1");
            return _types = map;
        }
    }

    // ---- entry point ------------------------------------------------------------------------------

    /// <summary>Suggestions for the caret position, or null when nothing fits. <paramref name="explicitRequest"/> is true for Ctrl+Space.</summary>
    public static CompletionResult? Complete(string text, int line, int col, bool explicitRequest)
    {
        try { return CompleteCore(text.Replace("\r\n", "\n"), line, col, explicitRequest); }
        catch (Exception ex) when (ErrorLog.IsRecoverable(ex))
        {
            ErrorLog.Report("code completion", ex);   // a completion bug must never get in the way of typing
            return null;
        }
    }

    private static CompletionResult? CompleteCore(string text, int line, int col, bool explicitRequest)
    {
        var lines = text.Split('\n');
        if (line < 0 || line >= lines.Length) return null;
        string cur = lines[line];
        col = Math.Clamp(col, 0, cur.Length);
        string before = cur[..col];

        // comments: no suggestions
        int slashes = IndexOutsideString(before, "//");
        if (slashes >= 0) return null;

        // ---- inside a string literal
        int quote = OpenQuote(before);
        if (quote >= 0) return CompleteString(before, quote, explicitRequest);

        // ---- generic argument of GetComponent<...>
        var gen = Regex.Match(before, @"\b(GetComponent|GetComponents|AddComponent|RemoveComponent|WithComponent)\s*<\s*(\w*)$");
        if (gen.Success)
            return Filter(ComponentItems(), gen.Groups[2].Value, col - gen.Groups[2].Length);

        // ---- the word being typed, and what is before it
        int wordStart = col;
        while (wordStart > 0 && (char.IsLetterOrDigit(before[wordStart - 1]) || before[wordStart - 1] == '_')) wordStart--;
        string prefix = before[wordStart..];

        bool dot = wordStart > 0 && before[wordStart - 1] == '.';
        if (dot)
        {
            int recvEnd = wordStart - 1;
            if (recvEnd > 0 && before[recvEnd - 1] == '?') recvEnd--;
            int recvStart = ReceiverStart(before, recvEnd);
            if (recvStart >= recvEnd) return null;
            string receiver = before[recvStart..recvEnd];
            var (type, isStatic) = ResolveChain(receiver, text, line);
            if (type is null) return null;
            return Filter(MemberItems(type, isStatic), prefix, wordStart);
        }

        if (prefix.Length == 0 && !explicitRequest) return null;
        if (prefix.Length > 0 && char.IsDigit(prefix[0])) return null;

        // ---- a plain name: variables of the file, members of the base class, types, keywords
        var items = new List<CompletionItem>();
        foreach (var v in Declarations(text, line).Keys.Distinct()) items.Add(new CompletionItem(v, v, CompletionKind.Variable, "variable"));
        if (BaseType(text, line) is { } bt) items.AddRange(MemberItems(bt, false, includeProtected: true));
        foreach (var kv in Types) items.Add(new CompletionItem(kv.Key, kv.Key, CompletionKind.Type, TypeKind(kv.Value)));
        foreach (var k in Keywords) items.Add(new CompletionItem(k, k, CompletionKind.Keyword, "keyword"));
        return Filter(items, prefix, wordStart);
    }

    // ---- strings --------------------------------------------------------------------------------

    private static CompletionResult? CompleteString(string before, int quote, bool explicitRequest)
    {
        string typed = before[(quote + 1)..];
        string head = before[..quote].TrimEnd();
        string? method = Regex.Match(head, @"(\w+)\s*\([^()]*$") is { Success: true } m ? m.Groups[1].Value : null;
        string? member = Regex.Match(head, @"(\w+)\s*(?:==|!=|=)\s*$") is { Success: true } m2 ? m2.Groups[1].Value : null;

        List<CompletionItem>? items = null;
        if (method is not null && ObjectMethods.Contains(method)) items = ObjectItems();
        else if (method is not null && SceneMethods.Contains(method)) items = SceneItems();
        else if (method is not null && ComponentMethods.Contains(method)) items = ComponentItems();
        else if (member is not null && (member is "TargetScene" or "CurrentName" or "CurrentScene" or "Scene")) items = SceneItems();
        else if (member is "Name") items = ObjectItems();
        else if (explicitRequest) items = ObjectItems().Concat(SceneItems()).Concat(ComponentItems()).ToList();
        if (items is null) return null;

        var result = Filter(items, typed, quote + 1);
        return result is null ? null : new CompletionResult { ReplaceStart = result.ReplaceStart, Prefix = result.Prefix, InString = true, Items = result.Items };
    }

    private static List<CompletionItem> ObjectItems()
    {
        var items = new List<CompletionItem>();
        foreach (var g in WorldIndex.Objects.Where(o => o.Name != "(scene components)").GroupBy(o => o.Name, StringComparer.Ordinal))
        {
            var first = g.First();
            string comps = string.Join(", ", first.Components.Select(c => c.Type).Take(3));
            string where = g.Count() == 1 ? first.Scene : first.Scene + " +" + (g.Count() - 1);
            items.Add(new CompletionItem(g.Key, g.Key, CompletionKind.Object, where + (comps.Length > 0 ? "  [" + comps + "]" : "")));
        }
        return items;
    }

    private static List<CompletionItem> SceneItems()
    {
        var items = new List<CompletionItem>();
        foreach (var s in WorldIndex.Scenes)
            items.Add(new CompletionItem(s.Name, s.Name, CompletionKind.Scene, $"scene, {s.Objects.Count} objects" + (s.OnMap ? $", cell {s.CellX},{s.CellY}" : "")));
        foreach (var g in WorldLayout.Groups)
            items.Add(new CompletionItem(g.Name, g.Name, CompletionKind.Group, "merge group: " + string.Join(" + ", g.Scenes)));
        return items;
    }

    private static List<CompletionItem> ComponentItems()
    {
        var items = new List<CompletionItem>();
        foreach (var t in ComponentRegistry.Types) items.Add(new CompletionItem(t.Name, t.Name, CompletionKind.Component, "component"));
        foreach (var t in Types.Values.Where(t => typeof(Component).IsAssignableFrom(t) && !t.IsAbstract)) 
            if (items.All(i => i.Text != t.Name)) items.Add(new CompletionItem(t.Name, t.Name, CompletionKind.Component, "component"));
        return items;
    }

    // ---- filtering ------------------------------------------------------------------------------

    private static CompletionResult? Filter(List<CompletionItem> items, string prefix, int replaceStart)
    {
        IEnumerable<CompletionItem> q = items;
        if (prefix.Length > 0)
            q = items.Where(i => i.Text.Contains(prefix, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(i => i.Text.StartsWith(prefix, StringComparison.Ordinal) ? 0 : i.Text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? 1 : 2);
        var list = q.GroupBy(i => (i.Text, i.Kind)).Select(g => g.First()).ToList();
        if (prefix.Length == 0) list = list.OrderBy(i => KindOrder(i.Kind)).ThenBy(i => i.Text, StringComparer.OrdinalIgnoreCase).ToList();
        else list = list.OrderBy(i => i.Text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(i => KindOrder(i.Kind)).ThenBy(i => i.Text, StringComparer.OrdinalIgnoreCase).ToList();
        if (list.Count == 0) return null;
        if (list.Count == 1 && list[0].Text == prefix) return null;   // already typed completely
        return new CompletionResult { ReplaceStart = replaceStart, Prefix = prefix, Items = list.Take(200).ToList() };
    }

    private static int KindOrder(CompletionKind k) => k switch
    {
        CompletionKind.Variable => 0, CompletionKind.Property => 1, CompletionKind.Field => 1, CompletionKind.Method => 2,
        CompletionKind.Object => 0, CompletionKind.Scene => 0, CompletionKind.Group => 1, CompletionKind.Component => 0,
        CompletionKind.Keyword => 3, CompletionKind.Type => 4, _ => 5
    };

    // ---- the expression before a dot ----------------------------------------------------------

    private static int IndexOutsideString(string s, string what)
    {
        bool inStr = false;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '"' && (i == 0 || s[i - 1] != '\\')) inStr = !inStr;
            else if (!inStr && string.CompareOrdinal(s, i, what, 0, what.Length) == 0) return i;
        }
        return -1;
    }

    /// <summary>True when the caret (after <paramref name="before"/>, the text of the line up to it) is inside a string literal.</summary>
    public static bool InsideString(string before) => OpenQuote(before) >= 0;

    /// <summary>Index of the quote that opens the string the caret is in, or -1.</summary>
    private static int OpenQuote(string before)
    {
        int open = -1;
        for (int i = 0; i < before.Length; i++)
        {
            if (before[i] == '"' && (i == 0 || before[i - 1] != '\\')) open = open < 0 ? i : -1;
            else if (before[i] == '\'' && open < 0 && i + 2 < before.Length && before[i + 2] == '\'') i += 2;   // 'x'
        }
        return open;
    }

    /// <summary>Walks backwards over identifiers, calls, indexers and generic arguments to find where the receiver expression starts.</summary>
    private static int ReceiverStart(string s, int end)
    {
        int i = end;
        while (i > 0)
        {
            char c = s[i - 1];
            if (c is ')' or ']' or '>')
            {
                char close = c, open = c == ')' ? '(' : c == ']' ? '[' : '<';
                int depth = 0, j = i - 1;
                bool inStr = false;
                for (; j >= 0; j--)
                {
                    if (s[j] == '"' && (j == 0 || s[j - 1] != '\\')) inStr = !inStr;
                    if (inStr) continue;
                    if (s[j] == close) depth++;
                    else if (s[j] == open && --depth == 0) break;
                }
                if (j < 0) return end;
                i = j;
                continue;
            }
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                while (i > 0 && (char.IsLetterOrDigit(s[i - 1]) || s[i - 1] == '_')) i--;
                if (i > 0 && s[i - 1] == '.') { i--; if (i > 0 && s[i - 1] == '?') i--; continue; }
                if (i > 0 && s[i - 1] == '?' && i > 1 && s[i - 2] == '.') { i -= 2; continue; }
                return i;
            }
            return i;
        }
        return i;
    }

    private sealed record Segment(string Name, string? Generic, bool IsCall, bool IsIndex);

    private static List<Segment> Parse(string expr)
    {
        var parts = new List<Segment>();
        int i = 0;
        expr = expr.Trim();
        if (expr.StartsWith("new ", StringComparison.Ordinal))
        {
            var m = Regex.Match(expr, @"^new\s+([\w\.]+(?:<[^()]*>)?)\s*(\(.*\))?");
            if (!m.Success) return parts;
            parts.Add(new Segment("new " + m.Groups[1].Value, null, true, false));
            i = m.Length;
            if (i < expr.Length && expr[i] == '.') i++;
        }
        while (i < expr.Length)
        {
            int start = i;
            while (i < expr.Length && (char.IsLetterOrDigit(expr[i]) || expr[i] == '_')) i++;
            string name = expr[start..i];
            string? generic = null;
            bool call = false, index = false;
            if (i < expr.Length && expr[i] == '<')
            {
                int end = Skip(expr, i, '<', '>');
                if (end > 0) { generic = expr[(i + 1)..end]; i = end + 1; }
            }
            if (i < expr.Length && expr[i] == '(') { int end = Skip(expr, i, '(', ')'); if (end < 0) return parts; call = true; i = end + 1; }
            parts.Add(new Segment(name, generic, call, false));
            while (i < expr.Length && expr[i] == '[') { int end = Skip(expr, i, '[', ']'); if (end < 0) return parts; parts.Add(new Segment("", null, false, true)); i = end + 1; index = true; }
            if (i < expr.Length && expr[i] == '?') i++;
            if (i < expr.Length && expr[i] == '!') i++;
            if (i < expr.Length && expr[i] == '.') i++;
            else if (i < expr.Length) break;
            _ = index;
        }
        return parts;
    }

    private static int Skip(string s, int open, char o, char c)
    {
        int depth = 0; bool inStr = false;
        for (int j = open; j < s.Length; j++)
        {
            if (s[j] == '"' && (j == 0 || s[j - 1] != '\\')) inStr = !inStr;
            if (inStr) continue;
            if (s[j] == o) depth++;
            else if (s[j] == c && --depth == 0) return j;
        }
        return -1;
    }

    /// <summary>The type of an expression such as <c>World.Find("Door").Transform</c>, and whether it names the type itself (static access).</summary>
    private static (Type? Type, bool IsStatic) ResolveChain(string expr, string text, int line, int depth = 0)
    {
        if (depth > 4) return (null, false);
        var parts = Parse(expr);
        if (parts.Count == 0) return (null, false);

        Type? type = null;
        bool isStatic = false;
        var first = parts[0];
        int next = 1;

        if (first.Name.StartsWith("new ", StringComparison.Ordinal)) type = ResolveTypeName(first.Name[4..]);
        else if (first.Name == "this") type = BaseType(text, line);
        else if (first.Name == "base") type = BaseType(text, line);
        else
        {
            var decls = Declarations(text, line);
            if (!first.IsCall && decls.TryGetValue(first.Name, out var decl))
                type = decl.StartsWith("=", StringComparison.Ordinal) ? ResolveChain(decl[1..], text, line, depth + 1).Type : ResolveTypeName(decl);
            if (type is null && BaseType(text, line) is { } bt)
            {
                var (mt, ms) = MemberType(bt, first, false);
                type = mt; isStatic = false; _ = ms;
            }
            if (type is null && Types.TryGetValue(first.Name, out var named)) { type = named; isStatic = true; }
            if (type is null && first.Name is "String" or "string") { type = typeof(string); isStatic = true; }
        }
        if (type is null) return (null, false);

        for (int i = next; i < parts.Count && type is not null; i++)
        {
            var seg = parts[i];
            if (seg.IsIndex) { type = ElementType(type); isStatic = false; continue; }
            var (mt, _) = MemberType(type, seg, isStatic);
            type = mt; isStatic = false;
        }
        return (type, isStatic);
    }

    private static (Type? Type, bool Static) MemberType(Type owner, Segment seg, bool staticAccess)
    {
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy | (staticAccess ? BindingFlags.Static : BindingFlags.Instance | BindingFlags.Static);
        Type? result = null;
        if (!seg.IsCall)
        {
            var p = owner.GetProperty(seg.Name, flags | BindingFlags.Public);
            if (p is not null) result = p.PropertyType;
            else if (owner.GetField(seg.Name, flags) is { } f) result = f.FieldType;
        }
        if (result is null)
        {
            var methods = owner.GetMethods(flags).Where(m => m.Name == seg.Name && (m.IsPublic || m.IsFamily)).ToList();
            var m = methods.FirstOrDefault();
            if (m is not null)
            {
                result = m.ReturnType;
                if (m.IsGenericMethodDefinition || result.ContainsGenericParameters)
                {
                    var ga = seg.Generic is not null ? ResolveTypeName(seg.Generic) : null;
                    if (result.IsGenericParameter) result = ga;
                    else if (ga is not null && result.IsGenericType && m.GetGenericArguments().Length == 1) result = SafeMakeGeneric(result.GetGenericTypeDefinition(), ga);
                    else result = result.ContainsGenericParameters ? null : result;
                }
            }
        }
        if (result is null) return (null, false);
        return (Unwrap(result), false);
    }

    private static Type? SafeMakeGeneric(Type def, params Type[] args)
    {
        try { return def.MakeGenericType(args); } catch (ArgumentException) { return null; }
    }

    private static Type Unwrap(Type t)
    {
        if (Nullable.GetUnderlyingType(t) is { } u) return u;
        if (t.IsByRef) return t.GetElementType()!;
        return t;
    }

    private static Type? ElementType(Type t)
    {
        if (t.IsArray) return t.GetElementType();
        if (t == typeof(string)) return typeof(char);
        foreach (var i in new[] { t }.Concat(t.GetInterfaces()))
            if (i.IsGenericType)
            {
                var def = i.GetGenericTypeDefinition();
                var args = i.GetGenericArguments();
                if (def == typeof(IList<>) || def == typeof(IReadOnlyList<>) || def == typeof(IEnumerable<>)) return args[0];
                if (def == typeof(IDictionary<,>) || def == typeof(IReadOnlyDictionary<,>)) return args[1];
            }
        return null;
    }

    // ---- type names -------------------------------------------------------------------------------

    /// <summary>"GameObject", "float", "List&lt;Vector3&gt;", "int[]", "Dictionary&lt;string, GameObject&gt;?" to a Type.</summary>
    public static Type? ResolveTypeName(string name)
    {
        name = name.Trim().TrimEnd('?');
        if (name.EndsWith("[]", StringComparison.Ordinal)) return ResolveTypeName(name[..^2]) is { } el ? el.MakeArrayType() : null;
        int lt = name.IndexOf('<');
        if (lt > 0 && name.EndsWith('>'))
        {
            string head = name[..lt];
            var args = SplitTop(name[(lt + 1)..^1]).Select(ResolveTypeName).ToList();
            if (args.Any(a => a is null)) return null;
            if (!Types.TryGetValue(head, out var def)) return null;
            var gen = def.IsGenericTypeDefinition ? def : null;
            if (gen is null)
            {
                // the name map holds one type per name: look for the generic version in the same namespace
                gen = def.Assembly.GetType(def.Namespace + "." + head + "`" + args.Count);
                if (gen is null) return null;
            }
            return SafeMakeGeneric(gen, args.Select(a => a!).ToArray());
        }
        int dot = name.LastIndexOf('.');
        if (dot >= 0) name = name[(dot + 1)..];
        if (Aliases.TryGetValue(name, out var alias)) name = alias;
        if (name == "var" || name.Length == 0) return null;
        if (Types.TryGetValue(name, out var t)) return t;
        // types declared by the game and by scripts (components and everything public)
        foreach (var comp in ComponentRegistry.Types) if (comp.Name == name) return comp;
        foreach (var w in WorldScriptRegistry.Types) if (w.Name == name) return w;
        return null;
    }

    private static List<string> SplitTop(string s)
    {
        var parts = new List<string>(); int depth = 0, start = 0;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '<') depth++;
            else if (s[i] == '>') depth--;
            else if (s[i] == ',' && depth == 0) { parts.Add(s[start..i]); start = i + 1; }
        }
        parts.Add(s[start..]);
        return parts;
    }

    // ---- the file being edited ----------------------------------------------------------------------

    /// <summary>The class the caret is in derives from this type (members of it can be used without a dot).</summary>
    private static Type? BaseType(string text, int line)
    {
        var lines = text.Split('\n');
        string upto = string.Join("\n", lines.Take(Math.Min(lines.Length, line + 1)));
        var matches = Regex.Matches(upto, @"\bclass\s+\w+\s*:\s*([\w\.]+)");
        if (matches.Count == 0) matches = Regex.Matches(text, @"\bclass\s+\w+\s*:\s*([\w\.]+)");
        return matches.Count == 0 ? null : ResolveTypeName(matches[^1].Groups[1].Value);
    }

    private static readonly HashSet<string> NotATypeWord = new() { "return", "else", "new", "throw", "in", "is", "as", "case", "goto", "using", "await", "yield" };

    /// <summary>Variables, parameters and fields declared in the file before the caret. The value is the type name, or "=expression" for <c>var x = expression;</c>.</summary>
    private static Dictionary<string, string> Declarations(string text, int line)
    {
        var lines = text.Split('\n');
        string upto = string.Join("\n", lines.Take(Math.Min(lines.Length, line + 1)));
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (Match m in Regex.Matches(upto, @"\bforeach\s*\(\s*(?:var|[\w<>,\.\[\]\?]+)\s+(\w+)\s+in\s+([^)]+)\)"))
            map[m.Groups[1].Value] = "=" + m.Groups[2].Value.Trim() + "[0]";
        foreach (Match m in Regex.Matches(upto, @"\bvar\s+(\w+)\s*=\s*([^;]+);"))
            map[m.Groups[1].Value] = "=" + m.Groups[2].Value.Trim();
        foreach (Match m in Regex.Matches(upto, @"(?<![\w\.])([A-Za-z_][\w]*(?:\.\w+)*(?:<[\w<>,\s\.\?\[\]]*>)?(?:\[\])?\??)\s+(\w+)\s*(?==|;|,|\)|\{|\sin\b)"))
        {
            string typeWord = m.Groups[1].Value, name = m.Groups[2].Value;
            if (typeWord == "var" || NotATypeWord.Contains(typeWord) || Keywords.Contains(name) || (Keywords.Contains(typeWord) && !Aliases.ContainsKey(typeWord))) continue;
            if (!map.ContainsKey(name)) map[name] = typeWord;
        }
        // "[0]" from foreach: the element type of the collection
        foreach (var k in map.Keys.ToList())
            if (map[k].EndsWith("[0]", StringComparison.Ordinal) && map[k].StartsWith("=", StringComparison.Ordinal)) { }
        return map;
    }

    // ---- members ------------------------------------------------------------------------------------

    private static List<CompletionItem> MemberItems(Type type, bool isStatic, bool includeProtected = false)
    {
        var items = new List<CompletionItem>();
        var flags = BindingFlags.Public | BindingFlags.FlattenHierarchy | (isStatic ? BindingFlags.Static : BindingFlags.Instance);
        if (includeProtected) flags |= BindingFlags.NonPublic;

        bool Visible(MethodBase? m) => m is not null && (m.IsPublic || (includeProtected && (m.IsFamily || m.IsFamilyOrAssembly)));

        foreach (var p in type.GetProperties(flags))
        {
            if (p.GetIndexParameters().Length > 0) continue;
            var acc = p.GetMethod ?? p.SetMethod;
            if (!Visible(acc)) continue;
            items.Add(new CompletionItem(p.Name, p.Name, CompletionKind.Property, Friendly(p.PropertyType) + (p.CanWrite && p.SetMethod!.IsPublic ? "" : "  (read only)")));
        }
        foreach (var f in type.GetFields(flags))
            if (!f.IsSpecialName && (f.IsPublic || (includeProtected && f.IsFamily)))
                items.Add(new CompletionItem(f.Name, f.Name, CompletionKind.Field, Friendly(f.FieldType)));

        foreach (var g in type.GetMethods(flags).Where(m => !m.IsSpecialName && Visible(m) && !m.Name.StartsWith('<')).GroupBy(m => m.Name))
        {
            if (type != typeof(object) && g.Key is "Equals" or "GetHashCode" or "GetType" or "ReferenceEquals" or "MemberwiseClone" or "Finalize") continue;
            var first = g.OrderBy(m => m.GetParameters().Length).First();
            string sig = $"{Friendly(first.ReturnType)} {g.Key}({string.Join(", ", first.GetParameters().Select(p => Friendly(p.ParameterType) + " " + p.Name))})";
            if (g.Count() > 1) sig += $"  (+{g.Count() - 1} overloads)";
            items.Add(new CompletionItem(g.Key, g.Key + "(", CompletionKind.Method, sig));
        }
        if (isStatic)
            foreach (var n in type.GetNestedTypes(BindingFlags.Public))
                items.Add(new CompletionItem(n.Name, n.Name, CompletionKind.Type, TypeKind(n)));
        return items;
    }

    private static string TypeKind(Type t) => t.IsEnum ? "enum" : t.IsInterface ? "interface" : t.IsValueType ? "struct" : t.IsAbstract && t.IsSealed ? "static class" : "class";

    public static string Friendly(Type t)
    {
        if (t == typeof(void)) return "void";
        if (t.IsByRef) return Friendly(t.GetElementType()!);
        if (t.IsArray) return Friendly(t.GetElementType()!) + "[]";
        if (Nullable.GetUnderlyingType(t) is { } u) return Friendly(u) + "?";
        foreach (var (alias, real) in Aliases) if (t.Name == real && t.Namespace == "System") return alias;
        if (t.IsGenericType)
        {
            string name = t.Name[..t.Name.IndexOf('`')];
            return name + "<" + string.Join(", ", t.GetGenericArguments().Select(Friendly)) + ">";
        }
        return t.Name;
    }
}
