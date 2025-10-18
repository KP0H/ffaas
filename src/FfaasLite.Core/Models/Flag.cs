namespace FfaasLite.Core.Models
{
    public class Flag
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Key { get; set; } = string.Empty;
        public FlagType Type { get; set; }
        public bool? BoolValue { get; set; }
        public string? StringValue { get; set; }
        public double? NumberValue { get; set; }
        public List<TargetRule> Rules { get; set; } = new();
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
