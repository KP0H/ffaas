using FfaasLite.Core.Flags;
using FfaasLite.Core.Models;
using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;

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
            _http = http ?? new HttpClient();
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

            var result = await resp.Content.ReadFromJsonAsync<EvalResult>(_json, ct);
            if (result is null) throw new InvalidOperationException("Empty EvalResult from server.");

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
