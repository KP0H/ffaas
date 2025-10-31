using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using FfaasLite.Api.Contracts;
using FfaasLite.Api.Helpers;
using FfaasLite.Api.Infrastructure;
using FfaasLite.Api.Realtime;
using FfaasLite.Api.Security;
using FfaasLite.Core.Flags;
using FfaasLite.Core.Models;
using FfaasLite.Infrastructure.Cache;
using FfaasLite.Infrastructure.Db;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SupportNonNullableReferenceTypes();

    c.SchemaGeneratorOptions.SchemaFilters.Add(new EnumSchemaFilter());
});

builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseNpgsql(cfg.GetConnectionString("postgres")));

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(cfg.GetConnectionString("redis")!));

builder.Services.AddSingleton<RedisCache>();

builder.Services.AddSingleton<IFlagEvaluator, FlagEvaluator>();
builder.Services.Configure<DatabaseMigrationOptions>(cfg.GetSection("Database:Migrations"));
builder.Services.AddSingleton<IDatabaseInitializer, DatabaseInitializer>();
builder.Services.AddHostedService<DatabaseMigrationHostedService>();

var dataProtection = builder.Services.AddDataProtection();
var dataProtectionKeysDir = cfg["DataProtection:KeysDirectory"];
if (!string.IsNullOrWhiteSpace(dataProtectionKeysDir))
{
    Directory.CreateDirectory(dataProtectionKeysDir);
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysDir));
}

builder.Services.AddOptions<ApiKeyAuthenticationOptions>()
    .Bind(cfg.GetSection("Auth"));
builder.Services.AddOptions<ApiKeyAuthenticationOptions>(AuthConstants.Schemes.ApiKey)
    .Bind(cfg.GetSection("Auth"));

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = AuthConstants.Schemes.ApiKey;
    options.DefaultChallengeScheme = AuthConstants.Schemes.ApiKey;
}).AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(AuthConstants.Schemes.ApiKey, _ => { });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AuthConstants.Policies.Reader, policy =>
        policy.RequireRole(AuthConstants.Roles.Reader, AuthConstants.Roles.Editor));
    options.AddPolicy(AuthConstants.Policies.Editor, policy =>
        policy.RequireRole(AuthConstants.Roles.Editor));
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseAuthentication();
app.UseAuthorization();

var sseClients = new ConcurrentDictionary<Guid, SseClientConnection>();
var sseJsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
sseJsonOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
var heartbeatInterval = TimeSpan.FromSeconds(15);
var retryAfter = TimeSpan.FromSeconds(5);
var changeVersion = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/api/flags", async (HttpContext httpContext, [FromBody] FlagCreateDto dto, AppDbContext db, RedisCache cache) =>
{
    var flag = new Flag
    {
        Key = dto.Key,
        Type = dto.Type,
        BoolValue = dto.BoolValue,
        StringValue = dto.StringValue,
        NumberValue = dto.NumberValue,
        Rules = dto.Rules ?? new()
    };

    var actor = ResolveActor(httpContext);
    var snapshot = CloneFlag(flag);

    db.Flags.Add(flag);
    db.Audit.Add(new AuditEntry
    {
        Action = "create",
        Actor = actor,
        FlagKey = flag.Key,
        DiffJson = new AuditDiff { Before = null, Updated = snapshot }
    });
    await db.SaveChangesAsync();

    await cache.RemoveAsync("flags:all");
    await BroadcastFlagChangeAsync(snapshot, FlagChangeType.Created);
    return Results.Created($"/api/flags/{flag.Key}", flag);
}).RequireAuthorization(AuthConstants.Policies.Editor);

app.MapGet("/api/flags", async (AppDbContext db, RedisCache cache) =>
{
    const string cacheKey = "flags:all";
    var flags = await cache.GetAsync<List<Flag>>(cacheKey);
    if (flags is null)
    {
        flags = await db.Flags.AsNoTracking().ToListAsync();
        await cache.SetAsync(cacheKey, flags, TimeSpan.FromMinutes(2));
    }
    return Results.Ok(flags);
});

app.MapGet("/api/flags/{key}", async (string key, AppDbContext db, RedisCache cache) =>
{
    var cacheKey = $"flag:{key}";
    var flag = await cache.GetAsync<Flag>(cacheKey);
    if (flag is null)
    {
        flag = await db.Flags.AsNoTracking().FirstOrDefaultAsync(f => f.Key == key);
        if (flag is null) return Results.NotFound();
        await cache.SetAsync(cacheKey, flag);
    }
    return Results.Ok(flag);
});

