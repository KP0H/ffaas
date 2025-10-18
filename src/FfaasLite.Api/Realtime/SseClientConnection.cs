using System.Threading;

using Microsoft.AspNetCore.Http;

namespace FfaasLite.Api.Realtime;

internal sealed class SseClientConnection
{
    public SseClientConnection(HttpResponse response)
    {
        Response = response;
    }

    public HttpResponse Response { get; }

    public SemaphoreSlim WriteLock { get; } = new(1, 1);

    public CancellationTokenSource? HeartbeatCts { get; set; }

    public CancellationTokenRegistration AbortRegistration { get; set; }
}
