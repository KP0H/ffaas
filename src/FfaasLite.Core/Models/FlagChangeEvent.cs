namespace FfaasLite.Core.Models
{
    public enum FlagChangeType
    {
        Created,
        Updated,
        Deleted
    }

    public sealed class FlagChangePayload
    {
        public string Key { get; init; } = string.Empty;

        public Flag? Flag { get; init; }
    }

    public sealed class FlagChangeEvent
    {
        public FlagChangeType Type { get; init; }

        public long Version { get; init; }

        public required FlagChangePayload Payload { get; init; }
    }
}
