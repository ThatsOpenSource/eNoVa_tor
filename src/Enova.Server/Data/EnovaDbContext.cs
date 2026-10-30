using Microsoft.EntityFrameworkCore;

namespace Lragrz.Enova.Server.Data;

public sealed class EnovaDbContext : DbContext
{
    public EnovaDbContext(DbContextOptions<EnovaDbContext> options) : base(options)
    {
    }

    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<OrganisationEntity> Organisations => Set<OrganisationEntity>();
    public DbSet<AgencyEntity> Agencies => Set<AgencyEntity>();
    public DbSet<ConfigVersionEntity> ConfigVersions => Set<ConfigVersionEntity>();
    public DbSet<UploadQueueEntity> UploadQueue => Set<UploadQueueEntity>();
    public DbSet<ProcessedCaseEntity> ProcessedCases => Set<ProcessedCaseEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OrganisationEntity>(e =>
        {
            e.ToTable("organisations");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(128).IsRequired();
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.DefaultAuthority).HasColumnName("default_authority").HasMaxLength(64);
            e.Property(x => x.DefaultSender).HasColumnName("default_sender").HasMaxLength(128);
            e.Property(x => x.DefaultUploadEnabled).HasColumnName("default_upload_enabled").HasDefaultValue(true);
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<AgencyEntity>(e =>
        {
            e.ToTable("agencies");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.OrganisationId).HasColumnName("organisation_id");
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(128).IsRequired();
            e.HasIndex(x => new { x.OrganisationId, x.Name }).IsUnique();
            e.Property(x => x.AuthorityIdentifier).HasColumnName("authority_identifier").HasMaxLength(64);
            e.Property(x => x.SenderName).HasColumnName("sender_name").HasMaxLength(128);
            e.Property(x => x.CryptoProvider).HasColumnName("crypto_provider").HasMaxLength(32);
            e.Property(x => x.DmsType).HasColumnName("dms_type").HasMaxLength(32).HasDefaultValue("none");
            e.Property(x => x.DmsConfigJson).HasColumnName("dms_config_json").HasColumnType("jsonb");
            e.Property(x => x.LocalOutputDirectory).HasColumnName("local_output_directory").HasMaxLength(512);
            e.Property(x => x.OutputAntworten).HasColumnName("output_antworten").HasMaxLength(128);
            e.Property(x => x.OutputAblage).HasColumnName("output_ablage").HasMaxLength(128);
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasOne(x => x.Organisation)
                .WithMany(o => o.Agencies)
                .HasForeignKey(x => x.OrganisationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserEntity>(e =>
        {
            e.ToTable("users");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Username).HasColumnName("username").HasMaxLength(64).IsRequired();
            e.HasIndex(x => x.Username).IsUnique();
            e.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(128).IsRequired();
            e.Property(x => x.PasswordHash).HasColumnName("password_hash").IsRequired();
            e.Property(x => x.Role).HasColumnName("role").HasMaxLength(16).IsRequired();
            e.Property(x => x.OrganisationId).HasColumnName("organisation_id");
            e.Property(x => x.AgencyId).HasColumnName("agency_id");
            e.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true);
            e.Property(x => x.LastLoginAt).HasColumnName("last_login_at");
            e.Property(x => x.LastLogoutAt).HasColumnName("last_logout_at");
            e.Property(x => x.IsSessionActive).HasColumnName("is_session_active").HasDefaultValue(false);
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasOne(x => x.Organisation)
                .WithMany(o => o.Users)
                .HasForeignKey(x => x.OrganisationId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Agency)
                .WithMany(a => a.Users)
                .HasForeignKey(x => x.AgencyId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ProcessedCaseEntity>(e =>
        {
            e.ToTable("processed_cases");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.CaseId).HasColumnName("case_id").HasMaxLength(128).IsRequired();
            e.HasIndex(x => x.CaseId).IsUnique();
            e.Property(x => x.SignatureHash).HasColumnName("signature_hash").HasMaxLength(128);
            e.Property(x => x.SpecificsHash).HasColumnName("specifics_hash").HasMaxLength(128);
            e.Property(x => x.ProcessedBy).HasColumnName("processed_by").HasMaxLength(128);
            e.Property(x => x.ProcessedAt).HasColumnName("processed_at");
        });

        modelBuilder.Entity<UploadQueueEntity>(e =>
        {
            e.ToTable("upload_queue");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.OrganisationId).HasColumnName("organisation_id");
            e.Property(x => x.FileName).HasColumnName("file_name").HasMaxLength(256);
            e.Property(x => x.LocalPath).HasColumnName("local_path").HasMaxLength(512);
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(32);
            e.Property(x => x.Attempts).HasColumnName("attempts");
            e.Property(x => x.LastError).HasColumnName("last_error");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<ConfigVersionEntity>(e =>
        {
            e.ToTable("config_versions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.VersionTag).HasColumnName("version_tag").HasMaxLength(32).IsRequired();
            e.HasIndex(x => x.VersionTag).IsUnique();
            e.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
            e.Property(x => x.CreatedBy).HasColumnName("created_by");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasOne<UserEntity>()
                .WithMany()
                .HasForeignKey(x => x.CreatedBy)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}

public sealed class OrganisationEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? DefaultAuthority { get; set; }
    public string? DefaultSender { get; set; }
    public bool DefaultUploadEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public List<AgencyEntity> Agencies { get; set; } = new();
    public List<UserEntity> Users { get; set; } = new();
}

public sealed class AgencyEntity
{
    public Guid Id { get; set; }
    public Guid OrganisationId { get; set; }
    public OrganisationEntity? Organisation { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? AuthorityIdentifier { get; set; }
    public string? SenderName { get; set; }
    public string CryptoProvider { get; set; } = "MOCK_FALLBACK";
    public string DmsType { get; set; } = "none";
    public string? DmsConfigJson { get; set; }
    public string? LocalOutputDirectory { get; set; }
    public string? OutputAntworten { get; set; }
    public string? OutputAblage { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<UserEntity> Users { get; set; } = new();
}

public sealed class UserEntity
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Role { get; set; } = "editor"; // manager | admin | editor
    public Guid? OrganisationId { get; set; }
    public OrganisationEntity? Organisation { get; set; }
    public Guid? AgencyId { get; set; }
    public AgencyEntity? Agency { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAt { get; set; }
    public DateTime? LastLogoutAt { get; set; }
    public bool IsSessionActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class ProcessedCaseEntity
{
    public Guid Id { get; set; }
    public string CaseId { get; set; } = "";
    public string? SignatureHash { get; set; }
    public string? SpecificsHash { get; set; }
    public string? ProcessedBy { get; set; }
    public DateTime ProcessedAt { get; set; }
}

public sealed class UploadQueueEntity
{
    public Guid Id { get; set; }
    public Guid? OrganisationId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string LocalPath { get; set; } = string.Empty;
    public string Status { get; set; } = "pending"; // pending | uploaded | failed
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class ConfigVersionEntity
{
    public int Id { get; set; }
    public string VersionTag { get; set; } = string.Empty;
    public string Payload { get; set; } = "{}";
    public Guid? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}

