using System.Diagnostics.Metrics;

using FfaasLite.Core.Flags;
using FfaasLite.Core.Models;

namespace FfaasLite.Tests
{
    public class FlagEvaluatorBooleanFlagTests
    {
        private static Flag MakeStringFlag(string @default, params TargetRule[] rules) => new()
        {
            Key = "string-flag",
            Type = FlagType.String,
            StringValue = @default,
            Rules = [.. rules]
        };

        [Fact]
        public void String_Override_By_Country()
        {
            var flag = MakeStringFlag("v1",
                new TargetRule("country", "eq", "NL", Priority: 1, StringOverride: "v2"));

            var ctx = new EvalContext(UserId: "u1", Attributes: new() { ["country"] = "NL" });

            var res = new FlagEvaluator().Evaluate(flag, ctx);

            Assert.Equal("v2", (string)res.Value!);
            Assert.Equal("rule", res.Variant);
        }

        [Fact]
        public void Contains_Operator_Is_CaseInsensitive()
        {
            var flag = MakeStringFlag("off",
                new TargetRule("email", "contains", "@corp.com", Priority: 1, StringOverride: "on"));

            var ctx = new EvalContext(UserId: "u1", Attributes: new() { ["email"] = "User@Corp.Com" });

            var res = new FlagEvaluator().Evaluate(flag, ctx);

            Assert.Equal("on", (string)res.Value!);
            Assert.Equal("rule", res.Variant);
        }

        [Fact]
        public void Ne_Operator_Mismtach_Falls_Back_Default()
        {
            var flag = MakeStringFlag("v1",
                new TargetRule("country", "ne", "NL", Priority: 1, StringOverride: "v2"));

            var ctx = new EvalContext(UserId: "u1", Attributes: new() { ["country"] = "NL" });

            var res = new FlagEvaluator().Evaluate(flag, ctx);

            Assert.Equal("v1", (string)res.Value!);
            Assert.Equal("default", res.Variant);
        }
    }
}
