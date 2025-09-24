using FfaasLite.Core.Flags;

namespace FfaasLite.SDK
{
    public interface IFlagClient : IAsyncDisposable
    {
        Task<EvalResult> EvaluateAsync(string key, EvalContext ctx, CancellationToken ct = default);

        Task StartSseAsync(CancellationToken ct = default);
    }
}
