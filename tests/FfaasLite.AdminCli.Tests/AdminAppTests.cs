using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using FfaasLite.AdminCli;
using FfaasLite.Core.Models;
using Xunit;

namespace FfaasLite.AdminCli.Tests;

public class AdminAppTests
{
    private static readonly JsonSerializerOptions Json = JsonOptions.Create();

    [Fact]
    public async Task FlagsList_PrintsTable()
    {
        var handler = new StubHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.Equal("/api/flags", req.RequestUri!.AbsolutePath);
            var flags = new[]
            {
                new FlagRecord { Key = "beta", Type = FlagType.Boolean, BoolValue = true, UpdatedAt = DateTimeOffset.Parse("2024-01-01T00:00:00Z") }
            };
            return Response(HttpStatusCode.OK, flags);
        });

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var args = new[] { "--api-key", "test-token", "flags", "list" };
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };

        var exit = await AdminApp.RunAsync(args, stdout, stderr, httpClient);

        Assert.Equal(0, exit);
        var output = stdout.ToString();
        Assert.Contains("beta", output);
        Assert.Contains("boolean", output);
        Assert.Empty(stderr.ToString());
    }

    [Fact]
    public void HttpClientBuilder_DisablesProxy()
    {
        var handler = HttpClientBuilder.CreateHandler();
        var socketsHandler = Assert.IsType<SocketsHttpHandler>(handler);
        Assert.False(socketsHandler.UseProxy);
        Assert.Null(socketsHandler.Proxy);
    }

    [Fact]
    public async Task FlagsUpsert_CreatesWhenMissing()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new StubHttpMessageHandler(req =>
        {
            requests.Add(req);
            if (req.Method == HttpMethod.Get)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            Assert.Equal(HttpMethod.Post, req.Method);
            var payload = req.Content!.ReadAsStringAsync().Result;
            var dto = JsonSerializer.Deserialize<FlagCreateDto>(payload, Json);
            
            Assert.Equal("new-flag", dto.Key);
            Assert.Equal(FlagType.String, dto.Type);
            Assert.Equal("hello", dto.StringValue);

            return new HttpResponseMessage(HttpStatusCode.Created);
        });

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var args = new[] { "--api-key", "test", "flags", "upsert", "new-flag", "--type", "string", "--string-value", "hello" };
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };

        var exit = await AdminApp.RunAsync(args, stdout, stderr, client);

        Assert.Equal(0, exit);
        Assert.Contains("Created flag 'new-flag'.", stdout.ToString());
        Assert.Equal(2, requests.Count);
    }

    [Fact]
    public async Task FlagsUpsert_UpdatesExisting()
    {
        var requests = new List<HttpRequestMessage>();
        var existing = new FlagRecord
        {
            Key = "existing",
            Type = FlagType.Boolean,
            BoolValue = false,
            UpdatedAt = DateTimeOffset.Parse("2024-01-01T00:00:00Z")
        };

        var handler = new StubHttpMessageHandler(req =>
        {
            requests.Add(req);
            if (req.Method == HttpMethod.Get)
            {
                return Response(HttpStatusCode.OK, existing);
            }

            Assert.Equal(HttpMethod.Put, req.Method);
            var payload = req.Content!.ReadAsStringAsync().Result;
            var dto = JsonSerializer.Deserialize<FlagUpdateDto>(payload, Json);
            
            Assert.True(dto.BoolValue);
            Assert.Equal(existing.UpdatedAt, dto.LastKnownUpdatedAt);

            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var args = new[] { "--api-key", "tok", "flags", "upsert", "existing", "--bool-value", "true" };
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };

        var exit = await AdminApp.RunAsync(args, stdout, stderr, client);

        Assert.Equal(0, exit);
        Assert.Contains("Updated flag 'existing'.", stdout.ToString());
        Assert.Equal(2, requests.Count);
    }

    [Fact]
    public async Task FlagsDelete_CallsApi()
    {
        var handler = new StubHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Delete, req.Method);
            Assert.Equal("/api/flags/remove", req.RequestUri!.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var args = new[] { "--api-key", "tok", "flags", "delete", "remove" };
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };

        var exit = await AdminApp.RunAsync(args, stdout, stderr, client);

        Assert.Equal(0, exit);
        Assert.Contains("Deleted flag 'remove'.", stdout.ToString());
        Assert.Empty(stderr.ToString());
    }

    [Fact]
    public async Task AuditsList_RespectsTake()
    {
        var audits = Enumerable.Range(0, 10).Select(i => new AuditRecord
        {
            Action = "update",
            FlagKey = $"flag-{i}",
            Actor = "cli",
            At = DateTimeOffset.UtcNow.AddMinutes(-i)
        }).ToList();

        var handler = new StubHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            return Response(HttpStatusCode.OK, audits);
        });

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var args = new[] { "--api-key", "tok", "audits", "list", "--take", "3" };
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };

        var exit = await AdminApp.RunAsync(args, stdout, stderr, client);

        Assert.Equal(0, exit);
        var output = stdout.ToString();
        Assert.Contains("flag-0", output);
        Assert.DoesNotContain("flag-5", output);
    }

    private static HttpResponseMessage Response<T>(HttpStatusCode statusCode, T payload)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, Json), Encoding.UTF8, "application/json")
        };
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _generator;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> generator)
        {
            _generator = generator;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_generator(request));
        }
    }
}
