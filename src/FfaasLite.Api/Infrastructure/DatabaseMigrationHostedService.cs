using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FfaasLite.Api.Infrastructure;

public sealed class DatabaseMigrationHostedService : IHostedService
{
    private const string SkipEnvVar = "FFAAAS_SKIP_MIGRATIONS";

    private readonly IDatabaseInitializer _initializer;
    private readonly ILogger<DatabaseMigrationHostedService> _logger;
    private readonly IConfiguration _configuration;
    private readonly DatabaseMigrationOptions _options;

    public DatabaseMigrationHostedService(
        IDatabaseInitializer initializer,
        IOptions<DatabaseMigrationOptions> options,
        IConfiguration configuration,
        ILogger<DatabaseMigrationHostedService> logger)
    {
        _initializer = initializer;
        _configuration = configuration;
        _logger = logger;
        _options = options?.Value ?? new DatabaseMigrationOptions();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (ShouldSkip())
        {
            _logger.LogInformation("Database migration automation skipped (configuration flag detected).");
            return;
        }

        var maxAttempts = Math.Max(1, _options.MaxRetryCount);
        var delay = TimeSpan.FromSeconds(Math.Max(0, _options.RetryDelaySeconds));

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await _initializer.InitializeAsync(cancellationToken);
                _logger.LogInformation("Database migration completed on attempt {Attempt}.", attempt);
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts && !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Database migration attempt {Attempt}/{MaxAttempts} failed. Retrying in {DelaySeconds}s.", attempt, maxAttempts, delay.TotalSeconds);
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken);
                }
            }
            catch (Exception ex) when (attempt >= maxAttempts)
            {
                _logger.LogError(ex, "Database migration failed after {MaxAttempts} attempts.", maxAttempts);
                throw;
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;

    private bool ShouldSkip()
    {
        if (_options.Skip)
        {
            return true;
        }

        var envValue = _configuration[SkipEnvVar];
        if (string.IsNullOrWhiteSpace(envValue))
        {
            return false;
        }

        if (bool.TryParse(envValue, out var parsed))
        {
            return parsed;
        }

        return string.Equals(envValue, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(envValue, "yes", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(envValue, "on", StringComparison.OrdinalIgnoreCase);
    }
}
