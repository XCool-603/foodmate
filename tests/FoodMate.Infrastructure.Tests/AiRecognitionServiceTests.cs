using FoodMate.Contracts.Ai;
using FoodMate.Core;
using FoodMate.Core.Abstractions;
using FoodMate.Core.Ai;
using FoodMate.Core.Entities;
using FoodMate.Core.Enums;
using FoodMate.Core.Exceptions;
using FoodMate.Infrastructure.Ai;
using FoodMate.Infrastructure.Data;
using FoodMate.Infrastructure.Records;
using FoodMate.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FoodMate.Infrastructure.Tests;

/// <summary>测试用图片存储：不落盘，只记录调用。</summary>
internal sealed class FakeImageStorage : IImageStorage
{
    public string ResolvedInput { get; private set; } = string.Empty;

    public Task<StoredImage> SaveAsync(Guid userId, ImageUpload upload, CancellationToken ct = default)
        => Task.FromResult(new StoredImage($"/uploads/{userId:N}/202506/test.jpg", null, null, upload.SizeBytes));

    public Task<string> ResolveForModelAsync(string url, CancellationToken ct = default)
    {
        ResolvedInput = url;

        // 模拟真实实现的行为：文件不存在时抛 ImageStorageException
        if (url.Contains("missing", StringComparison.OrdinalIgnoreCase))
        {
            throw new ImageStorageException($"图片文件不存在：{url}");
        }

        return Task.FromResult("data:image/jpeg;base64,FAKE");
    }
}

/// <summary>可控制返回结果的视觉模型。</summary>
internal sealed class FakeVisionClient(params RecognizedDishItem[] items) : IVisionModelClient
{
    public string ModelName => "fake-vision";

    public bool IsStub => true;

    public bool ShouldFail { get; set; }

    public bool ShouldTimeout { get; set; }

    public Task<VisionRecognitionResult> RecognizeDishesAsync(string imageUrl, CancellationToken ct = default)
    {
        if (ShouldFail)
        {
            throw new AiClientException("模型炸了", isTimeout: ShouldTimeout);
        }

        return Task.FromResult(new VisionRecognitionResult
        {
            Items = items,
            Scene = "dine_in",
            OverallConfidence = items.Length == 0 ? 0 : items.Average(i => i.Confidence),
            LatencyMs = 1234,
        });
    }
}

/// <summary>可控制返回结果的文本模型。</summary>
internal sealed class FakeTextClient : ITextModelClient
{
    public string ModelName => "fake-text";

    public bool IsStub => true;

    public bool ShouldFail { get; set; }

    public Task<GeneratedRecipe> GenerateRecipeAsync(string dishName, short servings, CancellationToken ct = default)
    {
        if (ShouldFail) throw new AiClientException("模型炸了");

        return Task.FromResult(new GeneratedRecipe
        {
            DishName = dishName,
            Servings = servings,
            CookMinutes = 25,
            Difficulty = 2,
            Steps = [new GeneratedRecipeStep(1, "切菜", 3), new GeneratedRecipeStep(2, "下锅炒", 10)],
            Tips = "火要大",
        });
    }

    public Task<ShoppingList> GenerateShoppingListAsync(
        IReadOnlyList<string> dishNames, short servings, CancellationToken ct = default)
    {
        if (ShouldFail) throw new AiClientException("模型炸了");

        return Task.FromResult(new ShoppingList
        {
            Items = [new ShoppingListItem("番茄", "5个", "蔬菜", [.. dishNames])],
            TextSummary = "【蔬菜】番茄 5个",
        });
    }
}

/// <summary>AI 识别全链路集成测试。</summary>
public class AiRecognitionServiceTests : SqliteTestBase
{
    private static readonly DateTimeOffset Now = new(2025, 6, 15, 4, 0, 0, TimeSpan.Zero);

    private FixedClock Clock { get; } = new(Now);

    private FakeImageStorage Storage { get; } = new();

    private RecordService Records(FoodMateDbContext db)
        => new(db, Clock, NullLogger<RecordService>.Instance);

    private AiRecognitionService CreateService(
        FoodMateDbContext db,
        IVisionModelClient vision,
        ITextModelClient? text = null,
        AiOptions? options = null)
        => new(
            db,
            vision,
            text ?? new FakeTextClient(),
            Storage,
            Records(db),
            Clock,
            Options.Create(options ?? new AiOptions()),
            NullLogger<AiRecognitionService>.Instance);

    // ── 识别 ────────────────────────────────────────────────

