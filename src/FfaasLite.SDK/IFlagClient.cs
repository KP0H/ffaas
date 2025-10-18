using System.Collections.Generic;

using FfaasLite.Core.Flags;
using FfaasLite.Core.Models;

namespace FfaasLite.SDK;

public interface IFlagClient : IAsyncDisposable
{
    Task<EvalResult> EvaluateAsync(string key, EvalContext ctx, CancellationToken ct = default);

    Task StartRealtimeAsync(FlagStreamOptions? options = null, CancellationToken ct = default);

    Task StopRealtimeAsync();

    Task RefreshSnapshotAsync(CancellationToken ct = default);

    bool TryGetCachedFlag(string key, out Flag? flag);

    IReadOnlyDictionary<string, Flag> SnapshotCachedFlags();
}
