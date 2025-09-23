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
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseNpgsql(cfg.GetConnectionString("postgres")));

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(cfg.GetConnectionString("redis")!));

builder.Services.AddSingleton<RedisCache>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/api/flags", async ([FromBody] Flag flag, AppDbContext db) =>
{
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

app.MapPut("/api/flags/{key}", async (string key, [FromBody] Flag updated, AppDbContext db) =>
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

    existing.Type = updated.Type;
    existing.BoolValue = updated.BoolValue;
    existing.StringValue = updated.StringValue;
    existing.NumberValue = updated.NumberValue;
    existing.Rules = updated.Rules;
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

app.Run();