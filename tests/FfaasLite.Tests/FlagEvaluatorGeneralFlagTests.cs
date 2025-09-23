using FfaasLite.Core.Flags;
using FfaasLite.Core.Models;
using System.Diagnostics.Metrics;

namespace FfaasLite.Tests
{
    public class FlagEvaluatorGeneralFlagTests
    {
        [Fact]
        public void Unknown_Operator_Is_Ignored()
        {
            var flag = new Flag
            {
                Key = "k",
                Type = FlagType.Boolean,
                BoolValue = true,
                Rules = new() { new TargetRule("country", "gt", "NL", 1, BoolOverride: false) }
            };
            var ctx = new EvalContext("u1", new() { ["country"] = "NL" });

            var res = new FlagEvaluator().Evaluate(flag, ctx);

            Assert.True((bool)res.Value!);
            Assert.Equal("default", res.Variant);
        }

        [Fact]
        public void Same_Priority_First_Wins()
        {
            var flag = new Flag
            {
                Key = "k",
                Type = FlagType.Boolean,
                BoolValue = false,
                Rules =
                [
                    new("country","eq","NL", 1, BoolOverride:false),
                    new("country","eq","NL", 1, BoolOverride:true)
                ]
            };
            var ctx = new EvalContext("u1", new() { ["country"] = "NL" });

            var res = new FlagEvaluator().Evaluate(flag, ctx);

            Assert.False((bool)res.Value!);
            Assert.Equal("rule", res.Variant);
        }


        [Fact]
        public void Priority_Null_Rules_Run_Last()
        {
            var flag = new Flag
            {
                Key = "k",
                Type = FlagType.Boolean,
                BoolValue = false,
                Rules =
                [
                    new TargetRule("country", "eq", "NL", Priority: null, BoolOverride: false),
                    new TargetRule("country", "eq", "NL", Priority: 1,    BoolOverride: true),
                ]
            };

            var ctx = new EvalContext(UserId: "u1", Attributes: new() { ["country"] = "NL" });

            var res = new FlagEvaluator().Evaluate(flag, ctx);

            Assert.True((bool)res.Value!);  // сработало правило с приоритетом 1
            Assert.Equal("rule", res.Variant);
        }

        [Fact]
        public void Attribute_Not_Present_Does_Not_Match()
        {
            var flag = new Flag
            {
                Key = "k",
                Type = FlagType.Boolean,
                BoolValue = true,
                Rules =
                [
                    new TargetRule("missingAttr", "eq", "X", Priority: 1, BoolOverride: false)
                ]
            };

            var ctx = new EvalContext(UserId: "u1", Attributes: new());

            var res = new FlagEvaluator().Evaluate(flag, ctx);

            Assert.True((bool)res.Value!);
            Assert.Equal("default", res.Variant);
        }

        [Theory]
        [InlineData("eq", "NL", "nl", true)]
        [InlineData("ne", "NL", "de", true)]
        [InlineData("ne", "NL", "NL", false)]
        [InlineData("contains", "oO", "foo", true)]
        public void Operators_Work_CaseInsensitive(string op, string ruleVal, string actual, bool shouldMatch)
        {
            var flag = new Flag
            {
                Key = "k",
                Type = FlagType.Boolean,
                BoolValue = false,
                Rules = [new("attr", op, ruleVal, Priority: 1, BoolOverride: true)]
            };

            var ctx = new EvalContext(UserId: "u1", Attributes: new() { ["attr"] = actual });

            var res = new FlagEvaluator().Evaluate(flag, ctx);

            if (shouldMatch)
            {
                Assert.True((bool)res.Value!);
                Assert.Equal("rule", res.Variant);
            }
            else
            {
                Assert.False((bool)res.Value!);
                Assert.Equal("default", res.Variant);
            }
        }
    }
}