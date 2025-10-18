using System;
using System.Threading;
using System.Threading.Tasks;

using FfaasLite.Infrastructure.Db;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FfaasLite.Api.Infrastructure;

public sealed class DatabaseInitializer : IDatabaseInitializer
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(IServiceProvider serviceProvider, ILogger<DatabaseInitializer> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (dbContext.Database.IsRelational())
        {
            _logger.LogInformation("Applying EF Core migrations (provider: {Provider}).", dbContext.Database.ProviderName);
            await dbContext.Database.MigrateAsync(cancellationToken);
            _logger.LogInformation("EF Core migrations applied successfully.");
        }
        else
        {
            _logger.LogInformation("Ensuring database is created for provider {Provider}.", dbContext.Database.ProviderName);
            await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        }
    }
}
