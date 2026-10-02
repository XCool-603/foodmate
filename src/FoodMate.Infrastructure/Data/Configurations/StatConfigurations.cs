using FoodMate.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodMate.Infrastructure.Data.Configurations;

internal sealed class UserDishStatConfiguration : IEntityTypeConfiguration<UserDishStat>
{
    public void Configure(EntityTypeBuilder<UserDishStat> b)
    {
        b.ToTable("user_dish_stats");

        // 联合主键
        b.HasKey(x => new { x.UserId, x.DishId });

        b.Ignore(x => x.AverageRating);

        b.HasIndex(x => new { x.UserId, x.LastEatenAt })
            .HasDatabaseName("ix_user_dish_stats_last_eaten")
            .IsDescending(false, true);

        b.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Dish)
            .WithMany()
            .HasForeignKey(x => x.DishId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class UserCuisineStatConfiguration : IEntityTypeConfiguration<UserCuisineStat>
{
    public void Configure(EntityTypeBuilder<UserCuisineStat> b)
    {
        b.ToTable("user_cuisine_stats");

        // 联合主键
        b.HasKey(x => new { x.UserId, x.Cuisine });

        b.Property(x => x.Cuisine).HasConversion<short>();

        b.HasIndex(x => new { x.UserId, x.LastEatenAt })
            .HasDatabaseName("ix_user_cuisine_stats_last_eaten")
            .IsDescending(false, true);

        b.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
