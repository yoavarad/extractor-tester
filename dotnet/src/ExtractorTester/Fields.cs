using ExtractorTester.Config;
using ExtractorTester.Render;

namespace ExtractorTester.Fields;

/// <summary>Two keys flatten to the same dotted path (Python raises ValueError).</summary>
public sealed class FlattenCollisionException : Exception
{
    public FlattenCollisionException(string message) : base(message) { }
}

/// <summary>Metadata flattening and field classification (port of fields.py).</summary>
public static class FieldFlattener
{
    /// <summary>
    /// Flatten nested objects to dotted paths; lists, scalars and empty objects are values.
    /// Iterative (no recursion limit); output order matches Python's stack walk exactly.
    /// </summary>
    public static Dictionary<string, object?> Flatten(Dictionary<string, object?> obj)
    {
        var output = new Dictionary<string, object?>();
        var stack = new Stack<(string Prefix, Dictionary<string, object?> Current)>();
        stack.Push(("", obj));
        while (stack.Count > 0)
        {
            var (prefix, current) = stack.Pop();
            foreach (var (key, value) in current)
            {
                var path = prefix.Length > 0 ? $"{prefix}.{key}" : key;
                if (value is Dictionary<string, object?> { Count: > 0 } nested)
                {
                    stack.Push((path, nested));
                }
                else
                {
                    if (output.ContainsKey(path))
                    {
                        throw new FlattenCollisionException($"flatten path collision: {PyText.Repr(path)}");
                    }
                    output[path] = value;
                }
            }
        }
        return output;
    }

    private static bool Covers(List<string> entries, string path) =>
        entries.Any(e => path == e || path.StartsWith(e + ".", StringComparison.Ordinal));

    /// <summary>Classify a dotted path as "ignored", "generic" or "specific" (ignore wins).</summary>
    public static string Classify(string path, CompareConfig config)
    {
        if (Covers(config.IgnoreFields, path)) return "ignored";
        if (Covers(config.GenericFields, path)) return "generic";
        return "specific";
    }
}