app.MapPut("/api/flags/{key}", async (HttpContext httpContext, string key, [FromBody] FlagUpdateDto dto, AppDbContext db, RedisCache cache) =>
{
    var existing = await db.Flags.FirstOrDefaultAsync(f => f.Key == key);
    if (existing is null) return Results.NotFound();

    if (dto.LastKnownUpdatedAt is null || dto.LastKnownUpdatedAt == default)
    {
        return Results.BadRequest(new ProblemDetails
        {
            Title = "Missing concurrency token.",
            Detail = "Provide lastKnownUpdatedAt from prior GET/POST response."
        });
    }

    if (existing.UpdatedAt != dto.LastKnownUpdatedAt)
    {
        return Results.Conflict(new ProblemDetails
        {
            Title = "Flag updated by another request.",
            Detail = "Reload the flag and retry the operation with a fresh lastKnownUpdatedAt."
        });
    }

    var beforeSnapshot = CloneFlag(existing);

    existing.Type = dto.Type;
    existing.BoolValue = dto.BoolValue;
    existing.StringValue = dto.StringValue;
    existing.NumberValue = dto.NumberValue;
    existing.Rules = dto.Rules is { Count: > 0 } updatedRules
        ? updatedRules.Select(r => r with { }).ToList()
        : new List<TargetRule>();
    existing.UpdatedAt = DateTimeOffset.UtcNow;

    var actor = ResolveActor(httpContext);
    var afterSnapshot = CloneFlag(existing);
    db.Audit.Add(new AuditEntry
    {
        Action = "update",
        Actor = actor,
        FlagKey = key,
        DiffJson = new AuditDiff { Before = beforeSnapshot, Updated = afterSnapshot }
    });
    await db.SaveChangesAsync();

    await cache.RemoveAsync($"flag:{key}");
    await cache.RemoveAsync("flags:all");
    await BroadcastFlagChangeAsync(afterSnapshot, FlagChangeType.Updated);
    return Results.Ok(existing);
}).RequireAuthorization(AuthConstants.Policies.Editor)
  .Produces<Flag>(StatusCodes.Status200OK)
  .Produces(StatusCodes.Status400BadRequest)
  .Produces(StatusCodes.Status404NotFound)
  .Produces(StatusCodes.Status409Conflict);

app.MapDelete("/api/flags/{key}", async (HttpContext httpContext, string key, AppDbContext db, RedisCache cache) =>
{
    var existing = await db.Flags.FirstOrDefaultAsync(f => f.Key == key);
    if (existing is null) return Results.NotFound();
    var beforeSnapshot = CloneFlag(existing);
    var actor = ResolveActor(httpContext);

    db.Flags.Remove(existing);
    db.Audit.Add(new AuditEntry
    {
        Action = "delete",
        Actor = actor,
        FlagKey = key,
        DiffJson = new AuditDiff { Before = beforeSnapshot, Updated = null }
    });
    await db.SaveChangesAsync();

    await cache.RemoveAsync($"flag:{key}");
    await cache.RemoveAsync("flags:all");
    await BroadcastFlagChangeAsync(beforeSnapshot, FlagChangeType.Deleted);
    return Results.NoContent();
}).RequireAuthorization(AuthConstants.Policies.Editor);

app.MapGet("/api/audit", async (AppDbContext db) => Results.Ok(await db.Audit.AsNoTracking().OrderByDescending(a => a.At).Take(100).ToListAsync()))
   .RequireAuthorization(AuthConstants.Policies.Reader);

app.MapPost("/api/evaluate/{key}", async (
    [FromRoute] string key,
    [FromBody] EvalContext ctx,
    [FromServices] AppDbContext db,
    [FromServices] IFlagEvaluator eval,
    [FromServices] RedisCache cache) =>
{
    var cacheKey = $"flag:{key}";
    var flag = await cache.GetAsync<Flag>(cacheKey);
    if (flag is null)
    {
        flag = await db.Flags.AsNoTracking().FirstOrDefaultAsync(f => f.Key == key);
        if (flag is null) return Results.NotFound();
        await cache.SetAsync(cacheKey, flag);
    }
    return flag is null ? Results.NotFound() : Results.Ok(eval.Evaluate(flag, ctx));
})
.WithName("EvaluateFlag")
.Produces<EvalResult>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound)
.WithOpenApi(operation =>
{
    operation.Summary = "Evaluate feature flag for a given context";
    operation.Description = "Returns an evaluation result or 404 if the flag doesn't exist.";
    return operation;
});

app.MapGet("/api/stream", async (HttpContext context) =>
{
    context.Response.Headers.Append("Content-Type", "text/event-stream");
    context.Response.Headers.Append("Cache-Control", "no-cache");
    context.Response.Headers.Append("Connection", "keep-alive");
    context.Response.Headers.Append("X-Accel-Buffering", "no");

    await context.Response.Body.FlushAsync();

    var clientId = Guid.NewGuid();
    var connection = new SseClientConnection(context.Response);

    if (!sseClients.TryAdd(clientId, connection))
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        return;
    }

    connection.AbortRegistration = context.RequestAborted.Register(() => RemoveClient(clientId));
    connection.HeartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
    _ = Task.Run(() => SendHeartbeatsAsync(clientId, connection.HeartbeatCts.Token));

    await SendRetryIntervalAsync(connection, retryAfter);
    if (!await SendHeartbeatAsync(connection))
    {
        RemoveClient(clientId);
        return;
    }

    try
    {
        await Task.Delay(Timeout.Infinite, context.RequestAborted);
    }
    catch (OperationCanceledException)
    {
        // client disconnected
    }
    finally
    {
        RemoveClient(clientId);
    }
});

