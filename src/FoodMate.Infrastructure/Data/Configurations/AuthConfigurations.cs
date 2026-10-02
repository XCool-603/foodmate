using FoodMate.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodMate.Infrastructure.Data.Configurations;

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("refresh_tokens");
        b.HasKey(x => x.Id);

        b.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.Platform).HasConversion<short>();

        // 续期时的唯一查找入口
        b.HasIndex(x => x.TokenHash)
            .IsUnique()
            .HasDatabaseName("ux_refresh_tokens_hash");

        // 登出全部设备 / 风控封禁时按用户批量撤销
        b.HasIndex(x => new { x.UserId, x.ExpiresAt })
            .HasDatabaseName("ix_refresh_tokens_user_expires");

        // 清理过期令牌的后台任务
        b.HasIndex(x => x.ExpiresAt)
            .HasDatabaseName("ix_refresh_tokens_expires");

        b.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
