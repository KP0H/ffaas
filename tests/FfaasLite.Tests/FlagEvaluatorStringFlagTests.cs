using FfaasLite.Core.Flags;
using FfaasLite.Core.Models;

namespace FfaasLite.Tests
{
    public class FlagEvaluatorStringFlagTests
    {
        private static Flag MakeBoolFlag(bool @default, params TargetRule[] rules) => new()
        {
            Key = "bool-flag",
            Type = FlagType.Boolean,
            BoolValue = @default,
            Rules = [.. rules]
        };

        [Fact]
        public void Match_By_Attribute_Returns_Override_True()
        {
            var flag = MakeBoolFlag(false,
                new TargetRule("country", "eq", "NL", Priority: 1, BoolOverride: true));

            var ctx = new EvalContext(UserId: "u1", Attributes: new() { ["country"] = "NL" });

            var res = new FlagEvaluator().Evaluate(flag, ctx);

            Assert.True((bool)res.Value!);
            Assert.Equal("rule", res.Variant);
        }

        [Fact]
        public void No_Match_Returns_Default()
        {
            var flag = MakeBoolFlag(true,
                new TargetRule("country", "eq", "NL", Priority: 1, BoolOverride: false));

            var ctx = new EvalContext(UserId: "u1", Attributes: new() { ["country"] = "DE" });

            var res = new FlagEvaluator().Evaluate(flag, ctx);

            Assert.True((bool)res.Value!);
            Assert.Equal("default", res.Variant);
        }

        [Fact]
        public void Match_By_UserId_Special_Attribute()
        {
            string userId = Guid.NewGuid().ToString();

            var flag = MakeBoolFlag(false,
                new TargetRule("userId", "eq", userId, Priority: 1, BoolOverride: true));

            var ctx = new EvalContext(UserId: userId, Attributes: []);

            var res = new FlagEvaluator().Evaluate(flag, ctx);

            Assert.True((bool)res.Value!);
            Assert.Equal("rule", res.Variant);
        }

        [Fact]
        public void First_Matching_Rule_Wins_By_Priority()
        {
            var flag = MakeBoolFlag(false,
                new TargetRule("country", "eq", "NL", Priority: 2, BoolOverride: false),
                new TargetRule("country", "eq", "NL", Priority: 1, BoolOverride: true));

            var ctx = new EvalContext(UserId: "u1", Attributes: new() { ["country"] = "NL" });

            var res = new FlagEvaluator().Evaluate(flag, ctx);

            Assert.True((bool)res.Value!);
            Assert.Equal("rule", res.Variant);
        }

        [Fact]
        public void Empty_Rules_List_Always_Returns_Default()
        {
            var flag = MakeBoolFlag(true /* default */);

            var ctx = new EvalContext(UserId: "u1", Attributes: []);

            var res = new FlagEvaluator().Evaluate(flag, ctx);

            Assert.True((bool)res.Value!);
            Assert.Equal("default", res.Variant);
        }

        [Fact]
        public void Wrong_Override_Type_Is_Ignored_Falls_Back_To_Default()
        {
            var flag = MakeBoolFlag(false,
                new TargetRule("country", "eq", "NL", Priority: 1, StringOverride: "oops"));

            var ctx = new EvalContext(UserId: "u1", Attributes: new() { ["country"] = "NL" });

            var res = new FlagEvaluator().Evaluate(flag, ctx);

            Assert.False((bool)res.Value!);     // BoolOverride is null, take default
            Assert.Equal("rule", res.Variant);  // rule, but without override
        }

        [Fact]
        public void Segment_Operator_Finds_Value_In_List()
        {
            var flag = MakeBoolFlag(false,
                new TargetRule("segments", "segment", "beta,internal", Priority: 1, BoolOverride: true));

            var ctx = new EvalContext(UserId: "u1", Attributes: new() { ["segments"] = "pilot,beta" });

            var res = new FlagEvaluator().Evaluate(flag, ctx);

            Assert.True((bool)res.Value!);
            Assert.Equal("rule", res.Variant);
        }

        [Fact]
        public void Segment_Operator_Honours_Custom_Delimiter()
        {
            var flag = MakeBoolFlag(false,
                new TargetRule("segments", "segment", "qa|ops", Priority: 1, BoolOverride: true, SegmentDelimiter: "|"));

            var ctx = new EvalContext(UserId: "u1", Attributes: new() { ["segments"] = "dev|ops" });

            var res = new FlagEvaluator().Evaluate(flag, ctx);

            Assert.True((bool)res.Value!);
        }
    }
}