    [Fact]
    public async Task 识别应匹配到菜品库并写日志()
    {
        var user = await SeedUserAsync();
        var dish = await SeedDishAsync("番茄牛腩", cuisine: Cuisine.Sichuan);

        var vision = new FakeVisionClient(
            new RecognizedDishItem("番茄牛腩", 0.92, 420),
            new RecognizedDishItem("米饭", 0.61, 230));

        var service = CreateService(Db, vision);
        var outcome = await service.RecognizeAsync(user.Id, "/uploads/x.jpg");

        Assert.Equal(2, outcome.Result.Items.Count);
        Assert.Equal(dish.Id, outcome.Result.Items[0].MatchedDishId);
        Assert.Equal(0.92, outcome.Result.Items[0].Confidence);
        Assert.False(outcome.Result.Items[0].NeedsReview);
        Assert.True(outcome.Result.Items[1].NeedsReview);
        Assert.Null(outcome.Result.Items[1].MatchedDishId);   // 米饭不在库里

        var log = await Db.AiRecognitionLogs.AsNoTracking().FirstAsync();
        Assert.Equal(AiLogStatus.Success, log.Status);
        Assert.NotNull(log.ParsedResult);
        Assert.False(log.IsCorrected);
    }

    [Fact]
    public async Task 识别应通过别名匹配()
    {
        var user = await SeedUserAsync();
        var dish = await SeedDishAsync(
            "西红柿炒蛋",
            cuisine: Cuisine.Other,
            aliases: ["番茄炒蛋", "西红柿炒鸡蛋"]);

        // 模型输出的是别名写法
        var vision = new FakeVisionClient(new RecognizedDishItem("番茄炒蛋", 0.88));

        var service = CreateService(Db, vision);
        var outcome = await service.RecognizeAsync(user.Id, "/uploads/x.jpg");

        Assert.Equal(dish.Id, outcome.Result.Items[0].MatchedDishId);
        Assert.True(outcome.Result.Items[0].MatchedViaAlias);
    }

    [Fact]
    public async Task 识别到含忌口的菜应给出冲突提示()
    {
        var user = await SeedUserAsync();

        var preference = await Db.UserPreferences.FirstAsync(p => p.UserId == user.Id);
        preference.AvoidIngredients = [CommonAllergens.Peanut];
        await Db.SaveChangesAsync();

        await SeedDishAsync(
            "宫保鸡丁",
            ingredients: [new Ingredient("鸡丁", "300g"), new Ingredient("花生米", "50g", true)]);

        var vision = new FakeVisionClient(new RecognizedDishItem("宫保鸡丁", 0.9));

        var service = CreateService(Db, vision);
        var outcome = await service.RecognizeAsync(user.Id, "/uploads/x.jpg");

        Assert.Contains("花生米", outcome.Result.Items[0].ConflictIngredients);
    }

    [Fact]
    public async Task 模型失败应写失败日志并降级为业务错误()
    {
        var user = await SeedUserAsync();
        var vision = new FakeVisionClient { ShouldFail = true };
        var service = CreateService(Db, vision);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => service.RecognizeAsync(user.Id, "/uploads/x.jpg"));

        Assert.Equal(ApiErrorCode.AiRecognitionFailed, ex.Code);
        Assert.Contains("手动记录", ex.Message);

