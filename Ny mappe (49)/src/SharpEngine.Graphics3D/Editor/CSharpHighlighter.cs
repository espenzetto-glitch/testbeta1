namespace SharpEngine.Graphics3D.Editor;

public enum Token : byte { Plain, Keyword, Type, String, Comment, Number, Punctuation }

/// <summary>Cheap C# syntax colouring: one pass per line, remembering whether a /* comment is still open at the end of it.</summary>
public static class CSharpHighlighter
{
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "abstract","as","base","bool","break","byte","case","catch","char","checked","class","const","continue","decimal","default",
        "delegate","do","double","else","enum","event","explicit","extern","false","finally","fixed","float","for","foreach","goto",
        "if","implicit","in","int","interface","internal","is","lock","long","namespace","new","null","object","operator","out",
        "override","params","private","protected","public","readonly","ref","return","sbyte","sealed","short","sizeof","stackalloc",
        "static","string","struct","switch","this","throw","true","try","typeof","uint","ulong","unchecked","unsafe","ushort","using",
        "var","virtual","void","volatile","while","get","set","init","record","async","await","yield","when","where","with",
        // newer / contextual keywords, C# 15 preview: union, closed, extension, safe, field; labeled break/continue use plain identifiers
        "union","closed","extension","safe","field","required","scoped","file","partial","global","dynamic","nameof","not","and","or","nint","nuint"
    };

    /// <summary>Fills <paramref name="kinds"/> (one per character of the line). Returns the open-block-comment state for the next line.</summary>
    public static bool Classify(string line, bool inBlockComment, Token[] kinds)
    {
        int n = line.Length, i = 0;
        for (int k = 0; k < n && k < kinds.Length; k++) kinds[k] = Token.Plain;

        while (i < n)
        {
            char c = line[i];
            if (inBlockComment)
            {
                int end = line.IndexOf("*/", i, StringComparison.Ordinal);
                int stop = end < 0 ? n : end + 2;
                Fill(kinds, i, stop, Token.Comment);
                i = stop;
                if (end >= 0) inBlockComment = false;
            }
            else if (c == '/' && i + 1 < n && line[i + 1] == '/')
            {
                Fill(kinds, i, n, Token.Comment);
                i = n;
            }
            else if (c == '/' && i + 1 < n && line[i + 1] == '*')
            {
                inBlockComment = true;
                Fill(kinds, i, i + 2, Token.Comment);
                i += 2;
            }
            else if (c == '"' || c == '\'' || (c == '$' && i + 1 < n && line[i + 1] == '"') || (c == '@' && i + 1 < n && line[i + 1] == '"'))
            {
                int start = i;
                if (c == '$' || c == '@') i++;
                char quote = line[i++];
                bool verbatim = c == '@';
                while (i < n)
                {
                    if (!verbatim && line[i] == '\\') { i += 2; continue; }
                    if (line[i] == quote) { i++; break; }
                    i++;
                }
                Fill(kinds, start, Math.Min(i, n), Token.String);
                i = Math.Min(i, n);
            }
            else if (char.IsDigit(c) || (c == '.' && i + 1 < n && char.IsDigit(line[i + 1])))
            {
                int start = i;
                while (i < n && (char.IsLetterOrDigit(line[i]) || line[i] == '.' || line[i] == '_')) i++;
                Fill(kinds, start, i, Token.Number);
            }
            else if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < n && (char.IsLetterOrDigit(line[i]) || line[i] == '_')) i++;
                string word = line[start..i];
                var kind = Keywords.Contains(word) ? Token.Keyword : char.IsUpper(word[0]) ? Token.Type : Token.Plain;
                Fill(kinds, start, i, kind);
            }
            else
            {
                if ("{}()[];,.<>+-*/%=!&|^~?:".IndexOf(c) >= 0) kinds[i] = Token.Punctuation;
                i++;
            }
        }
        return inBlockComment;
    }

    private static void Fill(Token[] kinds, int from, int to, Token t)
    {
        for (int k = from; k < to && k < kinds.Length; k++) kinds[k] = t;
    }
}
