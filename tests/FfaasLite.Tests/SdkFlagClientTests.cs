using System.Net;
using System.Net.Http;
using System.Text.Json;

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
