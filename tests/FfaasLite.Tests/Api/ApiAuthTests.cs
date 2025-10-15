using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using FfaasLite.Api.Contracts;
using FfaasLite.Api.Security;
using FfaasLite.Core.Models;
using FfaasLite.Infrastructure.Db;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FfaasLite.Tests.Api;

public class ApiAuthTests
{
    [Fact]
    public async Task PostFlags_WithoutApiKey_Returns401()
    {
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient();
        await factory.ResetStateAsync();

        var response = await client.PostAsJsonAsync("/api/flags", CreateFlagPayload("flag-no-key"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostFlags_WithReaderKey_Returns403()
    {
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient();
        await factory.ResetStateAsync();

        var options = factory.Services.GetRequiredService<IOptionsMonitor<ApiKeyAuthenticationOptions>>().CurrentValue;
        Assert.Contains(options.ApiKeys, k => string.Equals(k.Key, "dev-reader-token", StringComparison.Ordinal));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "dev-reader-token");
        client.DefaultRequestHeaders.Add("X-Api-Key", "dev-reader-token");
        var response = await client.PostAsJsonAsync("/api/flags", CreateFlagPayload("flag-reader-key"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostFlags_WithEditorKey_Returns201()
    {
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient();
        await factory.ResetStateAsync();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "dev-editor-token");
        client.DefaultRequestHeaders.Add("X-Api-Key", "dev-editor-token");
        var response = await client.PostAsJsonAsync("/api/flags", CreateFlagPayload("flag-editor-key"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task PostFlags_WithHashedKey_Returns201()
    {
        var hashedKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("hashed-editor-token")));
        using var factory = new TestApiFactory(services =>
        {
            services.PostConfigure<ApiKeyAuthenticationOptions>(options =>
            {
                options.ApiKeys.Add(new ApiKeyCredential
                {
                    Name = "hashed-editor",
                    Hash = hashedKey,
                    Roles = new[] { AuthConstants.Roles.Editor }
                });
            });
            services.PostConfigure<ApiKeyAuthenticationOptions>(AuthConstants.Schemes.ApiKey, options =>
            {
                options.ApiKeys.Add(new ApiKeyCredential
                {
                    Name = "hashed-editor",
                    Hash = hashedKey,
                    Roles = new[] { AuthConstants.Roles.Editor }
                });
            });
        });

        using var client = factory.CreateClient();
        await factory.ResetStateAsync();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "hashed-editor-token");
        client.DefaultRequestHeaders.Add("X-Api-Key", "hashed-editor-token");
        var response = await client.PostAsJsonAsync("/api/flags", CreateFlagPayload("flag-hash-key"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static FlagCreateDto CreateFlagPayload(string key) => new(key, FlagType.Boolean, BoolValue: true);
}




