using FoodMate.Core.Entities;
using FoodMate.Infrastructure.Data.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodMate.Infrastructure.Data.Configurations;

internal sealed class DishConfiguration : IEntityTypeConfiguration<Dish>
{
    public void Configure(EntityTypeBuilder<Dish> b)
    {
        b.ToTable("dishes");
        b.HasKey(x => x.Id);

        b.Property(x => x.Name).HasMaxLength(60).IsRequired();
        b.Property(x => x.Description).HasMaxLength(500);
        b.Property(x => x.ImageUrl).HasMaxLength(500);

        b.Property(x => x.Cuisine).HasConversion<short>();
        b.Property(x => x.Category).HasConversion<short>();
        b.Property(x => x.SpicyLevel).HasConversion<short>();
        b.Property(x => x.MealTimes).HasConversion<short>();
        b.Property(x => x.Seasons).HasConversion<short>();

        b.Property(x => x.Aliases)
            .HasConversion(JsonValueConverters.StringList, JsonValueConverters.StringListComparer);

        b.Property(x => x.Ingredients)
            .HasConversion(JsonValueConverters.IngredientList, JsonValueConverters.IngredientListComparer);

        b.Property(x => x.Tags)
            .HasConversion(JsonValueConverters.StringList, JsonValueConverters.StringListComparer);

        b.ToTable(t => t.HasCheckConstraint("ck_dishes_spicy_range", "spicy_level BETWEEN 0 AND 5"));

        // 决策候选集主查询：内置菜 + 当前用户自定义菜 + 上架 + 未删除
        b.HasIndex(x => new { x.IsActive, x.IsDeleted, x.Cuisine, x.Category })
            .HasDatabaseName("ix_dishes_candidates");

        b.HasIndex(x => x.OwnerUserId)
            .HasDatabaseName("ix_dishes_owner")
            .HasFilter("owner_user_id IS NOT NULL");

        // 冷启动兜底排序
        b.HasIndex(x => x.Popularity)
            .HasDatabaseName("ix_dishes_popularity")
            .IsDescending()
            .HasFilter("is_active AND NOT is_deleted");

        b.HasIndex(x => x.Name)
            .HasDatabaseName("ix_dishes_name");

        b.HasOne(x => x.Owner)
            .WithMany()
            .HasForeignKey(x => x.OwnerUserId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Recipe)
            .WithOne(x => x.Dish)
            .HasForeignKey<Recipe>(x => x.DishId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class RecipeConfiguration : IEntityTypeConfiguration<Recipe>
{
    public void Configure(EntityTypeBuilder<Recipe> b)
    {
        b.ToTable("recipes");
        b.HasKey(x => x.Id);

        b.Property(x => x.Servings).HasConversion<short>();
        b.Property(x => x.CookMinutes).HasConversion<short>();
        b.Property(x => x.Difficulty).HasConversion<short>();
        b.Property(x => x.Tips).HasMaxLength(500);

        b.Property(x => x.Steps)
            .HasConversion(JsonValueConverters.RecipeStepList, JsonValueConverters.RecipeStepListComparer);

        b.ToTable(t => t.HasCheckConstraint("ck_recipes_difficulty", "difficulty BETWEEN 1 AND 3"));

        b.HasIndex(x => x.DishId).IsUnique().HasDatabaseName("ux_recipes_dish_id");
        b.HasIndex(x => x.CookMinutes).HasDatabaseName("ix_recipes_cook_minutes");
    }
}
