namespace FfaasLite.Core.Models
{
    public record TargetRule(
        string Attribute,
        string Operator,
        string Value,
        int? Priority = null,
        bool? BoolOverride = null,
        string? StringOverride = null,
        double? NumberOverride = null
    );
}
