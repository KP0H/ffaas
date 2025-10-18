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
