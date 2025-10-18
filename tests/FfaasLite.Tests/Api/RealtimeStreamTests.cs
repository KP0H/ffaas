using System;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using FfaasLite.Api.Contracts;
using FfaasLite.Core.Models;
using FfaasLite.SDK;

namespace FfaasLite.Tests.Api;

public class RealtimeStreamTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [Fact]
    public async Task Stream_EmitsStructuredEvent_OnFlagCreate()
    {
        using var factory = new TestApiFactory();
        await factory.ResetStateAsync();

        using var anonymousClient = factory.CreateClient();
        using var response = await anonymousClient.GetAsync("/api/stream", HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        using var readCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var nextEventTask = ReadNextEventAsync(reader, "flag-change", readCts.Token);

        using var editorClient = CreateEditorClient(factory);
        var createDto = new FlagCreateDto("sse-create", FlagType.Boolean, BoolValue: true);
        var createResponse = await editorClient.PostAsJsonAsync("/api/flags", createDto, readCts.Token);
        createResponse.EnsureSuccessStatusCode();

        var evt = await nextEventTask;
        readCts.Cancel();

        var change = JsonSerializer.Deserialize<FlagChangeEvent>(evt.Data ?? string.Empty, Json);
        Assert.NotNull(change);
        Assert.Equal(FlagChangeType.Created, change!.Type);
        Assert.Equal("sse-create", change.Payload.Key);
        Assert.NotNull(change.Payload.Flag);
        Assert.True(change.Payload.Flag!.BoolValue);
    }

    [Fact]
    public async Task FlagClient_RebuildsCache_FromRealtimeEvents()
    {
        using var factory = new TestApiFactory();
        await factory.ResetStateAsync();

        using var realtimeHttp = factory.CreateClient();
        await using var flagClient = new FlagClient(realtimeHttp.BaseAddress!.ToString(), realtimeHttp);

        var options = new FlagStreamOptions
        {
            BootstrapSnapshot = true,
            InitialRetryDelay = TimeSpan.FromMilliseconds(200),
            MaxRetryDelay = TimeSpan.FromSeconds(2),
            HeartbeatTimeout = TimeSpan.FromSeconds(15)
        };

        await flagClient.StartRealtimeAsync(options);
        await Task.Delay(200); // allow stream to connect

        using var editorClient = CreateEditorClient(factory);

        var createDto = new FlagCreateDto("sdk-realtime", FlagType.Boolean, BoolValue: true);
        var createResponse = await editorClient.PostAsJsonAsync("/api/flags", createDto);
        createResponse.EnsureSuccessStatusCode();

        await EventuallyAsync(() => Task.FromResult(flagClient.TryGetCachedFlag("sdk-realtime", out var flag) && flag is { BoolValue: true }), TimeSpan.FromSeconds(5));

        Assert.True(flagClient.TryGetCachedFlag("sdk-realtime", out var cached));
        Assert.NotNull(cached);

        var updateDto = new FlagUpdateDto(
            Type: FlagType.Boolean,
            BoolValue: false,
            LastKnownUpdatedAt: cached!.UpdatedAt);

        var updateResponse = await editorClient.PutAsJsonAsync("/api/flags/sdk-realtime", updateDto);
        updateResponse.EnsureSuccessStatusCode();

        await EventuallyAsync(() => Task.FromResult(flagClient.TryGetCachedFlag("sdk-realtime", out var flag) && flag is { BoolValue: false }), TimeSpan.FromSeconds(5));

        var deleteResponse = await editorClient.DeleteAsync("/api/flags/sdk-realtime");
        deleteResponse.EnsureSuccessStatusCode();

        await EventuallyAsync(() => Task.FromResult(!flagClient.TryGetCachedFlag("sdk-realtime", out _)), TimeSpan.FromSeconds(5));
    }

    private static async Task<SseEvent> ReadNextEventAsync(StreamReader reader, string expectedEvent, CancellationToken token)
    {
        var builder = new TestSseBuilder();

        while (!token.IsCancellationRequested)
        {
            var lineTask = reader.ReadLineAsync();
            string? line;

            try
            {
                line = await lineTask.WaitAsync(TimeSpan.FromSeconds(10), token);
            }
            catch (TimeoutException)
            {
                throw new TimeoutException("Timed out waiting for SSE event.");
            }

            if (line is null)
            {
                throw new InvalidOperationException("SSE stream closed unexpectedly.");
            }

            var evt = builder.ProcessLine(line);
            if (builder.TryTakeRetry(out _))
            {
                continue;
            }

            if (evt is null || !string.Equals(evt.EventName, expectedEvent, StringComparison.Ordinal))
            {
                continue;
            }

            return evt;
        }

        throw new OperationCanceledException();
    }

    private static async Task EventuallyAsync(Func<Task<bool>> predicate, TimeSpan timeout, TimeSpan? poll = null)
    {
        using var cts = new CancellationTokenSource(timeout);
        var delay = poll ?? TimeSpan.FromMilliseconds(100);

        while (!cts.IsCancellationRequested)
        {
            if (await predicate().ConfigureAwait(false))
            {
                return;
            }

            try
            {
                await Task.Delay(delay, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                break;
            }
        }

        throw new TimeoutException("Condition was not met within the allotted time.");
    }

    private static HttpClient CreateEditorClient(TestApiFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "dev-editor-token");
        client.DefaultRequestHeaders.Add("X-Api-Key", "dev-editor-token");
        return client;
    }

    private sealed record SseEvent(string? EventName, string? Data);

    private sealed class TestSseBuilder
    {
        private readonly StringBuilder _data = new();
        private string? _event;
        private TimeSpan? _retry;

        public SseEvent? ProcessLine(string line)
        {
            if (line.Length == 0)
            {
                if (_event is null && _data.Length == 0)
                {
                    Reset();
                    return null;
                }

                var evt = new SseEvent(_event, _data.ToString());
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
                case "retry":
                    if (int.TryParse(value, out var ms) && ms >= 0)
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
        }
    }
}

