using FfaasLite.Api.Contracts;
using FfaasLite.Api.Helpers;
using FfaasLite.Core.Flags;
using FfaasLite.Core.Models;
using FfaasLite.Infrastructure.Cache;
using FfaasLite.Infrastructure.Db;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using System.Text.Json;
using System.Text.Json.Serialization;

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

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/api/flags", async ([FromBody] FlagCreateDto dto, AppDbContext db, RedisCache cache) =>
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

    db.Flags.Add(flag);
    db.Audit.Add(new AuditEntry { Action = "create", FlagKey = flag.Key, DiffJson = new AuditDiff { Before = null, Updated = flag } });
    await db.SaveChangesAsync();

    //TODO: await BroadcastChange(flag);
    await cache.SetAsync($"flag:{flag.Key}", flag, TimeSpan.FromMinutes(5));
    await cache.RemoveAsync("flags:all");
    return Results.Created($"/api/flags/{flag.Key}", flag);
});

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

app.MapPut("/api/flags/{key}", async (string key, [FromBody] FlagUpdateDto dto, AppDbContext db, RedisCache cache) =>
{
    var existing = await db.Flags.FirstOrDefaultAsync(f => f.Key == key);
    if (existing is null) return Results.NotFound();

    var beforeObj = new Flag
    {
        Id = existing.Id,
        Key = existing.Key,
        Type = existing.Type,
        BoolValue = existing.BoolValue,
        StringValue = existing.StringValue,
        NumberValue = existing.NumberValue,
        Rules = [.. existing.Rules],
        UpdatedAt = existing.UpdatedAt
    };

    existing.Type = dto.Type;
    existing.BoolValue = dto.BoolValue;
    existing.StringValue = dto.StringValue;
    existing.NumberValue = dto.NumberValue;
    existing.Rules = dto.Rules ?? new();
    existing.UpdatedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync();

    db.Audit.Add(new AuditEntry { Action = "update", FlagKey = key, DiffJson = new AuditDiff { Before = beforeObj, Updated = existing } });
    await db.SaveChangesAsync();

    //TODO: await BroadcastChange(existing);
    await cache.SetAsync($"flag:{key}", existing, TimeSpan.FromMinutes(5));
    await cache.RemoveAsync("flags:all");
    return Results.Ok(existing);
});

// TODO: Remove or check permissions somehow? 
app.MapDelete("api/flags/{key}", async (string key, AppDbContext db) =>
{
    var existing = await db.Flags.FirstOrDefaultAsync(f => f.Key == key);
    if (existing is null) return Results.NotFound();
    var beforeObj = existing;

    db.Flags.Remove(existing);
    await db.SaveChangesAsync();

    db.Audit.Add(new AuditEntry { Action = "delete", FlagKey = key, DiffJson = new AuditDiff { Before = beforeObj, Updated = null } });
    await db.SaveChangesAsync();

    var cache = app.Services.GetRequiredService<RedisCache>();
    await cache.RemoveAsync($"flag:{key}");
    await cache.RemoveAsync("flags:all");
    return Results.NoContent();
});

app.MapGet("/api/audit", async (AppDbContext db) => Results.Ok(await db.Audit.AsNoTracking().OrderByDescending(a => a.At).Take(100).ToListAsync()));

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

app.Run();