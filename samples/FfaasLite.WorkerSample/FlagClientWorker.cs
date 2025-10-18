using FfaasLite.Core.Flags;
using FfaasLite.SDK;
using Microsoft.Extensions.Options;
using System.Linq;

namespace FfaasLite.WorkerSample;

public sealed class FlagClientWorker : BackgroundService
{
    private readonly ILogger<FlagClientWorker> _logger;
    private readonly IOptionsMonitor<FlagClientSettings> _settings;
    private FlagClient? _client;

    public FlagClientWorker(
        ILogger<FlagClientWorker> logger,
        IOptionsMonitor<FlagClientSettings> settings)
    {
        _logger = logger;
        _settings = settings;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = _settings.CurrentValue;
        var options = BuildClientOptions(settings);

        _client = await FlagClient.CreateAsync(options, stoppingToken);

        _logger.LogInformation(
            "Connected to FFaaS at {BaseUrl}. Realtime: {Realtime} (heartbeat {HeartbeatTimeout}s), background refresh every {RefreshInterval}s.",
            options.BaseUrl,
            options.StartRealtimeStream,
            options.StreamOptions?.HeartbeatTimeout.TotalSeconds ?? 0,
            options.BackgroundRefresh.Enabled ? options.BackgroundRefresh.Interval.TotalSeconds : 0);

        LogTypedHelper(settings);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var context = BuildEvalContext(settings);
                var checkout = await _client.EvaluateAsync(settings.SampleFlagKey, context, stoppingToken);
                _logger.LogInformation(
                    "Flag {Key} => {Value} (variant {Variant}, as of {AsOf})",
                    checkout.Key,
                    checkout.Value ?? "<null>",
                    checkout.Variant,
                    checkout.AsOf);

                var rateLimit = await _client.EvaluateAsync("rate-limit", context, stoppingToken);
                _logger.LogInformation(
                    "Rate limit => {Value} (variant {Variant}, as of {AsOf})",
                    rateLimit.Value ?? "<null>",
                    rateLimit.Variant,
                    rateLimit.AsOf);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to evaluate flag {FlagKey}", settings.SampleFlagKey);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, settings.PollIntervalSeconds)), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
            _client = null;
        }

        await base.StopAsync(cancellationToken);
    }

    private void LogTypedHelper(FlagClientSettings settings)
    {
        if (_client is null || !_logger.IsEnabled(LogLevel.Debug))
        {
            return;
        }

        var helper = _client.GenerateTypedHelper(new FlagClientTypedHelperOptions
        {
            Namespace = "FfaasLite.WorkerSample.Generated",
            ClassName = "FeatureFlags",
            FlagKeys = settings.TypedHelperKeys is { Length: > 0 } keys
                ? keys
                : _client.SnapshotCachedFlags().Keys.ToArray()
        });

        _logger.LogDebug("Generated typed helper snippet:{NewLine}{Helper}", Environment.NewLine, helper);
    }

    private static EvalContext BuildEvalContext(FlagClientSettings settings)
    {
        var attributes = settings.Attributes?.Count > 0
            ? new Dictionary<string, string>(settings.Attributes, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        return new EvalContext(
            UserId: settings.SampleUserId,
            Attributes: attributes);
    }

    private static FlagClientOptions BuildClientOptions(FlagClientSettings settings)
    {
        var options = new FlagClientOptions
        {
            BaseUrl = settings.BaseUrl,
            ApiKey = settings.ApiKey,
            BootstrapOnStartup = true,
            StartRealtimeStream = settings.UseRealtime,
            StreamOptions = new FlagStreamOptions
            {
                HeartbeatTimeout = TimeSpan.FromSeconds(Math.Max(5, settings.HeartbeatTimeoutSeconds)),
                InitialRetryDelay = TimeSpan.FromMilliseconds(settings.InitialRetryDelayMs),
                MaxRetryDelay = TimeSpan.FromSeconds(Math.Max(5, settings.MaxRetryDelaySeconds)),
                BackoffFactor = Math.Max(1.2, settings.RetryBackoffFactor),
                BootstrapSnapshot = true
            },
            BackgroundRefresh = new BackgroundRefreshOptions
            {
                Enabled = settings.BackgroundRefreshIntervalSeconds > 0,
                Interval = TimeSpan.FromSeconds(Math.Max(5, settings.BackgroundRefreshIntervalSeconds))
            }
        };

        if (settings.RequestTimeoutSeconds > 0)
        {
            options.RequestTimeout = TimeSpan.FromSeconds(settings.RequestTimeoutSeconds);
        }

        options.OnBackgroundRefreshError = ex =>
        {
            Console.Error.WriteLine($"[{DateTimeOffset.UtcNow:u}] Background refresh failed: {ex.Message}");
        };

        return options;
    }
}

public sealed class FlagClientSettings
{
    public string BaseUrl { get; set; } = "http://localhost:8080";
    public string ApiKey { get; set; } = "dev-editor-token";
    public bool UseRealtime { get; set; } = true;
    public int HeartbeatTimeoutSeconds { get; set; } = 20;
    public int InitialRetryDelayMs { get; set; } = 500;
    public int MaxRetryDelaySeconds { get; set; } = 5;
    public double RetryBackoffFactor { get; set; } = 2;
    public int BackgroundRefreshIntervalSeconds { get; set; } = 120;
    public int RequestTimeoutSeconds { get; set; } = 10;
    public string SampleFlagKey { get; set; } = "checkout";
    public string SampleUserId { get; set; } = "worker-sample-user";
    public int PollIntervalSeconds { get; set; } = 30;
    public Dictionary<string, string> Attributes { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["country"] = "US",
        ["appVersion"] = "1.0.0",
        ["segments"] = "vip"
    };
    public string[]? TypedHelperKeys { get; set; }
}
