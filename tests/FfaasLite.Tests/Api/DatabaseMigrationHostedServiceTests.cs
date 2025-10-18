using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using FfaasLite.Api.Infrastructure;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NSubstitute;

using Xunit;

namespace FfaasLite.Tests.Api;

public class DatabaseMigrationHostedServiceTests
{
    [Fact]
    public async Task StartAsync_RunsInitializer_WhenNotSkipped()
    {
        var initializer = new FakeInitializer();
        var service = CreateService(initializer);

        await service.StartAsync(CancellationToken.None);

        Assert.Equal(1, initializer.CallCount);
    }

    [Fact]
    public async Task StartAsync_Skips_WhenConfigured()
    {
        var initializer = new FakeInitializer();
        var options = new DatabaseMigrationOptions
        {
            Skip = true
        };
        var service = CreateService(initializer, configuration: null, options);

        await service.StartAsync(CancellationToken.None);

        Assert.Equal(0, initializer.CallCount);
    }

    [Fact]
    public async Task StartAsync_Skips_WhenEnvironmentVariableSet()
    {
        var initializer = new FakeInitializer();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FFAAAS_SKIP_MIGRATIONS"] = "true"
            })
            .Build();
        var service = CreateService(initializer, configuration);

        await service.StartAsync(CancellationToken.None);

        Assert.Equal(0, initializer.CallCount);
    }

    [Fact]
    public async Task StartAsync_RetriesUntilSuccess()
    {
        var initializer = new FakeInitializer(new InvalidOperationException(), new InvalidOperationException(), null);
        var options = new DatabaseMigrationOptions
        {
            MaxRetryCount = 5,
            RetryDelaySeconds = 0
        };
        var service = CreateService(initializer, configuration: null, options);

        await service.StartAsync(CancellationToken.None);

        Assert.Equal(3, initializer.CallCount);
    }

    private static DatabaseMigrationHostedService CreateService(
        FakeInitializer initializer,
        IConfiguration? configuration = null,
        DatabaseMigrationOptions? options = null)
    {
        var logger = Substitute.For<ILogger<DatabaseMigrationHostedService>>();
        var optionsWrapper = Substitute.For<IOptions<DatabaseMigrationOptions>>();
        optionsWrapper.Value.Returns(options ?? new DatabaseMigrationOptions());
        configuration ??= new ConfigurationBuilder().Build();

        return new DatabaseMigrationHostedService(initializer, optionsWrapper, configuration, logger);
    }

    private sealed class FakeInitializer : IDatabaseInitializer
    {
        private readonly Queue<Exception?> _results;

        public FakeInitializer(params Exception?[] results)
        {
            _results = new Queue<Exception?>(results.Length == 0 ? new[] { (Exception?)null } : results);
        }

        public int CallCount { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            var next = _results.Count > 0 ? _results.Dequeue() : null;
            if (next is not null)
            {
                throw next;
            }

            return Task.CompletedTask;
        }
    }
}
