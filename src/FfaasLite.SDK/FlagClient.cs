using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using FfaasLite.Core.Flags;
using FfaasLite.Core.Models;

namespace FfaasLite.SDK;

public sealed class FlagClient : IFlagClient, IAsyncDisposable
{
    private static readonly FlagEvaluator Evaluator = new();

    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly string _baseUrl;
    private readonly JsonSerializerOptions _json;
    private readonly ConcurrentDictionary<string, Flag> _flags = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    private CancellationTokenSource? _streamCts;
    private Task? _streamTask;
    private FlagStreamOptions _options = FlagStreamOptions.CreateDefault();
    private long _lastVersion;
    private TimeSpan? _serverSuggestedRetry;

    public FlagClient(string baseUrl, HttpClient? http = null)
    {
        _baseUrl = baseUrl.TrimEnd('/');

        _json = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };
        _json.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

        if (http is null)
        {
            var handler = new SocketsHttpHandler
            {
                UseProxy = false,
                Proxy = null,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                ConnectTimeout = TimeSpan.FromSeconds(5),
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            _http = new HttpClient(handler)
            {
                Timeout = Timeout.InfiniteTimeSpan,
                DefaultRequestVersion = HttpVersion.Version11,
                DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower
            };
            _ownsHttpClient = true;
        }
        else
        {
            _http = http;
            _http.Timeout = Timeout.InfiniteTimeSpan;
            _http.DefaultRequestVersion = HttpVersion.Version11;
            _http.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
            _ownsHttpClient = false;
        }
    }

    public IReadOnlyDictionary<string, Flag> SnapshotCachedFlags()
        => _flags.ToDictionary(static kvp => kvp.Key, static kvp => kvp.Value, StringComparer.Ordinal);

    public bool TryGetCachedFlag(string key, out Flag? flag)
        => _flags.TryGetValue(key, out flag);

