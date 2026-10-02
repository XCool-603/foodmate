using FoodMate.Core.Entities;
using FoodMate.Infrastructure.Data.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodMate.Infrastructure.Data.Configurations;

internal sealed class MealRecordConfiguration : IEntityTypeConfiguration<MealRecord>
{
    public void Configure(EntityTypeBuilder<MealRecord> b)
    {
        b.ToTable("meal_records");
        b.HasKey(x => x.Id);

        b.Property(x => x.DishName).HasMaxLength(60).IsRequired();
        b.Property(x => x.Note).HasMaxLength(200);
        b.Property(x => x.PhotoUrl).HasMaxLength(500);

        b.Property(x => x.MealType).HasConversion<short>();
        b.Property(x => x.DiningMode).HasConversion<short>();
        b.Property(x => x.Source).HasConversion<short>();
        b.Property(x => x.Rating).HasConversion<short>();

        b.Property(x => x.DishSnapshot)
            .HasConversion(JsonValueConverters.DishSnapshotValue, JsonValueConverters.DishSnapshotComparer);

        b.ToTable(t => t.HasCheckConstraint(
            "ck_meal_records_rating",
            "rating IS NULL OR rating BETWEEN 1 AND 5"));

        b.ToTable(t => t.HasCheckConstraint(
            "ck_meal_records_servings",
            "servings > 0 AND servings <= 10"));

        // 记录列表：最高频查询
        b.HasIndex(x => new { x.UserId, x.EatenAt })
            .HasDatabaseName("ix_meal_records_user_eaten")
            .IsDescending(false, true)
            .HasFilter("NOT is_deleted");

        // 新鲜度计算：某用户最近吃过哪些菜
        b.HasIndex(x => new { x.UserId, x.DishId, x.EatenAt })
            .HasDatabaseName("ix_meal_records_user_dish")
            .IsDescending(false, false, true)
            .HasFilter("NOT is_deleted");

        // 画像学习：按时间聚合
        b.HasIndex(x => new { x.UserId, x.CreatedAt })
            .HasDatabaseName("ix_meal_records_user_created")
            .IsDescending(false, true)
            .HasFilter("NOT is_deleted");

        // 待评分提醒
        b.HasIndex(x => new { x.UserId, x.EatenAt })
            .HasDatabaseName("ix_meal_records_pending_rating")
            .IsDescending(false, true)
            .HasFilter("rating IS NULL AND NOT is_deleted");

        b.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Dish)
            .WithMany()
            .HasForeignKey(x => x.DishId)
            .OnDelete(DeleteBehavior.SetNull);

        b.HasOne(x => x.DecisionSession)
            .WithMany()
            .HasForeignKey(x => x.DecisionSessionId)
            .OnDelete(DeleteBehavior.SetNull);

        b.HasOne(x => x.AiRecognitionLog)
            .WithMany()
            .HasForeignKey(x => x.AiRecognitionLogId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class DecisionSessionConfiguration : IEntityTypeConfiguration<DecisionSession>
{
    public void Configure(EntityTypeBuilder<DecisionSession> b)
    {
        b.ToTable("decision_sessions");
        b.HasKey(x => x.Id);

        b.Property(x => x.Weather).HasMaxLength(30);
        b.Property(x => x.EngineVersion).HasMaxLength(20).IsRequired();
        b.Property(x => x.Latitude).HasPrecision(9, 6);
        b.Property(x => x.Longitude).HasPrecision(9, 6);

        b.Property(x => x.MealType).HasConversion<short>();
        b.Property(x => x.DiningMode).HasConversion<short>();
        b.Property(x => x.PartySize).HasConversion<short>();

        b.Property(x => x.MoodTags)
            .HasConversion(JsonValueConverters.StringList, JsonValueConverters.StringListComparer);

        b.Property(x => x.Candidates)
            .HasConversion(JsonValueConverters.CandidateList, JsonValueConverters.CandidateListComparer);

        b.HasIndex(x => new { x.UserId, x.CreatedAt })
            .HasDatabaseName("ix_decision_sessions_user_created")
            .IsDescending(false, true);

        b.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne<Dish>()
            .WithMany()
            .HasForeignKey(x => x.ChosenDishId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class AiRecognitionLogConfiguration : IEntityTypeConfiguration<AiRecognitionLog>
{
    public void Configure(EntityTypeBuilder<AiRecognitionLog> b)
    {
        b.ToTable("ai_recognition_logs");
        b.HasKey(x => x.Id);

        b.Property(x => x.ImageUrl).HasMaxLength(500).IsRequired();
        b.Property(x => x.ModelName).HasMaxLength(60).IsRequired();
        b.Property(x => x.ErrorMessage).HasMaxLength(500);

        b.Property(x => x.Status).HasConversion<short>();

        b.HasIndex(x => new { x.UserId, x.CreatedAt })
            .HasDatabaseName("ix_ai_logs_user_created")
            .IsDescending(false, true);

        // 监控：识别失败率
        b.HasIndex(x => new { x.Status, x.CreatedAt })
            .HasDatabaseName("ix_ai_logs_status_created")
            .IsDescending(false, true);

        // 数据资产：被修正过的样本
        b.HasIndex(x => x.CreatedAt)
            .HasDatabaseName("ix_ai_logs_corrected")
            .IsDescending()
            .HasFilter("is_corrected");

        b.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
