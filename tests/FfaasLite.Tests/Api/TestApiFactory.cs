using FfaasLite.Api.Contracts;
using FfaasLite.Api.Security;
using FfaasLite.Core.Models;
using FfaasLite.Infrastructure.Db;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using StackExchange.Redis;

namespace FfaasLite.Tests.Api;

internal class TestApiFactory : WebApplicationFactory<Program>
{
    private readonly Action<IServiceCollection>? _configureServices;
    private readonly Action<IConfigurationBuilder>? _configureConfiguration;
    private readonly string _databaseName = $"test-db-{Guid.NewGuid()}";

    public TestApiFactory(
        Action<IServiceCollection>? configureServices = null,
        Action<IConfigurationBuilder>? configureConfiguration = null)
    {
        _configureServices = configureServices;
        _configureConfiguration = configureConfiguration;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        if (_configureConfiguration is not null)
        {
            builder.ConfigureAppConfiguration((_, configurationBuilder) =>
            {
                _configureConfiguration(configurationBuilder);
            });
        }

        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(DbContextOptions<AppDbContext>));
            services.RemoveAll(typeof(AppDbContext));
            services.AddDbContext<AppDbContext>(opt => opt.UseInMemoryDatabase(_databaseName));

            services.RemoveAll(typeof(IConnectionMultiplexer));
            var database = Substitute.For<IDatabase>();
            database.StringSetAsync(
                    Arg.Any<RedisKey>(),
                    Arg.Any<RedisValue>(),
                    Arg.Any<TimeSpan?>(),
                    Arg.Any<When>(),
                    Arg.Any<CommandFlags>())
                .Returns(Task.FromResult(true));
            database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
                .Returns(Task.FromResult(RedisValue.Null));
            database.KeyDeleteAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
                .Returns(Task.FromResult(true));

            var multiplexer = Substitute.For<IConnectionMultiplexer>();
            multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>())
                .Returns(database);
            services.AddSingleton(multiplexer);

            void SeedKeys(ApiKeyAuthenticationOptions options)
            {
                if (!options.ApiKeys.Any())
                {
                    options.ApiKeys.Add(new ApiKeyCredential
                    {
                        Name = "test-reader",
                        Key = "dev-reader-token",
                        Roles = new[] { AuthConstants.Roles.Reader }
                    });
                    options.ApiKeys.Add(new ApiKeyCredential
                    {
                        Name = "test-editor",
                        Key = "dev-editor-token",
                        Roles = new[] { AuthConstants.Roles.Editor }
                    });
                }
            }

            services.PostConfigure<ApiKeyAuthenticationOptions>(options => SeedKeys(options));
            services.PostConfigure<ApiKeyAuthenticationOptions>(AuthConstants.Schemes.ApiKey, options => SeedKeys(options));

            _configureServices?.Invoke(services);
        });
    }

    public async Task ResetStateAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
    }
}

