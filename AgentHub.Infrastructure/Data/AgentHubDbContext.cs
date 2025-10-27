using AgentHub.Core.Domain.Enums;
using AgentHub.Core.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace AgentHub.Infrastructure.Data;

/// <summary>
/// AgentHub数据库上下文
/// </summary>
public class AgentHubDbContext : DbContext
{
    public AgentHubDbContext(DbContextOptions<AgentHubDbContext> options)
        : base(options)
    {
    }

    // DbSet定义
    public DbSet<User> Users { get; set; }
    public DbSet<UserProfile> UserProfiles { get; set; }
    public DbSet<Conversation> Conversations { get; set; }
    public DbSet<Message> Messages { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 配置User实体
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Username)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(e => e.PasswordHash)
                .IsRequired()
                .HasMaxLength(255);

            entity.Property(e => e.Email)
                .HasMaxLength(100);

            entity.Property(e => e.Phone)
                .HasMaxLength(20);

            entity.Property(e => e.AvatarUrl)
                .HasMaxLength(500);

            entity.HasIndex(e => e.Username).IsUnique();
            entity.HasIndex(e => e.Email);

            // 配置与UserProfile的一对一关系
            entity.HasOne(e => e.Profile)
                .WithOne(p => p.User)
                .HasForeignKey<UserProfile>(p => p.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // 配置与Conversation的一对多关系
            entity.HasMany(e => e.Conversations)
                .WithOne(c => c.User)
                .HasForeignKey(c => c.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // 配置UserProfile实体
        modelBuilder.Entity<UserProfile>(entity =>
        {
            entity.ToTable("user_profiles");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.FullName)
                .HasMaxLength(100);

            entity.Property(e => e.Gender)
                .HasConversion(
                    v => v.ToString(),
                    v => Enum.Parse<Gender>(v))
                .HasMaxLength(10);

            entity.Property(e => e.BirthLunar)
                .HasMaxLength(50);

            entity.Property(e => e.BirthProvince)
                .HasMaxLength(50);

            entity.Property(e => e.BirthCity)
                .HasMaxLength(50);

            entity.Property(e => e.BirthLongitude)
                .HasPrecision(10, 6);

            entity.Property(e => e.BirthLatitude)
                .HasPrecision(10, 6);

            entity.Property(e => e.MaritalStatus)
                .HasConversion(
                    v => v.ToString(),
                    v => Enum.Parse<MaritalStatus>(v))
                .HasMaxLength(20);

            entity.Property(e => e.Occupation)
                .HasMaxLength(100);

            entity.Property(e => e.Education)
                .HasMaxLength(50);

            entity.Property(e => e.FocusAreasJson)
                .HasColumnType("json");

            entity.Property(e => e.ImportantEventsJson)
                .HasColumnType("json");

            entity.Property(e => e.BaziInfoJson)
                .HasColumnType("json");

            entity.Property(e => e.ZiweiInfoJson)
                .HasColumnType("json");

            entity.Property(e => e.AstroInfoJson)
                .HasColumnType("json");

            entity.Ignore(e => e.FocusAreas); // 不映射到数据库
        });

        // 配置Conversation实体
        modelBuilder.Entity<Conversation>(entity =>
        {
            entity.ToTable("conversations");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.AgentType)
                .IsRequired()
                .HasConversion(
                    v => v.ToString(),
                    v => Enum.Parse<AgentType>(v))
                .HasMaxLength(50);

            entity.Property(e => e.Title)
                .HasMaxLength(255);

            entity.Property(e => e.Status)
                .IsRequired()
                .HasConversion(
                    v => v.ToString(),
                    v => Enum.Parse<ConversationStatus>(v))
                .HasMaxLength(20)
                .HasDefaultValue(ConversationStatus.Active);

            entity.Property(e => e.ContextSummary)
                .HasColumnType("text");

            entity.Property(e => e.MetadataJson)
                .HasColumnType("json");

            entity.HasIndex(e => new { e.UserId, e.Status });
            entity.HasIndex(e => e.LastMessageAt);

            // 配置与Message的一对多关系
            entity.HasMany(e => e.Messages)
                .WithOne(m => m.Conversation)
                .HasForeignKey(m => m.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // 配置Message实体
        modelBuilder.Entity<Message>(entity =>
        {
            entity.ToTable("messages");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Role)
                .IsRequired()
                .HasConversion(
                    v => v.ToString(),
                    v => Enum.Parse<MessageRole>(v))
                .HasMaxLength(20);

            entity.Property(e => e.Content)
                .IsRequired()
                .HasColumnType("text");

            entity.Property(e => e.MetadataJson)
                .HasColumnType("json");

            entity.Property(e => e.ModelVersion)
                .HasMaxLength(50);

            entity.HasIndex(e => new { e.ConversationId, e.CreatedAt });
        });
    }
}
