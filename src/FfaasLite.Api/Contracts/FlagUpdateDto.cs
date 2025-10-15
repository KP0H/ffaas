using FfaasLite.Core.Models;

namespace FfaasLite.Api.Contracts
{
    public record FlagUpdateDto(
        FlagType Type,
        bool? BoolValue = null,
        string? StringValue = null,
        double? NumberValue = null,
        List<TargetRule>? Rules = null,
        DateTimeOffset? LastKnownUpdatedAt = null
    );
}
