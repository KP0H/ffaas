namespace FfaasLite.Api.Infrastructure;

public sealed class DatabaseMigrationOptions
{
    public bool Skip { get; set; }

    public int MaxRetryCount { get; set; } = 5;

    public int RetryDelaySeconds { get; set; } = 5;
}
