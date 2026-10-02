using FoodMate.Core.Entities;
using FoodMate.Infrastructure.Data.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodMate.Infrastructure.Data.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("users");
        b.HasKey(x => x.Id);

        b.Property(x => x.Nickname).HasMaxLength(50);
        b.Property(x => x.AvatarUrl).HasMaxLength(500);
        b.Property(x => x.Status).HasConversion<short>();

        b.HasOne(x => x.Preference)
            .WithOne(x => x.User)
            .HasForeignKey<UserPreference>(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class UserIdentityConfiguration : IEntityTypeConfiguration<UserIdentity>
{
    public void Configure(EntityTypeBuilder<UserIdentity> b)
    {
        b.ToTable("user_identities");
        b.HasKey(x => x.Id);

        b.Property(x => x.Platform).HasConversion<short>();
        b.Property(x => x.OpenId).HasMaxLength(128).IsRequired();
        b.Property(x => x.UnionId).HasMaxLength(128);

        // 登录查找的唯一入口
        b.HasIndex(x => new { x.Platform, x.OpenId })
            .IsUnique()
            .HasDatabaseName("ux_user_identities_platform_openid");

        b.HasIndex(x => x.UserId)
            .HasDatabaseName("ix_user_identities_user_id");

        // 为 v2 跨端账号合并预留
        b.HasIndex(x => x.UnionId)
            .HasDatabaseName("ix_user_identities_union_id")
            .HasFilter("union_id IS NOT NULL");

        b.HasOne(x => x.User)
            .WithMany(x => x.Identities)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class UserPreferenceConfiguration : IEntityTypeConfiguration<UserPreference>
{
    public void Configure(EntityTypeBuilder<UserPreference> b)
    {
        b.ToTable("user_preferences");
        b.HasKey(x => x.UserId);

        b.Property(x => x.SpicyLevel).HasConversion<short>();

        b.Property(x => x.AvoidIngredients)
            .HasConversion(JsonValueConverters.StringList, JsonValueConverters.StringListComparer);

        b.Property(x => x.PreferredCuisines)
            .HasConversion(JsonValueConverters.CuisineList, JsonValueConverters.CuisineListComparer);

        b.Property(x => x.DiningModeWeights)
            .HasConversion(
                JsonValueConverters.DiningModeWeightsValue,
                JsonValueConverters.DiningModeWeightsComparer);

        b.ToTable(t => t.HasCheckConstraint(
            "ck_user_preferences_spicy_range",
            "spicy_level BETWEEN 0 AND 5"));

        b.ToTable(t => t.HasCheckConstraint(
            "ck_user_preferences_budget_range",
            "budget_min_cents >= 0 AND budget_max_cents >= budget_min_cents"));
    }
}
