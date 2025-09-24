using FfaasLite.Core.Flags;
using FfaasLite.Core.Models;
using System.Diagnostics.Metrics;

namespace FfaasLite.Tests
{
    public class FlagEvaluatorNumberFlagTests
    {
        private static Flag MakeNumberFlag(double @default, params TargetRule[] rules) => new()
        {
            Key = "number-flag",
            Type = FlagType.Number,
            NumberValue = @default,
            Rules = [.. rules]
        };

        [Fact]
        public void Number_Override_By_User_Segment()
        {
            var flag = MakeNumberFlag(10,
                new TargetRule("tier", "eq", "pro", Priority: 1, NumberOverride: 25));

            var ctx = new EvalContext(UserId: "42", Attributes: new() { ["tier"] = "pro" });

            var res = new FlagEvaluator().Evaluate(flag, ctx);

            Assert.Equal(25d, (double)res.Value!, precision: 6);
            Assert.Equal("rule", res.Variant);
        }

        [Fact]
        public void No_Attributes_In_Context_Returns_Default()
        {
            var flag = MakeNumberFlag(7,
                new TargetRule("country", "eq", "NL", Priority: 1, NumberOverride: 9));

            var ctx = new EvalContext(UserId: "42", Attributes: null);

            var res = new FlagEvaluator().Evaluate(flag, ctx);

            Assert.Equal(7d, (double)res.Value!, precision: 6);
            Assert.Equal("default", res.Variant);
        }
    }
}