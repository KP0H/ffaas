using FfaasLite.Core.Models;

namespace FfaasLite.Api.Contracts
{
    public record FlagCreateDto(
        string Key,
        FlagType Type,
        bool? BoolValue = null,
        string? StringValue = null,
        double? NumberValue = null,
        List<TargetRule>? Rules = null
    );
}
