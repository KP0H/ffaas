using System.Net.Http.Headers;
using System.Net.Http.Json;

using FfaasLite.Api.Contracts;
using FfaasLite.Api.Security;
using FfaasLite.Core.Models;
using FfaasLite.Infrastructure.Db;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FfaasLite.Tests.Api;

public class AuditLogTests
{
    [Fact]
    public async Task PostFlag_WritesCreateAuditWithActor()
    {
        using var factory = new TestApiFactory();
        await factory.ResetStateAsync();

        using var client = CreateEditorClient(factory);
        var response = await client.PostAsJsonAsync("/api/flags", new FlagCreateDto("audit-create", FlagType.Boolean, BoolValue: true));
        response.EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entry = await db.Audit.AsNoTracking()
            .SingleAsync(a => a.FlagKey == "audit-create" && a.Action == "create");

        Assert.Equal(ResolveExpectedActor(factory), entry.Actor);
        Assert.NotNull(entry.DiffJson);
        Assert.Null(entry.DiffJson!.Before);
        Assert.NotNull(entry.DiffJson!.Updated);
        Assert.True(entry.DiffJson!.Updated!.BoolValue);
    }

    [Fact]
    public async Task PutFlag_WritesUpdateAuditDiff()
    {
        using var factory = new TestApiFactory();
        await factory.ResetStateAsync();

        var seeded = await SeedFlagAsync(factory, "audit-update", flag =>
        {
            flag.Type = FlagType.Boolean;
            flag.BoolValue = false;
        });

        using var client = CreateEditorClient(factory);
        var update = new FlagUpdateDto(
            Type: FlagType.Boolean,
            BoolValue: true,
            LastKnownUpdatedAt: seeded.UpdatedAt);

        var response = await client.PutAsJsonAsync("/api/flags/audit-update", update);
        response.EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entry = await db.Audit.AsNoTracking()
            .OrderByDescending(a => a.At)
            .FirstAsync(a => a.FlagKey == "audit-update" && a.Action == "update");

        Assert.Equal(ResolveExpectedActor(factory), entry.Actor);
        Assert.NotNull(entry.DiffJson);
        Assert.NotNull(entry.DiffJson!.Before);
        Assert.False(entry.DiffJson!.Before!.BoolValue);
        Assert.NotNull(entry.DiffJson!.Updated);
        Assert.True(entry.DiffJson!.Updated!.BoolValue);
        Assert.NotEqual(entry.DiffJson!.Before!.UpdatedAt, entry.DiffJson!.Updated!.UpdatedAt);
    }

    [Fact]
    public async Task DeleteFlag_WritesDeleteAuditDiff()
    {
        using var factory = new TestApiFactory();
        await factory.ResetStateAsync();

        await SeedFlagAsync(factory, "audit-delete", flag =>
        {
            flag.Type = FlagType.Boolean;
            flag.BoolValue = true;
        });

        using var client = CreateEditorClient(factory);
        var response = await client.DeleteAsync("/api/flags/audit-delete");
        response.EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entry = await db.Audit.AsNoTracking()
            .SingleAsync(a => a.FlagKey == "audit-delete" && a.Action == "delete");

        Assert.Equal(ResolveExpectedActor(factory), entry.Actor);
        Assert.NotNull(entry.DiffJson);
        Assert.NotNull(entry.DiffJson!.Before);
        Assert.Null(entry.DiffJson!.Updated);
        Assert.True(entry.DiffJson!.Before!.BoolValue);

        var flag = await db.Flags.FirstOrDefaultAsync(f => f.Key == "audit-delete");
        Assert.Null(flag);
    }

    private static async Task<Flag> SeedFlagAsync(TestApiFactory factory, string key, Action<Flag>? configure = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var flag = new Flag
        {
            Key = key,
            Type = FlagType.Boolean,
            BoolValue = false
        };
        configure?.Invoke(flag);
        db.Flags.Add(flag);
        await db.SaveChangesAsync();
        return flag;
    }

    private static HttpClient CreateEditorClient(TestApiFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "dev-editor-token");
        client.DefaultRequestHeaders.Add("X-Api-Key", "dev-editor-token");
        return client;
    }

    private static string ResolveExpectedActor(TestApiFactory factory)
    {
        var optionsMonitor = factory.Services.GetRequiredService<IOptionsMonitor<ApiKeyAuthenticationOptions>>();
        var candidates = new List<ApiKeyCredential>();

        void Capture(ApiKeyAuthenticationOptions options)
        {
            foreach (var credential in options.ApiKeys)
            {
                if (credential is not null)
                {
                    candidates.Add(credential);
                }
            }
        }

        Capture(optionsMonitor.CurrentValue);
        Capture(optionsMonitor.Get(AuthConstants.Schemes.ApiKey));

        var match = candidates.FirstOrDefault(c => string.Equals(c.Key, "dev-editor-token", StringComparison.Ordinal));
        return string.IsNullOrWhiteSpace(match?.Name) ? "dev-editor-token" : match!.Name;
    }
}
