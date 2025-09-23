namespace FfaasLite.Core.Models
{
    public class AuditEntry
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Actor { get; set; } = "system";
        public string Action { get; set; } = string.Empty; // create/update/delete
        public string FlagKey { get; set; } = string.Empty;
        public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
        public string? DiffJson { get; set; }
    }
}
