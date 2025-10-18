using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace FfaasLite.SDK;

public sealed class FlagClientOptions
{
    public string BaseUrl { get; set; } = "http://localhost:8080";

    public HttpClient? HttpClient { get; set; }

    public string? ApiKey { get; set; }

    public bool BootstrapOnStartup { get; set; } = true;

    public bool StartRealtimeStream { get; set; } = true;

    public FlagStreamOptions? StreamOptions { get; set; }

    public BackgroundRefreshOptions BackgroundRefresh { get; set; } = new();

    public Action<HttpClient>? ConfigureHttpClient { get; set; }

    public Action<Exception>? OnBackgroundRefreshError { get; set; }

    public TimeSpan? RequestTimeout { get; set; }

    public Func<Func<HttpRequestMessage, HttpCompletionOption, CancellationToken, Task<HttpResponseMessage>>, HttpRequestMessage, HttpCompletionOption, CancellationToken, Task<HttpResponseMessage>>? SendAsyncWrapper { get; set; }
}

public sealed class BackgroundRefreshOptions
{
    public bool Enabled { get; set; }

    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(2);
}
