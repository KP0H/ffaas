using FfaasLite.Core.Flags;
using FfaasLite.Core.Models;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FfaasLite.SDK
{
    public class FlagClient : IFlagClient, IAsyncDisposable
    {
        private readonly HttpClient _http;
        private readonly string _baseUrl;
        private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
        private readonly ConcurrentDictionary<string, Flag> _flags = new();

        private Task? _sseTask;
        private CancellationTokenSource? _sseCts;

        public FlagClient(string baseUrl, HttpClient? http = null)
        {
            _baseUrl = baseUrl.TrimEnd('/');

            _json.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

            if (http is null)
            {
                // Prefer a predictable handler for local development and container scenarios
                var handler = new SocketsHttpHandler
                {
                    // Disable proxy usage to avoid 502 responses from system proxies on localhost
                    UseProxy = false,
                    Proxy = null,

                    // Normalize timeouts and connection lifetime for stable DNS/KeepAlive behavior
                    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                    ConnectTimeout = TimeSpan.FromSeconds(5),
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                };

                _http = new HttpClient(handler)
                {
                    Timeout = TimeSpan.FromSeconds(10),
                    DefaultRequestVersion = HttpVersion.Version11, // Force HTTP/1.1 for SSE compatibility
                    DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower
                };
            }
            else
            {
                _http = http;
                // Ensure supplied client keeps HTTP/1.1 defaults for SSE support
                _http.DefaultRequestVersion = HttpVersion.Version11;
                _http.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
            }
        }

        public async Task<EvalResult> EvaluateAsync(string key, EvalContext ctx, CancellationToken ct = default)
        {
            if (_flags.TryGetValue(key, out var cached))
            {
                var evalLocal = new FlagEvaluator().Evaluate(cached, ctx);
                return evalLocal;
            }

            var url = $"{_baseUrl}/api/evaluate/{Uri.EscapeDataString(key)}";
            var resp = await _http.PostAsJsonAsync(url, ctx, _json, ct);
            resp.EnsureSuccessStatusCode();

            var result = await resp.Content.ReadFromJsonAsync<EvalResult>(_json, ct)
                         ?? throw new InvalidOperationException("Empty EvalResult from server.");

            return Normalize(result);
        }

        private static EvalResult Normalize(EvalResult r)
        {
            if (r.Value is System.Text.Json.JsonElement je)
            {
                object? v = r.Type switch
                {
                    FlagType.Boolean => je.ValueKind is System.Text.Json.JsonValueKind.Null
                        ? null
                        : je.GetBoolean(),
                    FlagType.String => je.ValueKind is System.Text.Json.JsonValueKind.Null
                        ? null
                        : je.GetString(),
                    FlagType.Number => je.ValueKind is System.Text.Json.JsonValueKind.Null
                        ? null
                        : je.GetDouble(),
                    _ => null
                };
                return r with { Value = v };
            }
            return r;
        }


        public Task StartSseAsync(CancellationToken ct = default)
        {
            _sseCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var token = _sseCts.Token;

            _sseTask = Task.Run(async () =>
            {
                try
                {
                    using var stream = await _http.GetStreamAsync($"{_baseUrl}/api/stream", token);
                    using var reader = new StreamReader(stream);
                    while (!reader.EndOfStream && !token.IsCancellationRequested)
                    {
                        var line = await reader.ReadLineAsync();
                        if (line is null) break;
                        if (!line.StartsWith("data: ")) continue;

                        var json = line.Substring("data: ".Length);
                        var flag = JsonSerializer.Deserialize<Flag>(json, _json);
                        if (flag is null || string.IsNullOrWhiteSpace(flag.Key)) continue;

                        _flags.AddOrUpdate(flag.Key, flag, (_, __) => flag);
                    }
                }
                catch (OperationCanceledException) { /* ignore */ }
            }, token);

            return Task.CompletedTask;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                _sseCts?.Cancel();
                if (_sseTask is not null) await _sseTask;
            }
            catch { /* ignore */ }
            finally
            {
                _sseCts?.Dispose();
            }
        }
    }
}
