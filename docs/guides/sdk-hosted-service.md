# SDK Hosted Service & Background Refresh

This guide shows how to wire FlagClient into ASP.NET Core applications or worker services using FlagClientOptions, bootstrapping the cache, starting the realtime stream, and enabling background refresh.

## Minimal API Example

`csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(new FlagClientOptions
{
    BaseUrl = builder.Configuration["Ffaas:BaseUrl"] ?? "http://localhost:8080",
    ApiKey = builder.Configuration["Ffaas:ApiKey"],
    BootstrapOnStartup = true,
    StartRealtimeStream = true,
    BackgroundRefresh = new BackgroundRefreshOptions { Enabled = true, Interval = TimeSpan.FromSeconds(30) }
});

builder.Services.AddSingleton<IFlagClient>(sp =>
{
    var options = sp.GetRequiredService<FlagClientOptions>();
    return FlagClient.CreateAsync(options).GetAwaiter().GetResult();
});

builder.Services.AddHostedService<FlagClientLifecycleService>();

var app = builder.Build();
app.MapGet("/feature/{key}", async (string key, IFlagClient client) =>
{
    var result = await client.EvaluateAsync(key, new EvalContext());
    return Results.Ok(result);
});

app.Run();
`

The hosted service coordinates shutdown and optional background refresh:

`csharp
public sealed class FlagClientLifecycleService : IHostedService, IAsyncDisposable
{
    private readonly IFlagClient _client;

    public FlagClientLifecycleService(IFlagClient client)
    {
        _client = client;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
        => await _client.DisposeAsync();

    public ValueTask DisposeAsync() => _client.DisposeAsync();
}
`

## Worker Service Example

`csharp
public sealed class FlagPollingWorker : BackgroundService
{
    private readonly IFlagClient _client;
    private readonly ILogger<FlagPollingWorker> _logger;

    public FlagPollingWorker(IFlagClient client, ILogger<FlagPollingWorker> logger)
    {
        _client = client;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var result = await _client.EvaluateAsync("beta", new EvalContext(UserId: "worker"), stoppingToken);
            _logger.LogInformation("beta = {Value}", result.Value);
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }
}
`

Register the worker in Program.cs:

`csharp
public static async Task Main(string[] args)
{
    var host = Host.CreateDefaultBuilder(args)
        .ConfigureServices((context, services) =>
        {
            services.AddSingleton(new FlagClientOptions
            {
                BaseUrl = context.Configuration["Ffaas:BaseUrl"] ?? "http://localhost:8080",
                ApiKey = context.Configuration["Ffaas:ApiKey"],
                BootstrapOnStartup = true,
                StartRealtimeStream = false,
                BackgroundRefresh = new BackgroundRefreshOptions { Enabled = true, Interval = TimeSpan.FromSeconds(60) }
            });

            services.AddSingleton<IFlagClient>(sp =>
            {
                var options = sp.GetRequiredService<FlagClientOptions>();
                return FlagClient.CreateAsync(options).GetAwaiter().GetResult();
            });

            services.AddHostedService<FlagClientLifecycleService>();
            services.AddHostedService<FlagPollingWorker>();
        })
        .Build();

    await host.RunAsync();
}
`

> **Note:** For production scenarios prefer async-aware factories or custom hosted services that handle retries during startup (Polly wrappers can be supplied via FlagClientOptions.SendAsyncWrapper).

## Typed Helper Generation

After the cache is primed you can scaffold helpers:

`csharp
var helperCode = flagClient.GenerateTypedHelper(new FlagClientTypedHelperOptions
{
    Namespace = "MyApp.Flags",
    ClassName = "FeatureFlags"
});
File.WriteAllText("FeatureFlags.g.cs", helperCode);
`

Combine the generated static class with dependency injection to access strongly-typed accessors throughout your application.
