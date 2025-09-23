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
    db.Audit.Add(new AuditEntry { Action = "create", FlagKey = flag.Key, DiffJson = JsonSerializer.Serialize(flag) });
    await db.SaveChangesAsync();
    //TODO await BroadcastChange(flag);
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

app.Run();