        var log = await Db.AiRecognitionLogs.AsNoTracking().FirstAsync();
        Assert.Equal(AiLogStatus.ParseFailed, log.Status);
        Assert.NotNull(log.ErrorMessage);
    }

    [Fact]
    public async Task 超时应返回超时码()
    {
        var user = await SeedUserAsync();
        var vision = new FakeVisionClient { ShouldFail = true, ShouldTimeout = true };
        var service = CreateService(Db, vision);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => service.RecognizeAsync(user.Id, "/uploads/x.jpg"));

        Assert.Equal(ApiErrorCode.AiTimeout, ex.Code);
    }

    [Fact]
    public async Task 超过每小时配额应被拒绝()
    {
        var user = await SeedUserAsync();
        var vision = new FakeVisionClient(new RecognizedDishItem("测试菜", 0.9));

        var service = CreateService(Db, vision, options: new AiOptions { RecognizePerHour = 2 });

        await service.RecognizeAsync(user.Id, "/uploads/1.jpg");
        await service.RecognizeAsync(user.Id, "/uploads/2.jpg");

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => service.RecognizeAsync(user.Id, "/uploads/3.jpg"));

        Assert.Equal(ApiErrorCode.AiQuotaExceeded, ex.Code);
    }

    [Fact]
    public async Task 空图片地址应被拒绝()
    {
        var user = await SeedUserAsync();
        var service = CreateService(Db, new FakeVisionClient());

        await Assert.ThrowsAsync<BusinessException>(
            () => service.RecognizeAsync(user.Id, "  "));
    }

    [Fact]
    public async Task 图片无法读取应返回业务错误而非500()
    {
        // 图片不存在 / 路径非法时，存储层抛的是 ImageStorageException，
        // 若不转换就会一路冒到全局异常处理器变成 500——前端会误以为服务端故障
        var user = await SeedUserAsync();
        var service = CreateService(Db, new FakeVisionClient());

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => service.RecognizeAsync(user.Id, "/uploads/missing.jpg"));

        Assert.NotEqual(ApiErrorCode.InternalError, ex.Code);
        Assert.Contains("图片", ex.Message);
    }

    // ── 确认入库 ────────────────────────────────────────────

    [Fact]
    public async Task 确认应创建记录并标记未修正()
    {
        var user = await SeedUserAsync();
        var dish = await SeedDishAsync("番茄牛腩", cuisine: Cuisine.Sichuan, priceMin: 3200, priceMax: 4800);

        var vision = new FakeVisionClient(new RecognizedDishItem("番茄牛腩", 0.92, 420));
        var service = CreateService(Db, vision);

        var recognized = await service.RecognizeAsync(user.Id, "/uploads/x.jpg");

        var outcome = await service.ConfirmAsync(user.Id, recognized.LogId, new ConfirmRecognitionRequest
        {
            MealType = (short)MealType.Lunch,
            DiningMode = (short)DiningMode.DineIn,
            Items = [new ConfirmItemRequest { Index = 0, DishId = dish.Id, Name = "番茄牛腩", Portion = 1.0 }],
        });

        Assert.Single(outcome.RecordIds);
        Assert.Equal(0, outcome.CorrectedCount);
        Assert.False(outcome.WasCorrected);

        var record = await Db.MealRecords.AsNoTracking().FirstAsync();
        Assert.Equal(RecordSource.AiRecognized, record.Source);
        Assert.Equal(dish.Id, record.DishId);

        var log = await Db.AiRecognitionLogs.AsNoTracking().FirstAsync();
        Assert.Equal(AiLogStatus.Confirmed, log.Status);
    }

    [Fact]
    public async Task 用户修正菜名应沉淀修正数据()
    {
        var user = await SeedUserAsync();
        var correctDish = await SeedDishAsync("西红柿炒蛋");

        // 模型识别错了
        var vision = new FakeVisionClient(new RecognizedDishItem("番茄炒鸡蛋", 0.55));
        var service = CreateService(Db, vision);

        var recognized = await service.RecognizeAsync(user.Id, "/uploads/x.jpg");

        var outcome = await service.ConfirmAsync(user.Id, recognized.LogId, new ConfirmRecognitionRequest
        {
            MealType = (short)MealType.Lunch,
            DiningMode = (short)DiningMode.DineIn,
            Items =
            [
                new ConfirmItemRequest { Index = 0, DishId = correctDish.Id, Name = "西红柿炒蛋", Portion = 1.0 },
            ],
        });

        Assert.Equal(1, outcome.CorrectedCount);
        Assert.True(outcome.WasCorrected);

        var log = await Db.AiRecognitionLogs.AsNoTracking().FirstAsync();
        Assert.True(log.IsCorrected);
        Assert.NotNull(log.CorrectedResult);

        // 修正数据里应当能看到「用户最终改成了什么」
        Assert.Contains("西红柿炒蛋", log.CorrectedResult);
    }

    [Fact]
    public async Task 未匹配到菜品库时应创建自定义菜品()
    {
        var user = await SeedUserAsync();

        var vision = new FakeVisionClient(new RecognizedDishItem("外婆红烧肉", 0.8, 520));
        var service = CreateService(Db, vision);

        var recognized = await service.RecognizeAsync(user.Id, "/uploads/x.jpg");

        var outcome = await service.ConfirmAsync(user.Id, recognized.LogId, new ConfirmRecognitionRequest
        {
            MealType = (short)MealType.Dinner,
            DiningMode = (short)DiningMode.Homemade,
            Items = [new ConfirmItemRequest { Index = 0, Name = "外婆红烧肉", Portion = 1.0, EstimatedCalories = 520 }],
        });

        Assert.Single(outcome.CreatedDishIds);

        var created = await Db.Dishes.AsNoTracking().FirstAsync(d => d.Id == outcome.CreatedDishIds[0]);
        Assert.Equal("外婆红烧肉", created.Name);
        Assert.False(created.IsBuiltin);
        Assert.Equal(user.Id, created.OwnerUserId);
        Assert.Equal(DishCategory.Unknown, created.Category);
        Assert.Equal(520, created.Calories);
    }

    [Fact]
    public async Task 同名自定义菜品应复用而非重复创建()
    {
        var user = await SeedUserAsync();
        var existing = await SeedDishAsync("外婆红烧肉", withRecipe: false);

        // 把内置菜改成该用户的自定义菜
        var tracked = await Db.Dishes.FirstAsync(d => d.Id == existing.Id);
        tracked.IsBuiltin = false;
        tracked.OwnerUserId = user.Id;
        await Db.SaveChangesAsync();

        var vision = new FakeVisionClient(new RecognizedDishItem("外婆红烧肉", 0.8));
        var service = CreateService(Db, vision);

        var recognized = await service.RecognizeAsync(user.Id, "/uploads/x.jpg");

        await service.ConfirmAsync(user.Id, recognized.LogId, new ConfirmRecognitionRequest
        {
            MealType = (short)MealType.Dinner,
            DiningMode = (short)DiningMode.Homemade,
            Items = [new ConfirmItemRequest { Index = 0, Name = "外婆红烧肉", Portion = 1.0 }],
        });

        Assert.Single(await Db.Dishes.AsNoTracking()
            .Where(d => d.Name == "外婆红烧肉").ToListAsync());
    }

    [Fact]
    public async Task 一次确认多道菜应创建多条记录()
    {
        var user = await SeedUserAsync();
        var a = await SeedDishAsync("番茄牛腩");
        var b = await SeedDishAsync("凉拌黄瓜");

        var vision = new FakeVisionClient(
            new RecognizedDishItem("番茄牛腩", 0.9),
            new RecognizedDishItem("凉拌黄瓜", 0.85));

        var service = CreateService(Db, vision);
        var recognized = await service.RecognizeAsync(user.Id, "/uploads/x.jpg");

        var outcome = await service.ConfirmAsync(user.Id, recognized.LogId, new ConfirmRecognitionRequest
        {
            MealType = (short)MealType.Lunch,
            DiningMode = (short)DiningMode.DineIn,
            Items =
            [
                new ConfirmItemRequest { Index = 0, DishId = a.Id, Name = "番茄牛腩" },
                new ConfirmItemRequest { Index = 1, DishId = b.Id, Name = "凉拌黄瓜" },
            ],
        });

        Assert.Equal(2, outcome.RecordIds.Count);
        Assert.Equal(2, await Db.MealRecords.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task 确认后统计表应被更新()
    {
        var user = await SeedUserAsync();
        var dish = await SeedDishAsync("番茄牛腩");

        var vision = new FakeVisionClient(new RecognizedDishItem("番茄牛腩", 0.9));
        var service = CreateService(Db, vision);
        var recognized = await service.RecognizeAsync(user.Id, "/uploads/x.jpg");

        await service.ConfirmAsync(user.Id, recognized.LogId, new ConfirmRecognitionRequest
        {
            MealType = (short)MealType.Lunch,
            DiningMode = (short)DiningMode.DineIn,
            Items = [new ConfirmItemRequest { Index = 0, DishId = dish.Id, Name = "番茄牛腩" }],
        });

        var stat = await Db.UserDishStats.AsNoTracking()
            .FirstAsync(s => s.UserId == user.Id && s.DishId == dish.Id);

        Assert.Equal(1, stat.EatCount);
    }

    [Fact]
    public async Task 忌口冲突应在写入任何记录前整体拦下()
    {
        var user = await SeedUserAsync();

        var preference = await Db.UserPreferences.FirstAsync(p => p.UserId == user.Id);
        preference.AvoidIngredients = [CommonAllergens.Peanut];
        await Db.SaveChangesAsync();

        var safe = await SeedDishAsync("清蒸鲈鱼");
        var peanut = await SeedDishAsync(
            "宫保鸡丁",
            ingredients: [new Ingredient("花生米", "50g", true)]);

        var vision = new FakeVisionClient(
            new RecognizedDishItem("清蒸鲈鱼", 0.9),
            new RecognizedDishItem("宫保鸡丁", 0.9));

        var service = CreateService(Db, vision);
        var recognized = await service.RecognizeAsync(user.Id, "/uploads/x.jpg");

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.ConfirmAsync(user.Id, recognized.LogId, new ConfirmRecognitionRequest
            {
                MealType = (short)MealType.Lunch,
                DiningMode = (short)DiningMode.DineIn,
                Items =
                [
                    new ConfirmItemRequest { Index = 0, DishId = safe.Id, Name = "清蒸鲈鱼" },
                    new ConfirmItemRequest { Index = 1, DishId = peanut.Id, Name = "宫保鸡丁" },
                ],
            }));

        Assert.Equal(ApiErrorCode.AvoidIngredientConflict, ex.Code);
        Assert.Contains("花生米", ex.Message);

        // 关键：一条记录都不该写进去
        Assert.Equal(0, await Db.MealRecords.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task 强制确认应能写入忌口菜品()
    {
        var user = await SeedUserAsync();

        var preference = await Db.UserPreferences.FirstAsync(p => p.UserId == user.Id);
        preference.AvoidIngredients = [CommonAllergens.Peanut];
        await Db.SaveChangesAsync();

        var peanut = await SeedDishAsync("宫保鸡丁", ingredients: [new Ingredient("花生米", "50g", true)]);

        var vision = new FakeVisionClient(new RecognizedDishItem("宫保鸡丁", 0.9));
        var service = CreateService(Db, vision);
        var recognized = await service.RecognizeAsync(user.Id, "/uploads/x.jpg");

        var outcome = await service.ConfirmAsync(user.Id, recognized.LogId, new ConfirmRecognitionRequest
        {
            MealType = (short)MealType.Lunch,
            DiningMode = (short)DiningMode.DineIn,
            Force = true,
            Items = [new ConfirmItemRequest { Index = 0, DishId = peanut.Id, Name = "宫保鸡丁" }],
        });

        Assert.Single(outcome.RecordIds);
    }

    [Fact]
    public async Task 重复确认同一次识别应被拒绝()
    {
        var user = await SeedUserAsync();
        var dish = await SeedDishAsync("番茄牛腩");

        var vision = new FakeVisionClient(new RecognizedDishItem("番茄牛腩", 0.9));
        var service = CreateService(Db, vision);
        var recognized = await service.RecognizeAsync(user.Id, "/uploads/x.jpg");

        var request = new ConfirmRecognitionRequest
        {
            MealType = (short)MealType.Lunch,
            DiningMode = (short)DiningMode.DineIn,
            Items = [new ConfirmItemRequest { Index = 0, DishId = dish.Id, Name = "番茄牛腩" }],
        };

        await service.ConfirmAsync(user.Id, recognized.LogId, request);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => service.ConfirmAsync(user.Id, recognized.LogId, request));

        Assert.Equal(ApiErrorCode.ValidationFailed, ex.Code);
    }

    [Fact]
    public async Task 确认空列表应被拒绝()
    {
        var user = await SeedUserAsync();
        var service = CreateService(Db, new FakeVisionClient());

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.ConfirmAsync(user.Id, Guid.NewGuid(), new ConfirmRecognitionRequest()));

        Assert.Equal(ApiErrorCode.ValidationFailed, ex.Code);
    }

    [Fact]
    public async Task 确认他人的识别记录应报不存在()
    {
        var owner = await SeedUserAsync();
        var other = await SeedUserAsync();

        var vision = new FakeVisionClient(new RecognizedDishItem("测试菜", 0.9));
        var service = CreateService(Db, vision);
        var recognized = await service.RecognizeAsync(owner.Id, "/uploads/x.jpg");

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.ConfirmAsync(other.Id, recognized.LogId, new ConfirmRecognitionRequest
            {
                Items = [new ConfirmItemRequest { Index = 0, Name = "测试菜" }],
            }));

        Assert.Equal(ApiErrorCode.NotFound, ex.Code);
    }

    // ── 菜谱与买菜清单 ──────────────────────────────────────

    [Fact]
    public async Task 生成菜谱应缓存到菜品上()
    {
        var user = await SeedUserAsync();
        var dish = await SeedDishAsync("番茄牛腩");

        var service = CreateService(Db, new FakeVisionClient());
        var recipe = await service.GenerateRecipeAsync(user.Id, new GenerateRecipeRequest
        {
            DishId = dish.Id,
            DishName = "番茄牛腩",
            Servings = 2,
        });

        Assert.False(recipe.FromCache);
        Assert.Equal(2, recipe.Steps.Count);
        Assert.Equal("中等", recipe.DifficultyLabel);

        // 第二次应命中缓存，不再调用模型
        var cached = await service.GenerateRecipeAsync(user.Id, new GenerateRecipeRequest
        {
            DishId = dish.Id,
            Servings = 2,
        });

        Assert.True(cached.FromCache);
    }

    [Fact]
    public async Task 生成菜谱失败应降级为业务错误()
    {
        var user = await SeedUserAsync();
        var service = CreateService(Db, new FakeVisionClient(), new FakeTextClient { ShouldFail = true });

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.GenerateRecipeAsync(user.Id, new GenerateRecipeRequest { DishName = "番茄牛腩" }));

        Assert.Equal(ApiErrorCode.AiRecognitionFailed, ex.Code);
    }

    [Fact]
    public async Task 生成菜谱缺少菜名与ID应被拒绝()
    {
        var user = await SeedUserAsync();
        var service = CreateService(Db, new FakeVisionClient());

        await Assert.ThrowsAsync<BusinessException>(() =>
            service.GenerateRecipeAsync(user.Id, new GenerateRecipeRequest()));
    }

    [Fact]
    public async Task 生成买菜清单应汇总多道菜()
    {
        var user = await SeedUserAsync();
        var a = await SeedDishAsync("番茄牛腩");
        var b = await SeedDishAsync("西红柿炒蛋");

        var service = CreateService(Db, new FakeVisionClient());
        var list = await service.GenerateShoppingListAsync(user.Id, new ShoppingListRequest
        {
            DishIds = [a.Id, b.Id],
            Servings = 3,
        });

        Assert.NotEmpty(list.Items);
        Assert.Contains("番茄", list.TextSummary);
    }

    [Fact]
    public async Task 买菜清单空选择应被拒绝()
    {
        var user = await SeedUserAsync();
        var service = CreateService(Db, new FakeVisionClient());

        await Assert.ThrowsAsync<BusinessException>(() =>
            service.GenerateShoppingListAsync(user.Id, new ShoppingListRequest()));
    }
}

