using FfaasLite.Core.Flags;
using FfaasLite.Core.Models;
using FfaasLite.SDK;

namespace FfaasLite.Tests
{
    public class SdkEvalResultExtensionsTests
    {
        [Fact]
        public void AsBool_Returns_Value_For_Boolean()
        {
            var res = new EvalResult("k", true, FlagType.Boolean, "rule", DateTimeOffset.UtcNow);
            Assert.True(res.AsBool());
            Assert.Null(res.AsString());
            Assert.Null(res.AsNumber());
        }

        [Fact]
        public void AsString_Returns_Value_For_String()
        {
            var res = new EvalResult("k", "hello", FlagType.String, "default", DateTimeOffset.UtcNow);
            Assert.Equal("hello", res.AsString());
            Assert.Null(res.AsBool());
            Assert.Null(res.AsNumber());
        }

        [Fact]
        public void AsNumber_Returns_Value_For_Number()
        {
            var res = new EvalResult("k", 42d, FlagType.Number, "rule", DateTimeOffset.UtcNow);
            Assert.Equal(42d, res.AsNumber());
            Assert.Null(res.AsBool());
            Assert.Null(res.AsString());
        }
    }
}
