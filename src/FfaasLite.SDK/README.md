# FfaasLite SDK

[![NuGet](https://img.shields.io/nuget/v/FfaasLite.SDK.svg)](https://www.nuget.org/packages/FfaasLite.SDK)

A lightweight .NET client for the FFaaS Lite feature flag service.

## Install
```powershell
Install-Package FfaasLite.SDK
```

Targets `net8.0` and depends only on BCL + `System.Net.WebSockets.Client`.

## Quick Start
```csharp
var client = new FlagClient("https://ffaas.example.com");
await client.StartSseAsync(); // optional: sync local cache via SSE

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
- Server-Sent Events listener (`/api/stream`) that updates the cache when flags change.
- Normalizes JSON payloads so that `EvalResult.Value` uses native .NET types.
- Helper extensions `AsBool`, `AsString`, and `AsNumber`.
- Disposable; call `await client.DisposeAsync()` on shutdown to stop the SSE listener.

## HttpClient Integration
You can supply a custom `HttpClient` instance when constructing the client:
```csharp
services.AddHttpClient<IFlagClient, FlagClient>(client =>
{
    client.BaseAddress = new Uri("https://ffaas.example.com");
    client.Timeout = TimeSpan.FromSeconds(5);
});
```

When you provide the `HttpClient`, the SDK keeps HTTP/1.1 semantics and adds no additional handlers. Configure retry policies, proxies, and headers as needed.

## SSE Considerations
- `StartSseAsync` is idempotent; call it once during application startup.
- The SSE loop currently ignores comment and retry fields. Consider wrapping it in your own resilience policy if the service is not HA.
- Cache invalidation depends on SSE events broadcasting full flag payloads.

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
