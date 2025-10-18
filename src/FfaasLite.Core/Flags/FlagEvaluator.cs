using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

using FfaasLite.Core.Models;

namespace FfaasLite.Core.Flags
{
    public record EvalContext(string? UserId = null, Dictionary<string, string>? Attributes = null);

    public record EvalResult(string Key, object? Value, FlagType Type, string Variant, DateTimeOffset AsOf);

    public class FlagEvaluator : IFlagEvaluator
    {

        public EvalResult Evaluate(Flag flag, EvalContext ctx)
        {
            var rules = flag.Rules
                .OrderBy(r => r.Priority ?? int.MaxValue)
                .ToList();

            var now = DateTimeOffset.UtcNow;

            foreach (var r in rules)
            {
                if (IsMatch(flag, r, ctx))
                {
                    object? value = flag.Type switch
                    {
                        FlagType.Boolean => r.BoolOverride ?? flag.BoolValue,
                        FlagType.String => r.StringOverride ?? flag.StringValue,
                        FlagType.Number => r.NumberOverride ?? flag.NumberValue,
                        _ => null
                    };
                    return new EvalResult(flag.Key, value, flag.Type, Variant: "rule", AsOf: now);
                }
            }


            object? defaultVal = flag.Type switch
            {
                FlagType.Boolean => flag.BoolValue,
                FlagType.String => flag.StringValue,
                FlagType.Number => flag.NumberValue,
                _ => null
            };
            return new EvalResult(flag.Key, defaultVal, flag.Type, Variant: "default", AsOf: now);
        }


        private static bool IsMatch(Flag flag, TargetRule rule, EvalContext ctx)
        {
            var op = rule.Operator?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(op))
            {
                return false;
            }

            if (op == "percentage")
            {
                return EvaluatePercentage(flag, rule, ctx);
            }

            return EvaluateAttributeRule(rule, ctx, op);
        }

        private static bool EvaluateAttributeRule(TargetRule rule, EvalContext ctx, string op)
        {
            var left = ResolveAttributeValue(ctx, rule.Attribute);
            if (left is null)
            {
                return false;
            }

            var right = rule.Value ?? string.Empty;
            return Compare(left, op, right, rule);
        }

        private static string? ResolveAttributeValue(EvalContext ctx, string attribute)
        {
            if (string.IsNullOrWhiteSpace(attribute))
            {
                return null;
            }

            if (string.Equals(attribute, "userId", StringComparison.OrdinalIgnoreCase))
            {
                return ctx.UserId;
            }

            if (ctx.Attributes is null)
            {
                return null;
            }

            if (ctx.Attributes.TryGetValue(attribute, out var value))
            {
                return value;
            }

            var hit = ctx.Attributes.FirstOrDefault(kv => string.Equals(kv.Key, attribute, StringComparison.OrdinalIgnoreCase));
            return string.IsNullOrWhiteSpace(hit.Key) ? null : hit.Value;
        }

        private static bool Compare(string left, string op, string right, TargetRule rule)
        {
            switch (op)
            {
                case "eq":
                    return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
                case "ne":
                    return !string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
                case "contains":
                    return right.Length == 0
                        ? false
                        : left.Contains(right, StringComparison.OrdinalIgnoreCase);
                case "gt":
                    return TryParseNumber(left, out var leftNumber) &&
                           TryParseNumber(right, out var rightNumber) &&
                           leftNumber > rightNumber;
                case "lt":
                    return TryParseNumber(left, out leftNumber) &&
                           TryParseNumber(right, out rightNumber) &&
                           leftNumber < rightNumber;
                case "gte":
                case "ge":
                    return TryParseNumber(left, out leftNumber) &&
                           TryParseNumber(right, out rightNumber) &&
                           leftNumber >= rightNumber;
                case "lte":
                case "le":
                    return TryParseNumber(left, out leftNumber) &&
                           TryParseNumber(right, out rightNumber) &&
                           leftNumber <= rightNumber;
                case "regex":
                    return TryRegexMatch(left, right);
                case "segment":
                case "in":
                    return EvaluateSegment(left, right, rule.SegmentDelimiter);
                default:
                    return false;
            }
        }

        private static bool EvaluateSegment(string left, string right, string? delimiter)
        {
            var ruleSegments = SplitValues(right, delimiter);
            if (ruleSegments.Count == 0)
            {
                return false;
            }

            var attributeSegments = SplitValues(left, delimiter);
            if (attributeSegments.Count == 0)
            {
                attributeSegments = new HashSet<string>(new[] { left }, StringComparer.OrdinalIgnoreCase);
            }

            return attributeSegments.Overlaps(ruleSegments);
        }

        private static HashSet<string> SplitValues(string input, string? delimiter)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            string[] tokens;
            if (string.IsNullOrEmpty(delimiter))
            {
                tokens = input.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }
            else if (delimiter.Length == 1)
            {
                tokens = input.Split(delimiter[0], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }
            else
            {
                tokens = input.Split(delimiter, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }

            return new HashSet<string>(tokens.Where(t => t.Length > 0), StringComparer.OrdinalIgnoreCase);
        }

        private static bool TryRegexMatch(string input, string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                return false;
            }

            try
            {
                return Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private static bool TryParseNumber(string value, out double number)
            => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number);

        private static bool EvaluatePercentage(Flag flag, TargetRule rule, EvalContext ctx)
        {
            var percentage = rule.Percentage ?? ParsePercentage(rule.Value);
            if (percentage is null || percentage <= 0)
            {
                return false;
            }
            if (percentage >= 100)
            {
                return true;
            }

            var basisAttribute = !string.IsNullOrWhiteSpace(rule.PercentageAttribute)
                ? rule.PercentageAttribute
                : string.IsNullOrWhiteSpace(rule.Attribute) ? null : rule.Attribute;

            var basis = basisAttribute is not null
                ? ResolveAttributeValue(ctx, basisAttribute)
                : null;

            if (string.IsNullOrWhiteSpace(basis))
            {
                basis = ctx.UserId;
            }

            if (string.IsNullOrWhiteSpace(basis))
            {
                return false;
            }

            var salt = rule.Value ?? flag.Key;
            var bucket = ComputeBucket($"{flag.Key}:{salt}:{basis}");
            return bucket < percentage.Value;
        }

        private static double ComputeBucket(string input)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
            var sample = BitConverter.ToUInt32(bytes, 0);
            return sample / (double)uint.MaxValue * 100d;
        }

        private static double? ParsePercentage(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
        }
    }
}