app.MapGet("/ws", () =>
        Results.Problem(
            detail: "WebSocket updates are no longer available. Subscribe to /api/stream for realtime flag changes.",
            statusCode: StatusCodes.Status410Gone,
            title: "WebSocket endpoint removed"))
   .WithName("WebSocketDeprecated")
   .Produces(StatusCodes.Status410Gone);

async Task BroadcastFlagChangeAsync(Flag flag, FlagChangeType changeType)
{
    var payload = new FlagChangePayload
    {
        Key = flag.Key,
        Flag = changeType == FlagChangeType.Deleted ? null : CloneFlag(flag)
    };

    var evt = new FlagChangeEvent
    {
        Type = changeType,
        Version = NextVersion(),
        Payload = payload
    };

    var message = JsonSerializer.Serialize(evt, sseJsonOptions);
    var eventId = evt.Version.ToString(CultureInfo.InvariantCulture);

    foreach (var entry in sseClients.ToArray())
    {
        var success = await SendSseAsync(entry.Value, "flag-change", eventId, message);
        if (!success)
        {
            RemoveClient(entry.Key);
        }
    }
}

async Task<bool> SendHeartbeatAsync(SseClientConnection connection)
{
    var payload = JsonSerializer.Serialize(new { at = DateTimeOffset.UtcNow }, sseJsonOptions);
    return await SendSseAsync(connection, "heartbeat", null, payload);
}

async Task SendRetryIntervalAsync(SseClientConnection connection, TimeSpan retry)
{
    var entered = false;
    try
    {
        await connection.WriteLock.WaitAsync();
        entered = true;
        var buffer = Encoding.UTF8.GetBytes($"retry: {(int)retry.TotalMilliseconds}\n\n");
        await connection.Response.Body.WriteAsync(buffer);
        await connection.Response.Body.FlushAsync();
    }
    catch
    {
        // client disconnected
    }
    finally
    {
        if (entered)
        {
            connection.WriteLock.Release();
        }
    }
}

async Task SendHeartbeatsAsync(Guid clientId, CancellationToken cancellationToken)
{
    while (!cancellationToken.IsCancellationRequested)
    {
        try
        {
            await Task.Delay(heartbeatInterval, cancellationToken);
            if (!sseClients.TryGetValue(clientId, out var connection)) break;
            var ok = await SendHeartbeatAsync(connection);
            if (!ok)
            {
                RemoveClient(clientId);
                break;
            }
        }
        catch (OperationCanceledException)
        {
            break;
        }
    }
}

async Task<bool> SendSseAsync(SseClientConnection connection, string? eventName, string? eventId, string data)
{
    var entered = false;
    try
    {
        await connection.WriteLock.WaitAsync();
        entered = true;
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(eventId))
        {
            builder.Append("id: ").Append(eventId).Append('\n');
        }
        if (!string.IsNullOrWhiteSpace(eventName))
        {
            builder.Append("event: ").Append(eventName).Append('\n');
        }
        builder.Append("data: ").Append(data).Append("\n\n");
        var bytes = Encoding.UTF8.GetBytes(builder.ToString());
        await connection.Response.Body.WriteAsync(bytes);
        await connection.Response.Body.FlushAsync();
        return true;
    }
    catch
    {
        return false;
    }
    finally
    {
        if (entered)
        {
            connection.WriteLock.Release();
        }
    }
}

Flag CloneFlag(Flag flag)
{
    var rules = flag.Rules is { Count: > 0 } existingRules
        ? existingRules.Select(r => r with { }).ToList()
        : new List<TargetRule>();

    return new Flag
    {
        Id = flag.Id,
        Key = flag.Key,
        Type = flag.Type,
        BoolValue = flag.BoolValue,
        StringValue = flag.StringValue,
        NumberValue = flag.NumberValue,
        Rules = rules,
        UpdatedAt = flag.UpdatedAt
    };
}

void RemoveClient(Guid clientId)
{
    if (!sseClients.TryRemove(clientId, out var connection)) return;

    try
    {
        connection.AbortRegistration.Dispose();
    }
    catch
    {
        // ignore disposal issues
    }

    try
    {
        if (connection.HeartbeatCts is not null)
        {
            connection.HeartbeatCts.Cancel();
            connection.HeartbeatCts.Dispose();
        }
    }
    catch
    {
        // ignore cancellation problems
    }

    try
    {
        connection.WriteLock.Dispose();
    }
    catch
    {
        // ignore disposal issues
    }
}

long NextVersion() => Interlocked.Increment(ref changeVersion);

string ResolveActor(HttpContext context)
    => context.User?.Identity?.IsAuthenticated == true
        ? context.User.Identity?.Name ?? "system"
        : "system";

app.Run();

public partial class Program { }