/// <summary>本地图片存储测试。</summary>
public class LocalImageStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"fm-test-{Guid.NewGuid():N}");

    private LocalImageStorage CreateStorage()
    {
        Directory.CreateDirectory(Path.Combine(_root, "wwwroot"));

        return new LocalImageStorage(_root, NullLogger<LocalImageStorage>.Instance);
    }

    [Fact]
    public async Task 保存应返回相对路径并落盘()
    {
        var storage = CreateStorage();
        var userId = Guid.NewGuid();
        var content = "fake-image-bytes"u8.ToArray();

        var stored = await storage.SaveAsync(
            userId,
            new ImageUpload(new MemoryStream(content), "photo.jpg", "image/jpeg", content.Length));

        Assert.StartsWith("/uploads/", stored.Url);
        Assert.Equal(content.Length, stored.SizeBytes);

        var path = Path.Combine(_root, "wwwroot", stored.Url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path));
        Assert.Equal(content, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task 空内容应被拒绝()
    {
        var storage = CreateStorage();

        await Assert.ThrowsAsync<ImageStorageException>(() =>
            storage.SaveAsync(Guid.NewGuid(), new ImageUpload(new MemoryStream(), "a.jpg", "image/jpeg", 0)));
    }

    [Fact]
    public async Task 本地路径应解析为base64数据地址()
    {
        var storage = CreateStorage();
        var userId = Guid.NewGuid();

        var stored = await storage.SaveAsync(
            userId,
            new ImageUpload(new MemoryStream("abc"u8.ToArray()), "a.png", "image/png", 3));

        var resolved = await storage.ResolveForModelAsync(stored.Url);

        Assert.StartsWith("data:image/png;base64,", resolved);
    }

    [Fact]
    public async Task 绝对地址应原样返回()
    {
        var storage = CreateStorage();

        const string url = "https://cdn.example.com/a.jpg";

        Assert.Equal(url, await storage.ResolveForModelAsync(url));
    }

    [Fact]
    public async Task 目录穿越应被拒绝()
    {
        var storage = CreateStorage();

        await Assert.ThrowsAsync<ImageStorageException>(() =>
            storage.ResolveForModelAsync("/uploads/../../appsettings.json"));
    }

    [Fact]
    public async Task 不存在的文件应报错()
    {
        var storage = CreateStorage();

        await Assert.ThrowsAsync<ImageStorageException>(() =>
            storage.ResolveForModelAsync("/uploads/nobody/202506/missing.jpg"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
