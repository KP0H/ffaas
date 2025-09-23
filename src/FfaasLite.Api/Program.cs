using FfaasLite.Api.Contracts;
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

app.MapPost("/api/flags", async ([FromBody] FlagCreateDto dto, AppDbContext db) =>
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
    return Results.Created($"/api/flags/{flag.Key}", flag);
});

app.MapGet("/api/flags", async (AppDbContext db) =>
{
    var flags = await db.Flags.AsNoTracking().ToListAsync();
    return Results.Ok(flags);
});

app.MapGet("/api/flags/{key}", async (string key, AppDbContext db) =>
{
    var flag = await db.Flags.AsNoTracking().FirstOrDefaultAsync(f => f.Key == key);
    return flag is null ? Results.NotFound() : Results.Ok(flag);
});

app.MapPut("/api/flags/{key}", async (string key, [FromBody] FlagUpdateDto dto, AppDbContext db) =>
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

    return Results.NoContent();
});

app.MapGet("/api/audit", async (AppDbContext db) => Results.Ok(await db.Audit.AsNoTracking().OrderByDescending(a => a.At).Take(100).ToListAsync()));

app.MapPost("/api/evaluate/{key}", async (string key, [FromBody] EvalContext ctx, [FromServices] AppDbContext db, [FromServices] IFlagEvaluator eval) =>
{
    var flag = await db.Flags.AsNoTracking().FirstOrDefaultAsync(f => f.Key == key);
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