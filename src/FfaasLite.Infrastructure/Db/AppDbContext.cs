using FfaasLite.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System.Text.Json;

namespace FfaasLite.Infrastructure.Db
{
    public class AppDbContext : DbContext
    {
        public DbSet<Flag> Flags => Set<Flag>();
        public DbSet<AuditEntry> Audit => Set<AuditEntry>();

        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Flag>(e =>
            {
                e.HasKey(x => x.Id);
                e.HasIndex(x => x.Key).IsUnique();
                e.Property(x => x.Type).HasConversion(new EnumToStringConverter<FlagType>());
                e.Property(x => x.Rules)
                    .HasColumnType("jsonb")
                    .HasConversion(
                        v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                        v => JsonSerializer.Deserialize<List<TargetRule>>(v, (JsonSerializerOptions?)null) ?? new()
                    );
            });
            modelBuilder.Entity<AuditEntry>(e => e.HasKey(x => x.Id));
        }
    }
}
