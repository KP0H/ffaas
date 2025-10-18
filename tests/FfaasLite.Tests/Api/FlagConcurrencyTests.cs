using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using FfaasLite.Api.Contracts;
using FfaasLite.Core.Models;
using FfaasLite.Infrastructure.Db;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FfaasLite.Tests.Api;

public class FlagConcurrencyTests
{
    [Fact]
    public async Task PutFlag_WithMatchingTimestamp_Succeeds()
    {
        using var factory = new TestApiFactory();
        await factory.ResetStateAsync();

        var seeded = await SeedFlagAsync(factory, "feature", flag => flag.BoolValue = true);

        using var editorClient = CreateEditorClient(factory);
        var updateDto = new FlagUpdateDto(
            Type: FlagType.Boolean,
            BoolValue: false,
            LastKnownUpdatedAt: seeded.UpdatedAt);
        var updateResponse = await editorClient.PutAsJsonAsync("/api/flags/feature", updateDto);

        updateResponse.EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var fetched = await db.Flags.AsNoTracking().FirstOrDefaultAsync(f => f.Key == "feature");
        Assert.NotNull(fetched);
        Assert.False(fetched!.BoolValue);
        Assert.NotEqual(seeded.UpdatedAt, fetched.UpdatedAt);

        Assert.NotNull(fetched);
        Assert.False(fetched!.BoolValue);
    }

    [Fact]
    public async Task PutFlag_WithStaleTimestamp_Returns409()
    {
        using var factory = new TestApiFactory();
        await factory.ResetStateAsync();

        var seeded = await SeedFlagAsync(factory, "feature-conflict", flag => flag.BoolValue = true);

        using var editorClient = CreateEditorClient(factory);
        var firstUpdate = new FlagUpdateDto(FlagType.Boolean, BoolValue: false, LastKnownUpdatedAt: seeded.UpdatedAt);
        var firstResponse = await editorClient.PutAsJsonAsync("/api/flags/feature-conflict", firstUpdate);
        firstResponse.EnsureSuccessStatusCode();
        var staleUpdate = new FlagUpdateDto(FlagType.Boolean, BoolValue: true, LastKnownUpdatedAt: seeded.UpdatedAt);
        var staleResponse = await editorClient.PutAsJsonAsync("/api/flags/feature-conflict", staleUpdate);

        Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);
    }

    [Fact]
    public async Task PutFlag_WithoutTimestamp_Returns400()
    {
        using var factory = new TestApiFactory();
        await factory.ResetStateAsync();

        await SeedFlagAsync(factory, "feature-missing", flag => flag.BoolValue = true);

        using var editorClient = CreateEditorClient(factory);

        var update = new
        {
            type = "boolean",
            boolValue = false
        };

        var response = await editorClient.PutAsJsonAsync("/api/flags/feature-missing", update);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<Flag> SeedFlagAsync(TestApiFactory factory, string key, Action<Flag>? configure = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var flag = new Flag
        {
            Key = key,
            Type = FlagType.Boolean,
            BoolValue = true
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
}

