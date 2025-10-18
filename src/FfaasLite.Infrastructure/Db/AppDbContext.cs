using System.Text.Json;

using FfaasLite.Core.Models;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

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

                var rulesComparer = new ValueComparer<List<TargetRule>>(
                    (l, r) => (l ?? new()).SequenceEqual(r ?? new()),                  // equal
                    v => v == null ? 0 : v.Aggregate(0, (h, it) => HashCode.Combine(h, it.GetHashCode())), // hash
                    v => v == null ? new List<TargetRule>() : v.ToList()               // clone
                );

                e.Property(x => x.Rules)
                    .HasColumnType("jsonb")
                    .HasConversion(
                        v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                        v => JsonSerializer.Deserialize<List<TargetRule>>(v, (JsonSerializerOptions?)null) ?? new()
                    )
                    .Metadata.SetValueComparer(rulesComparer); ;
            });

            modelBuilder.Entity<AuditEntry>(e =>
            {
                e.HasKey(x => x.Id);

                e.Property(x => x.DiffJson)
                    .HasColumnType("jsonb")
                    .HasConversion(
                        v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                        v => JsonSerializer.Deserialize<AuditDiff>(v, (JsonSerializerOptions?)null)!
                    );
            });
        }
    }
}
