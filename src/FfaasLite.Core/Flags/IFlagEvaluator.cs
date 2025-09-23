using FfaasLite.Core.Models;

namespace FfaasLite.Core.Flags
{
    public interface IFlagEvaluator
    {
        EvalResult Evaluate(Flag flag, EvalContext ctx);
    }
}
