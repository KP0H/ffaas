using System.Security.Cryptography;
using System.Text;
using FfaasLite.Core.Flags;
using FfaasLite.Core.Models;

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

        [Fact]
        public void GreaterThan_Operator_Applies_NumberOverride()
        {
            var flag = MakeNumberFlag(5,
                new TargetRule("score", "gt", "75", Priority: 1, NumberOverride: 99));

            var ctx = new EvalContext(UserId: "42", Attributes: new() { ["score"] = "80" });

            var res = new FlagEvaluator().Evaluate(flag, ctx);

            Assert.Equal(99d, (double)res.Value!, precision: 6);
            Assert.Equal("rule", res.Variant);
        }

        [Fact]
        public void LessThan_Operator_Does_Not_Match_When_NotSatisfied()
        {
            var flag = MakeNumberFlag(5,
                new TargetRule("score", "lt", "20", Priority: 1, NumberOverride: 0));

            var ctx = new EvalContext(UserId: "42", Attributes: new() { ["score"] = "42" });

            var res = new FlagEvaluator().Evaluate(flag, ctx);

            Assert.Equal(5d, (double)res.Value!, precision: 6);
            Assert.Equal("default", res.Variant);
        }

        [Fact]
        public void Percentage_Rollout_Can_Succeed()
        {
            var rule = new TargetRule(
                Attribute: "userId",
                Operator: "percentage",
                Value: "beta",
                Priority: 1,
                NumberOverride: 77,
                Percentage: 100);

            var flag = MakeNumberFlag(5, rule);
            var ctx = new EvalContext(UserId: "user-123", Attributes: new());

            var res = new FlagEvaluator().Evaluate(flag, ctx);

            Assert.Equal(77d, (double)res.Value!, precision: 6);
            Assert.Equal("rule", res.Variant);
        }

        [Fact]
        public void Percentage_Rollout_Uses_Attribute_WhenProvided()
        {
            const string salt = "beta";
            const string basis = "session-a";
            var bucket = ComputeBucket("number-flag", salt, basis);
            var percentage = Math.Min(99.9, bucket + 0.5);

            var rule = new TargetRule(
                Attribute: "",
                Operator: "percentage",
                Value: salt,
                Priority: 1,
                NumberOverride: 33,
                Percentage: percentage,
                PercentageAttribute: "session");

            var flag = MakeNumberFlag(5, rule);
            var ctx = new EvalContext(UserId: "any", Attributes: new() { ["session"] = basis });

            var res = new FlagEvaluator().Evaluate(flag, ctx);

            Assert.Equal(33d, (double)res.Value!, precision: 6);
            Assert.Equal("rule", res.Variant);
        }

        [Fact]
        public void Percentage_Rollout_Can_Fail_When_Bucket_Above_Threshold()
        {
            const string salt = "beta";
            const string basis = "session-b";
            var bucket = ComputeBucket("number-flag", salt, basis);
            var percentage = Math.Max(0, bucket - 1);

            var rule = new TargetRule(
                Attribute: "",
                Operator: "percentage",
                Value: salt,
                Priority: 1,
                NumberOverride: 33,
                Percentage: percentage,
                PercentageAttribute: "session");

            var flag = MakeNumberFlag(5, rule);
            var ctx = new EvalContext(UserId: "any", Attributes: new() { ["session"] = basis });

            var res = new FlagEvaluator().Evaluate(flag, ctx);

            Assert.Equal(5d, (double)res.Value!, precision: 6);
            Assert.Equal("default", res.Variant);
        }

        private static double ComputeBucket(string flagKey, string salt, string basis)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes($"{flagKey}:{salt}:{basis}"));
            var sample = BitConverter.ToUInt32(bytes, 0);
            return sample / (double)uint.MaxValue * 100d;
        }
    }
}
