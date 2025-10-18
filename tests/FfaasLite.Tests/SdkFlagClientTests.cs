using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FfaasLite.Core.Flags;
using FfaasLite.Core.Models;
using FfaasLite.SDK;
using Xunit;

namespace FfaasLite.Tests
{
    public class SdkFlagClientTests
    {
        [Fact]
        public async Task EvaluateAsync_Uses_Server_When_Flag_Not_In_LocalCache()
        {
            // Arrange: stub HTTP handler that returns a serialized EvalResult
            var eval = new EvalResult("new-ui", true, FlagType.Boolean, "rule", DateTimeOffset.UtcNow);

            var handler = new MockHandler((req, ct) =>
            {
                if (req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath.EndsWith("/api/evaluate/new-ui"))
                {
                    var resp = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(JsonSerializer.Serialize(eval, new JsonSerializerOptions(JsonSerializerDefaults.Web)))
                    };
                    resp.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                    return Task.FromResult(resp);
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            });

            var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
            var client = new FlagClient("http://localhost", http);

            // Act
            var res = await client.EvaluateAsync("new-ui", new EvalContext(UserId: "u1", Attributes: new()));

            // Assert
            Assert.True((bool)res.Value!);
            Assert.Equal("rule", res.Variant);
            Assert.Equal("new-ui", res.Key);
            Assert.Equal(FlagType.Boolean, res.Type);
        }

        [Fact]
        public async Task RefreshSnapshotAsync_Preserves_Advanced_Targeting_Fields()
        {
            var flags = new[]
            {
                new Flag
                {
                    Key = "advanced",
                    Type = FlagType.Boolean,
                    BoolValue = false,
                    Rules =
                    [
                        new TargetRule(
                            Attribute: "userId",
                            Operator: "percentage",
                            Value: "gradual",
                            Priority: 10,
                            BoolOverride: true,
                            Percentage: 42.5,
                            PercentageAttribute: "sessionId",
                            SegmentDelimiter: "|")
                    ]
                }
            };

            var handler = new MockHandler((req, ct) =>
            {
                if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath.Equals("/api/flags", StringComparison.OrdinalIgnoreCase))
                {
                    var response = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(JsonSerializer.Serialize(flags, new JsonSerializerOptions(JsonSerializerDefaults.Web)))
                    };
                    response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                    return Task.FromResult(response);
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            });

            var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
            await using var client = new FlagClient("http://localhost", http);

            await client.RefreshSnapshotAsync();

            Assert.True(client.TryGetCachedFlag("advanced", out var cached));
            Assert.NotNull(cached);
            var rule = Assert.Single(cached!.Rules);
            Assert.Equal("percentage", rule.Operator);
            Assert.Equal(42.5, rule.Percentage);
            Assert.Equal("sessionId", rule.PercentageAttribute);
            Assert.Equal("|", rule.SegmentDelimiter);
        }

        [Fact]
        public async Task CreateAsync_BootstrapLoadsFlags()
        {
            var callCount = 0;
            var handler = new MockHandler((req, ct) =>
            {
                if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath.Equals("/api/flags", StringComparison.OrdinalIgnoreCase))
                {
                    Interlocked.Increment(ref callCount);
                    var json = JsonSerializer.Serialize(new[]
                    {
                        new Flag { Key = "beta", Type = FlagType.Boolean, BoolValue = true }
                    }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

                    var response = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(json)
                    };
                    response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                    return Task.FromResult(response);
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            });

            var options = new FlagClientOptions
            {
                BaseUrl = "http://localhost",
                HttpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") },
                BootstrapOnStartup = true,
                StartRealtimeStream = false,
                BackgroundRefresh = new BackgroundRefreshOptions { Enabled = false }
            };

            await using var client = await FlagClient.CreateAsync(options);

            Assert.Equal(1, callCount);
            Assert.True(client.TryGetCachedFlag("beta", out var flag));
            Assert.NotNull(flag);
            Assert.True(flag!.BoolValue);
        }

        [Fact]
        public async Task BackgroundRefresh_Performs_PeriodicUpdates()
        {
            var callCount = 0;
            var handler = new MockHandler((req, ct) =>
            {
                if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath.Equals("/api/flags", StringComparison.OrdinalIgnoreCase))
                {
                    var iteration = Interlocked.Increment(ref callCount);
                    var flag = new Flag
                    {
                        Key = "beta",
                        Type = FlagType.Boolean,
                        BoolValue = iteration % 2 == 0
                    };
                    var json = JsonSerializer.Serialize(new[] { flag }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                    var response = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(json)
                    };
                    response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                    return Task.FromResult(response);
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            });

            var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
            await using var client = new FlagClient("http://localhost", httpClient);

            await client.StartBackgroundRefreshAsync(TimeSpan.FromMilliseconds(25));
            await Task.Delay(120);
            Assert.True(callCount >= 2);
            await client.StopBackgroundRefreshAsync();
        }

        [Fact]
        public async Task BackgroundRefresh_Errors_Are_Reported()
        {
            var captured = new List<Exception>();
            var handler = new MockHandler((req, ct) =>
            {
                if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath.Equals("/api/flags", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromException<HttpResponseMessage>(new HttpRequestException("boom"));
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            });

            var options = new FlagClientOptions
            {
                BaseUrl = "http://localhost",
                HttpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") },
                BootstrapOnStartup = false,
                StartRealtimeStream = false,
                BackgroundRefresh = new BackgroundRefreshOptions { Enabled = true, Interval = TimeSpan.FromMilliseconds(20) },
                OnBackgroundRefreshError = ex => captured.Add(ex)
            };

            await using var client = await FlagClient.CreateAsync(options);
            await Task.Delay(80);
            await client.StopBackgroundRefreshAsync();

            Assert.NotEmpty(captured);
        }

        [Fact]
        public async Task RequestTimeout_CancelsLongRunningCall()
        {
            var handler = new MockHandler(async (req, ct) =>
            {
                if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath.Equals("/api/flags", StringComparison.OrdinalIgnoreCase))
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                };
            });

            var options = new FlagClientOptions
            {
                BaseUrl = "http://localhost",
                HttpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") },
                BootstrapOnStartup = false,
                StartRealtimeStream = false,
                RequestTimeout = TimeSpan.FromMilliseconds(50)
            };

            await using var client = await FlagClient.CreateAsync(options);

            await Assert.ThrowsAsync<TaskCanceledException>(() => client.RefreshSnapshotAsync());
        }

        [Fact]
        public async Task SendAsyncWrapper_Is_Invoked()
        {
            var handler = new MockHandler((req, ct) =>
            {
                if (req.RequestUri!.AbsolutePath.Equals("/api/flags", StringComparison.OrdinalIgnoreCase))
                {
                    var json = JsonSerializer.Serialize(Array.Empty<Flag>(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
                    var response = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(json)
                    };
                    response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                    return Task.FromResult(response);
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            });

            var counter = 0;
            var options = new FlagClientOptions
            {
                BaseUrl = "http://localhost",
                HttpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") },
                BootstrapOnStartup = false,
                StartRealtimeStream = false,
                SendAsyncWrapper = async (next, request, completion, token) =>
                {
                    Interlocked.Increment(ref counter);
                    return await next(request, completion, token);
                }
            };

            await using var client = await FlagClient.CreateAsync(options);
            await client.RefreshSnapshotAsync();

            Assert.True(counter > 0);
        }

        internal sealed class MockHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

            public MockHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
                => _handler = handler;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                => _handler(request, cancellationToken);
        }
    }
}
