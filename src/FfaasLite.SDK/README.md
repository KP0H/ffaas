# FfaasLite SDK

[![NuGet](https://img.shields.io/nuget/v/FfaasLite.SDK.svg)](https://www.nuget.org/packages/FfaasLite.SDK)

A lightweight .NET client for the FFaaS Lite feature flag service.

## Install
```powershell
Install-Package FfaasLite.SDK
```

Targets `net8.0` and depends only on BCL assemblies (no WebSocket client required).

## Quick Start
```csharp
var client = new FlagClient("https://ffaas.example.com");
await client.StartRealtimeAsync(new FlagStreamOptions
{
    HeartbeatTimeout = TimeSpan.FromSeconds(30),
    InitialRetryDelay = TimeSpan.FromSeconds(1)
}); // optional but recommended for realtime cache updates

var context = new EvalContext(
    UserId: "user-42",
    Attributes: new() { ["country"] = "NL", ["subscription"] = "pro" }
);

var result = await client.EvaluateAsync("new-ui", context);
if (result.AsBool() == true)
{
    // enable experiment
}
```

`EvaluateAsync` will fall back to the HTTP API when the local cache does not yet contain the requested flag.

## Features
- Local in-memory cache keyed by flag `Key`.
- Structured SSE listener (`/api/stream`) with heartbeats and configurable reconnect backoff.
- Snapshot helpers (`RefreshSnapshotAsync`, `TryGetCachedFlag`, `SnapshotCachedFlags`) for manual cache control.
- Normalizes JSON payloads so that `EvalResult.Value` uses native .NET types.
- Helper extensions `AsBool`, `AsString`, and `AsNumber`.
- Disposable; call `await client.StopRealtimeAsync()` or `await client.DisposeAsync()` during application shutdown.

## HttpClient Integration
You can supply a custom `HttpClient` instance when constructing the client:
```csharp
services.AddHttpClient<IFlagClient, FlagClient>(client =>
{
    client.BaseAddress = new Uri("https://ffaas.example.com");
    client.Timeout = Timeout.InfiniteTimeSpan; // SSE responses stay open indefinitely
});
```

When you provide the `HttpClient`, the SDK enforces HTTP/1.1 semantics, disables proxy usage, and leaves further handlers/policies up to you.

## Realtime Considerations
- `StartRealtimeAsync` is idempotent; call it once during application startup (pass `FlagStreamOptions` to tweak backoff and heartbeat thresholds).
- Server-supplied `retry` hints are honoured; failed connections fall back to exponential backoff capped by `MaxRetryDelay`.
- Heartbeat gaps trigger a reconnect when they exceed `HeartbeatTimeout`. Increase the threshold if the stream traverses slow proxies.
- Call `RefreshSnapshotAsync` if you suspect missed events, or wire a periodic snapshot as a safety net.

## Thread Safety
- Evaluations against the local cache are thread-safe (uses `ConcurrentDictionary`).
- SDK methods are safe for concurrent use across requests.

## Error Handling
- `EvaluateAsync` throws for non-success HTTP responses; wrap in try/catch if you want soft-fail behavior.
- SSE loop suppresses `OperationCanceledException` and will stop on other exceptions (logged by the host app if observed).

## Versioning
The package follows semantic versioning. Changes to the HTTP contract or `EvalResult` shape will trigger a major version bump. See the project roadmap for upcoming milestones.

## Contributing
Issues and pull requests are welcome in the main repository. Please accompany changes with tests where possible.
