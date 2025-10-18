using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FfaasLite.SDK;

public static class TypedFlagHelperBuilder
{
    public static string Generate(FlagClientTypedHelperOptions options)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));

        var keys = options.FlagKeys ?? Array.Empty<string>();
        if (keys.Count == 0)
        {
            throw new ArgumentException("FlagKeys must contain at least one flag key.", nameof(options));
        }

        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sanitizedMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            if (string.IsNullOrWhiteSpace(key)) continue;
            var sanitized = Sanitize(key);
            var original = sanitized;
            var index = 1;
            while (!unique.Add(sanitized))
            {
                sanitized = $"{original}_{index++}";
            }
            sanitizedMap[key] = sanitized;
        }

        if (sanitizedMap.Count == 0)
        {
            throw new ArgumentException("FlagKeys must contain at least one non-empty key.", nameof(options));
        }

        var sb = new StringBuilder();
        sb.AppendLine($"namespace {options.Namespace};");
        sb.AppendLine();
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using FfaasLite.Core.Models;");
        sb.AppendLine();
        sb.AppendLine($"public static class {options.ClassName}");
        sb.AppendLine("{");
        sb.AppendLine("    public static bool? AsBool(string key, IReadOnlyDictionary<string, Flag> cache)");
        sb.AppendLine("        => cache.TryGetValue(key, out var flag) ? flag.BoolValue : null;");
        sb.AppendLine();
        sb.AppendLine("    public static string? AsString(string key, IReadOnlyDictionary<string, Flag> cache)");
        sb.AppendLine("        => cache.TryGetValue(key, out var flag) ? flag.StringValue : null;");
        sb.AppendLine();
        sb.AppendLine("    public static double? AsNumber(string key, IReadOnlyDictionary<string, Flag> cache)");
        sb.AppendLine("        => cache.TryGetValue(key, out var flag) ? flag.NumberValue : null;");
        sb.AppendLine();

        foreach (var kvp in sanitizedMap)
        {
            var originalKey = kvp.Key;
            var safeName = kvp.Value;
            sb.AppendLine($"    public static bool? {safeName}Bool(IReadOnlyDictionary<string, Flag> cache) => AsBool(\"{originalKey}\", cache);");
            sb.AppendLine($"    public static string? {safeName}String(IReadOnlyDictionary<string, Flag> cache) => AsString(\"{originalKey}\", cache);");
            sb.AppendLine($"    public static double? {safeName}Number(IReadOnlyDictionary<string, Flag> cache) => AsNumber(\"{originalKey}\", cache);");
            sb.AppendLine();
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string Sanitize(string key)
    {
        var builder = new StringBuilder(key.Length);
        foreach (var ch in key)
        {
            builder.Append(char.IsLetterOrDigit(ch) ? ch : '_');
        }

        if (builder.Length == 0)
        {
            builder.Append('_');
        }

        if (!char.IsLetter(builder[0]) && builder[0] != '_')
        {
            builder.Insert(0, '_');
        }

        return builder.ToString();
    }
}