    public async Task<EvalResult> EvaluateAsync(string key, EvalContext ctx, CancellationToken ct = default)
    {
        if (_flags.TryGetValue(key, out var cached))
        {
            return Evaluator.Evaluate(cached, ctx);
        }

        var url = $"{_baseUrl}/api/evaluate/{Uri.EscapeDataString(key)}";
        using var resp = await _http.PostAsJsonAsync(url, ctx, _json, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        var result = await resp.Content.ReadFromJsonAsync<EvalResult>(_json, ct).ConfigureAwait(false)
                     ?? throw new InvalidOperationException("Empty EvalResult from server.");

        return Normalize(result);
    }

    public async Task RefreshSnapshotAsync(CancellationToken ct = default)
    {
        var flags = await _http.GetFromJsonAsync<List<Flag>>($"{_baseUrl}/api/flags", _json, ct).ConfigureAwait(false);
        if (flags is null)
        {
            return;
        }

        await _cacheLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var flag in flags)
            {
                seen.Add(flag.Key);
                _flags.AddOrUpdate(flag.Key, flag, (_, __) => flag);
            }

            foreach (var existing in _flags.Keys)
            {
                if (!seen.Contains(existing))
                {
                    _flags.TryRemove(existing, out _);
                }
            }
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    public async Task StartRealtimeAsync(FlagStreamOptions? options = null, CancellationToken ct = default)
    {
        if (_streamTask is not null && !_streamTask.IsCompleted)
        {
            throw new InvalidOperationException("Realtime stream already running.");
        }

        _options = (options ?? FlagStreamOptions.CreateDefault()).Clone();

        _streamCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _streamCts.Token;

        _streamTask = Task.Run(() => RunStreamLoopAsync(token), CancellationToken.None);

        if (_options.BootstrapSnapshot)
        {
            await RefreshSnapshotAsync(token).ConfigureAwait(false);
        }
    }

    public async Task StopRealtimeAsync()
    {
        if (_streamCts is null)
        {
            return;
        }

        _streamCts.Cancel();
        try
        {
            if (_streamTask is not null)
            {
                await _streamTask.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // expected on shutdown
        }
        finally
        {
            _streamCts.Dispose();
            _streamCts = null;
            _streamTask = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopRealtimeAsync().ConfigureAwait(false);

        if (_ownsHttpClient)
        {
            _http.Dispose();
        }

        _cacheLock.Dispose();
    }

    private async Task RunStreamLoopAsync(CancellationToken token)
    {
        var attempt = 0;

        while (!token.IsCancellationRequested)
        {
            HttpResponseMessage? response = null;
            Stream? stream = null;

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/api/stream");
                request.Headers.Accept.ParseAdd("text/event-stream");
                if (_lastVersion > 0)
                {
                    request.Headers.TryAddWithoutValidation("Last-Event-ID", _lastVersion.ToString(CultureInfo.InvariantCulture));
                }

                response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);

                using var reader = new StreamReader(stream, leaveOpen: false);
                attempt = 0;
                _serverSuggestedRetry = null;

                await ReadStreamAsync(reader, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception)
            {
                attempt++;
                var delay = _options.GetRetryDelay(attempt, _serverSuggestedRetry);
                if (delay > TimeSpan.Zero)
                {
                    try
                    {
                        await Task.Delay(delay, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        break;
                    }
                }
            }
            finally
            {
                stream?.Dispose();
                response?.Dispose();
            }
        }
    }

    private async Task ReadStreamAsync(StreamReader reader, CancellationToken token)
    {
        var builder = new SseMessageBuilder();

        while (!token.IsCancellationRequested)
        {
            var readTask = reader.ReadLineAsync();
            string? line;

            try
            {
                line = _options.HeartbeatTimeout == Timeout.InfiniteTimeSpan
                    ? await readTask.WaitAsync(token).ConfigureAwait(false)
                    : await readTask.WaitAsync(_options.HeartbeatTimeout, token).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                throw new TimeoutException($"No realtime updates received within {_options.HeartbeatTimeout}.");
            }

            if (line is null)
            {
                break;
            }

            var evt = builder.ProcessLine(line);
            if (builder.TryTakeRetry(out var retry))
            {
                _serverSuggestedRetry = retry;
            }

            if (evt is null)
            {
                continue;
            }

            await ProcessEventAsync(evt, token).ConfigureAwait(false);
        }
    }

    private async Task ProcessEventAsync(SseEvent evt, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(evt.EventName))
        {
            return;
        }

        switch (evt.EventName)
        {
            case "flag-change":
                if (string.IsNullOrWhiteSpace(evt.Data)) return;
                var change = JsonSerializer.Deserialize<FlagChangeEvent>(evt.Data, _json);
                if (change is null) return;
                _lastVersion = change.Version;
                await ApplyFlagChangeAsync(change, token).ConfigureAwait(false);
                break;

            case "heartbeat":
                // no-op, WaitAsync enforces heartbeat timeout
                break;
        }
    }

    private async Task ApplyFlagChangeAsync(FlagChangeEvent change, CancellationToken token)
    {
        await _cacheLock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            switch (change.Type)
            {
                case FlagChangeType.Deleted:
                    _flags.TryRemove(change.Payload.Key, out _);
                    break;
                default:
                    if (change.Payload.Flag is { } flag)
                    {
                        _flags.AddOrUpdate(flag.Key, flag, (_, __) => flag);
                    }
                    break;
            }
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private static EvalResult Normalize(EvalResult result)
    {
        if (result.Value is JsonElement element)
        {
            object? value = result.Type switch
            {
                FlagType.Boolean => element.ValueKind is JsonValueKind.Null ? null : element.GetBoolean(),
                FlagType.String => element.ValueKind is JsonValueKind.Null ? null : element.GetString(),
                FlagType.Number => element.ValueKind is JsonValueKind.Null ? null : element.GetDouble(),
                _ => null
            };

            return result with { Value = value };
        }

        return result;
    }

    private sealed record SseEvent(string? EventName, string? Id, string? Data);

    private sealed class SseMessageBuilder
    {
        private readonly StringBuilder _data = new();
        private string? _event;
        private string? _id;
        private TimeSpan? _retry;

        public SseEvent? ProcessLine(string line)
        {
            if (line.Length == 0)
            {
                if (_event is null && _data.Length == 0 && _id is null)
                {
                    Reset();
                    return null;
                }

                var evt = new SseEvent(_event, _id, _data.ToString());
                Reset();
                return evt;
            }

            if (line[0] == ':')
            {
                return null;
            }

            var colonIndex = line.IndexOf(':', StringComparison.Ordinal);
            string field;
            string value;

            if (colonIndex >= 0)
            {
                field = line[..colonIndex];
                value = line[(colonIndex + 1)..];
                if (value.StartsWith(" ", StringComparison.Ordinal))
                {
                    value = value[1..];
                }
            }
            else
            {
                field = line;
                value = string.Empty;
            }

            switch (field)
            {
                case "data":
                    if (_data.Length > 0)
                    {
                        _data.Append('\n');
                    }
                    _data.Append(value);
                    break;
                case "event":
                    _event = value;
                    break;
                case "id":
                    _id = value;
                    break;
                case "retry":
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms) && ms >= 0)
                    {
                        _retry = TimeSpan.FromMilliseconds(ms);
                    }
                    break;
            }

            return null;
        }

        public bool TryTakeRetry(out TimeSpan retry)
        {
            if (_retry.HasValue)
            {
                retry = _retry.Value;
                _retry = null;
                return true;
            }

            retry = default;
            return false;
        }

        private void Reset()
        {
            _data.Clear();
            _event = null;
            _id = null;
        }
    }
}

public sealed class FlagStreamOptions
{
    public TimeSpan InitialRetryDelay { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(30);
    public double BackoffFactor { get; set; } = 2d;
    public TimeSpan HeartbeatTimeout { get; set; } = TimeSpan.FromSeconds(45);
    public bool BootstrapSnapshot { get; set; } = true;

    internal static FlagStreamOptions CreateDefault() => new();

    internal FlagStreamOptions Clone() => new()
    {
        InitialRetryDelay = InitialRetryDelay,
        MaxRetryDelay = MaxRetryDelay,
        BackoffFactor = BackoffFactor,
        HeartbeatTimeout = HeartbeatTimeout,
        BootstrapSnapshot = BootstrapSnapshot
    };

    internal TimeSpan GetRetryDelay(int attempt, TimeSpan? serverSuggested)
    {
        if (attempt < 1) attempt = 1;

        var factor = BackoffFactor <= 0 ? 1 : BackoffFactor;
        var exponent = attempt - 1;
        var computedMs = InitialRetryDelay.TotalMilliseconds * Math.Pow(factor, exponent);
        if (computedMs < 0)
        {
            computedMs = 0;
        }

        var computed = TimeSpan.FromMilliseconds(Math.Min(computedMs, MaxRetryDelay.TotalMilliseconds));
        if (computed < TimeSpan.Zero)
        {
            computed = TimeSpan.Zero;
        }

        if (serverSuggested.HasValue && serverSuggested.Value > computed)
        {
            computed = serverSuggested.Value;
        }

        return computed;
    }
}
