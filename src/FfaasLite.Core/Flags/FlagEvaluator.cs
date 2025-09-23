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

            foreach (var r in rules)
            {
                string? attrVal = null;

                if (string.Equals(r.Attribute, "userId", StringComparison.OrdinalIgnoreCase))
                {
                    attrVal = ctx.UserId;
                }
                else if (ctx.Attributes is not null)
                {
                    if (!ctx.Attributes.TryGetValue(r.Attribute, out attrVal))
                    {
                        var hit = ctx.Attributes.FirstOrDefault(kv => string.Equals(kv.Key, r.Attribute, StringComparison.OrdinalIgnoreCase));
                        if (!string.IsNullOrEmpty(hit.Key)) attrVal = hit.Value;
                    }
                }

                if (attrVal is not null && Compare(attrVal, r.Operator, r.Value))
                {
                    object? val = flag.Type switch
                    {
                        FlagType.Boolean => (object?)r.BoolOverride ?? flag.BoolValue,
                        FlagType.String => (object?)r.StringOverride ?? flag.StringValue,
                        FlagType.Number => (object?)r.NumberOverride ?? flag.NumberValue,
                        _ => null
                    };
                    return new EvalResult(flag.Key, val, flag.Type, Variant: "rule", AsOf: DateTimeOffset.UtcNow);
                }
            }


            object? defaultVal = flag.Type switch
            {
                FlagType.Boolean => flag.BoolValue,
                FlagType.String => flag.StringValue,
                FlagType.Number => flag.NumberValue,
                _ => null
            };
            return new EvalResult(flag.Key, defaultVal, flag.Type, Variant: "default", AsOf: DateTimeOffset.UtcNow);
        }


        private static bool Compare(string left, string op, string right)
            => op switch
            {
                "eq" => string.Equals(left, right, StringComparison.OrdinalIgnoreCase),
                "ne" => !string.Equals(left, right, StringComparison.OrdinalIgnoreCase),
                "contains" => left.Contains(right, StringComparison.OrdinalIgnoreCase),
                _ => false
            };
    }
}
