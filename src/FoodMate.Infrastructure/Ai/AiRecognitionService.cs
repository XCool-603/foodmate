using System.Text.Json;
using FoodMate.Core;
using FoodMate.Core.Abstractions;
using FoodMate.Core.Ai;
using FoodMate.Core.Decision;
using FoodMate.Core.Entities;
using FoodMate.Core.Enums;
using FoodMate.Core.Exceptions;
using FoodMate.Contracts.Ai;
using FoodMate.Contracts.Records;
using FoodMate.Infrastructure.Data;
using FoodMate.Infrastructure.Records;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FoodMate.Infrastructure.Ai;

/// <summary>一次识别的内部产出。</summary>
/// <param name="LogId">识别日志 ID。</param>
/// <param name="Result">识别结果（含匹配信息）。</param>
/// <param name="RemainingToday">今日剩余次数。</param>
public sealed record RecognitionOutcome(
    Guid LogId,
    RecognizeResponse Result,
    int RemainingToday);

/// <summary>确认入库的产出。</summary>
/// <param name="RecordIds">创建的记录。</param>
/// <param name="CreatedDishIds">新建的菜品。</param>
/// <param name="CorrectedCount">被修正的条目数。</param>
/// <param name="WasCorrected">本次识别是否被修正过。</param>
public sealed record ConfirmOutcome(
    IReadOnlyList<Guid> RecordIds,
    IReadOnlyList<Guid> CreatedDishIds,
    int CorrectedCount,
    bool WasCorrected);

/// <summary>
/// AI 识别应用服务。
/// </summary>
/// <remarks>
/// <para>
/// 流程严格分成两步：<b>识别</b>只返回结果并写日志，<b>确认</b>才真正入库。
/// 视觉模型的准确率不足以直接信任，用户必须过一眼。
/// </para>
/// <para>
/// 用户在确认页的每一次修正都会被记录到 <c>ai_recognition_logs.corrected_result</c>——
/// 这是「模型错在哪」的标注，是后续优化 Prompt、构建 few-shot 库、
/// 乃至微调模型的核心数据资产。
/// </para>
/// <para>
/// <b>AI 是增强而非依赖。</b>模型不可用时一律降级为手动输入，
/// 绝不因为 AI 挂了就让用户无法记录。
/// </para>
/// </remarks>
public sealed class AiRecognitionService(
    FoodMateDbContext db,
    IVisionModelClient vision,
    ITextModelClient text,
    IImageStorage storage,
    RecordService records,
    IClock clock,
    IOptions<AiOptions> options,
    ILogger<AiRecognitionService> logger)
{
    /// <summary>
    /// 日志载荷的序列化选项。
    /// </summary>
    /// <remarks>
    /// 复用数据层的 <see cref="JsonOpts"/>——它启用了
    /// <c>UnsafeRelaxedJsonEscaping</c>，中文按原样存储而不是 <c>\uXXXX</c> 转义。
    /// 这对 <c>corrected_result</c> 尤其重要：那是要给人看、要拿去分析的数据资产，
    /// 转义后既没法直接读，也没法用文本工具检索。
    /// </remarks>
    private static readonly JsonSerializerOptions JsonOpts = Data.Json.JsonOpts.Default;

    private AiOptions Options => options.Value;

    // ── 识别 ────────────────────────────────────────────────

    /// <summary>识别图片中的菜品（不入库）。</summary>
    public async Task<RecognitionOutcome> RecognizeAsync(
        Guid userId,
        string imageUrl,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            throw BusinessException.Validation("imageUrl 不能为空。");
        }

        await EnsureQuotaAsync(userId, ct);

        // 图片解析失败（文件不存在、路径非法）属于「用户传错了」，
        // 必须转成业务错误返回，否则会变成 500 让前端以为是服务端故障
        string modelInput;

        try
        {
            modelInput = await storage.ResolveForModelAsync(imageUrl, ct);
        }
        catch (ImageStorageException ex)
        {
            throw new BusinessException(
                ApiErrorCode.ImageFormatUnsupported,
                $"图片无法读取：{ex.Message} 请重新上传。");
        }

        VisionRecognitionResult recognition;

        try
        {
            recognition = await vision.RecognizeDishesAsync(modelInput, ct);
        }
        catch (AiClientException ex)
        {
            // 失败也要留痕，便于监控失败率与排查
            await WriteLogAsync(userId, imageUrl, ex.IsTimeout, ex.Message, null, ct);

            throw new BusinessException(
                ex.IsTimeout ? ApiErrorCode.AiTimeout : ApiErrorCode.AiRecognitionFailed,
                ex.IsTimeout
                    ? "识别超时了，可以重试或直接手动记录。"
                    : $"识别失败：{ex.Message} 可以直接手动记录。");
        }

        var (items, conflicts) = await EnrichAsync(userId, recognition.Items, ct);

        var payload = BuildPayload(recognition, items);

        var log = await WriteLogAsync(userId, imageUrl, isTimeout: false, error: null, payload, ct);

        var usedToday = await CountRecentAsync(userId, TimeSpan.FromDays(1), ct);
        var remaining = Math.Max(0, Options.RecognizePerDay - usedToday);

        logger.LogInformation(
            "识别完成 User={UserId} Log={LogId} 条目={Count} 耗时={Latency}ms 模型={Model}",
            userId, log.Id, items.Count, recognition.LatencyMs, vision.ModelName);

        return new RecognitionOutcome(
            log.Id,
            new RecognizeResponse
            {
                LogId = log.Id,
                ModelName = vision.ModelName,
                IsStubModel = vision.IsStub,
                LatencyMs = recognition.LatencyMs,
                Scene = recognition.Scene,
                SceneLabel = SceneLabel(recognition.Scene),
                OverallConfidence = recognition.OverallConfidence,
                Items = items,
                RemainingToday = remaining,
            },
            remaining);
    }

    // ── 确认入库 ────────────────────────────────────────────

    /// <summary>用户确认（或修正）后写入记录。</summary>
    public async Task<ConfirmOutcome> ConfirmAsync(
        Guid userId,
        Guid logId,
        ConfirmRecognitionRequest request,
        CancellationToken ct = default)
    {
        if (request.Items.Count == 0)
        {
            throw BusinessException.Validation("至少要保留一道菜。");
        }

        var log = await db.AiRecognitionLogs
            .FirstOrDefaultAsync(l => l.Id == logId && l.UserId == userId, ct)
            ?? throw BusinessException.NotFound($"识别记录 {logId} 不存在");

        if (log.Status == AiLogStatus.Confirmed)
        {
            throw BusinessException.Validation("这次识别已经确认过了，请重新拍照。");
        }

        var original = DeserializePayload(log.ParsedResult);
        var originalByIndex = original?.Items.ToDictionary(i => i.Index) ?? [];

        // 先整体查一遍忌口冲突，再逐条写入。
        // 否则第一条写完、第二条抛 2003，会留下「确认了一半」的脏数据。
        if (!request.Force)
        {
            await EnsureNoBatchConflictAsync(userId, request.Items, ct);
        }

        var correctedCount = 0;
        var recordIds = new List<Guid>();
        var createdDishIds = new List<Guid>();

        foreach (var item in request.Items)
        {
            var name = item.Name?.Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                throw BusinessException.Validation("菜名不能为空。");
            }

            // 与原始识别结果比对，自行判定「是否被修正」——
            // 不信任客户端传来的 isCorrected 标志
            if (originalByIndex.TryGetValue(item.Index, out var before))
            {
                if (!string.Equals(before.Name, name, StringComparison.Ordinal)
                    || before.MatchedDishId != item.DishId)
                {
                    correctedCount++;
                }
            }
            else
            {
                correctedCount++;   // 用户手动新增的条目也算修正
            }

            var dish = await ResolveOrCreateDishAsync(userId, item, name, ct);

            if (dish is not null && !dish.IsBuiltin && dish.OwnerUserId == userId
                && originalByIndex.TryGetValue(item.Index, out var orig)
                && orig.MatchedDishId is null)
            {
                createdDishIds.Add(dish.Id);
            }

            var record = await records.CreateAsync(userId, new CreateRecordRequest
            {
                DishId = dish?.Id,
                DishName = name,
                MealType = request.MealType,
                DiningMode = request.DiningMode,
                EatenAt = request.EatenAt,
                Servings = item.Portion <= 0 ? 1.0 : item.Portion,
                Rating = null,
                Source = (short)RecordSource.AiRecognized,
                Force = request.Force,
            }, ct);

            recordIds.Add(record.Id);

            // 把记录回填到 AI 日志，便于后续分析「识别 → 实际记录」的转化
            record.AiRecognitionLogId = log.Id;
        }

        // 沉淀修正数据：这是整个 AI 链路最有价值的产出
        log.CorrectedResult = JsonSerializer.Serialize(
            new StoredRecognitionPayload
            {
                ModelName = log.ModelName,
                Scene = original?.Scene ?? "unknown",
                Items =
                [
                    .. request.Items.Select(i => new StoredRecognitionItem
                    {
                        Index = i.Index,
                        Name = i.Name.Trim(),
                        Confidence = originalByIndex.TryGetValue(i.Index, out var b) ? b.Confidence : 0,
                        EstimatedCalories = i.EstimatedCalories,
                        Portion = i.Portion,
                        MatchedDishId = i.DishId,
                    }),
                ],
            },
            JsonOpts);

        log.IsCorrected = correctedCount > 0;
        log.Status = AiLogStatus.Confirmed;

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "识别已确认 User={UserId} Log={LogId} 条目={Count} 修正={Corrected} 新建菜品={Created}",
            userId, logId, request.Items.Count, correctedCount, createdDishIds.Count);

        return new ConfirmOutcome(recordIds, createdDishIds, correctedCount, log.IsCorrected);
    }

    // ── 菜谱与买菜清单 ──────────────────────────────────────

    /// <summary>生成菜谱（优先用菜品库已缓存的）。</summary>
    public async Task<RecipeDto> GenerateRecipeAsync(
        Guid userId,
        GenerateRecipeRequest request,
        CancellationToken ct = default)
    {
        var dishName = request.DishName?.Trim();

        if (request.DishId is { } dishId)
        {
            var cached = await db.Recipes
                .AsNoTracking()
                .Include(r => r.Dish)
                .FirstOrDefaultAsync(r => r.DishId == dishId, ct);

            if (cached is not null)
            {
                return ToDto(cached, fromCache: true, isStub: false);
            }

            dishName ??= (await db.Dishes.AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == dishId, ct))?.Name;
        }

        if (string.IsNullOrWhiteSpace(dishName))
        {
            throw BusinessException.Validation("必须提供 dishId 或 dishName。");
        }

        await EnsureTextQuotaAsync(userId, ct);

        GeneratedRecipe generated;

        try
        {
            generated = await text.GenerateRecipeAsync(
                dishName,
                request.Servings <= 0 ? (short)2 : request.Servings,
                ct);
        }
        catch (AiClientException ex)
        {
            throw new BusinessException(
                ex.IsTimeout ? ApiErrorCode.AiTimeout : ApiErrorCode.AiRecognitionFailed,
                $"菜谱生成失败：{ex.Message}");
        }

        // 若关联了菜品且该菜品还没有菜谱，落库缓存，避免重复调用
        if (request.DishId is { } id)
        {
            var dish = await db.Dishes.FirstOrDefaultAsync(d => d.Id == id, ct);
            var hasRecipe = await db.Recipes.AnyAsync(r => r.DishId == id, ct);

            if (dish is not null && !hasRecipe)
            {
                db.Recipes.Add(new Recipe
                {
                    Id = Guid.NewGuid(),
                    DishId = id,
                    Servings = generated.Servings,
                    CookMinutes = generated.CookMinutes,
                    Difficulty = generated.Difficulty,
                    Steps = [.. generated.Steps.Select(s => new RecipeStep(s.Order, s.Text, s.DurationMinutes))],
                    Tips = generated.Tips,
                    CreatedAt = clock.UtcNow,
                });

                await db.SaveChangesAsync(ct);
            }
        }

        return new RecipeDto
        {
            DishId = request.DishId,
            DishName = dishName,
            Servings = generated.Servings,
            CookMinutes = generated.CookMinutes,
            Difficulty = generated.Difficulty,
            DifficultyLabel = EnumLabels.Difficulty(generated.Difficulty),
            Steps = [.. generated.Steps.Select(s => new RecipeStepDto(s.Order, s.Text, s.DurationMinutes))],
            Tips = generated.Tips,
            FromCache = false,
            IsStubModel = text.IsStub,
        };
    }

    /// <summary>生成买菜清单。</summary>
    public async Task<ShoppingListDto> GenerateShoppingListAsync(
        Guid userId,
        ShoppingListRequest request,
        CancellationToken ct = default)
    {
        if (request.DishIds.Count == 0)
        {
            throw BusinessException.Validation("请至少选择一道菜。");
        }

        var names = await db.Dishes
            .AsNoTracking()
            .Where(d => request.DishIds.Contains(d.Id) && !d.IsDeleted)
            .Select(d => d.Name)
            .ToListAsync(ct);

        if (names.Count == 0)
        {
            throw BusinessException.NotFound("选中的菜品都不存在。");
        }

        await EnsureTextQuotaAsync(userId, ct);

        ShoppingList list;

        try
        {
            list = await text.GenerateShoppingListAsync(
                names,
                request.Servings <= 0 ? (short)2 : request.Servings,
                ct);
        }
        catch (AiClientException ex)
        {
            throw new BusinessException(
                ex.IsTimeout ? ApiErrorCode.AiTimeout : ApiErrorCode.AiRecognitionFailed,
                $"买菜清单生成失败：{ex.Message}");
        }

        return new ShoppingListDto
        {
            Items = [.. list.Items.Select(i => new ShoppingItemDto(i.Name, i.TotalAmount, i.Category, i.ForDishes))],
            TextSummary = list.TextSummary,
            IsStubModel = text.IsStub,
        };
    }

    // ── 内部实现 ────────────────────────────────────────────

    /// <summary>把识别结果与菜品库、用户忌口对上。</summary>
    private async Task<(List<RecognizedItemDto> Items, Dictionary<int, List<string>> Conflicts)>
        EnrichAsync(
            Guid userId,
            IReadOnlyList<RecognizedDishItem> recognized,
            CancellationToken ct)
    {
        var targets = await db.Dishes
            .AsNoTracking()
            .Where(d => d.IsActive && !d.IsDeleted && (d.IsBuiltin || d.OwnerUserId == userId))
            .Select(d => new { d.Id, d.Name, d.Aliases })
            .ToListAsync(ct);

        var matchTargets = targets
            .Select(t => new DishMatchTarget(t.Id, t.Name, t.Aliases))
            .ToList();

        var matches = DishMatcher.MatchAll(
            [.. recognized.Select(r => r.Name)],
            matchTargets);

        // 忌口冲突提示：菜品库里能查到食材的才可能判断出来
        var preference = await db.UserPreferences
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        IReadOnlySet<string> expandedAvoid = preference is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : HardFilter.ExpandAvoidList(preference.AvoidIngredients);

        var matchedIds = matches.Values.Select(m => m.DishId).ToList();

        var ingredientsOf = matchedIds.Count == 0
            ? []
            : await db.Dishes
                .AsNoTracking()
                .Where(d => matchedIds.Contains(d.Id))
                .Select(d => new { d.Id, d.Ingredients })
                .ToDictionaryAsync(d => d.Id, d => d.Ingredients, ct);

        var items = new List<RecognizedItemDto>(recognized.Count);
        var conflicts = new Dictionary<int, List<string>>();

        for (var i = 0; i < recognized.Count; i++)
        {
            var source = recognized[i];
            matches.TryGetValue(i, out var match);

            var itemConflicts = new List<string>();

            if (match is not null
                && expandedAvoid.Count > 0
                && ingredientsOf.TryGetValue(match.DishId, out var ingredients))
            {
                // 复用决策引擎的忌口匹配规则，保证「识别提示」与「推荐过滤」口径一致
                itemConflicts = [.. ingredients
                    .Where(ing => expandedAvoid.Any(a =>
                        ing.Name.Contains(a, StringComparison.OrdinalIgnoreCase)
                        || (ing.Name.Length >= 2 && a.Contains(ing.Name, StringComparison.OrdinalIgnoreCase))))
                    .Select(ing => ing.Name)
                    .Distinct()];
            }

            if (itemConflicts.Count > 0)
            {
                conflicts[i] = itemConflicts;
            }

            items.Add(new RecognizedItemDto
            {
                Index = i,
                Name = source.Name,
                Confidence = source.Confidence,
                NeedsReview = source.NeedsReview,
                EstimatedCalories = source.EstimatedCalories,
                Ingredients = source.Ingredients ?? [],
                Portion = source.Portion,
                MatchedDishId = match?.DishId,
                MatchedDishName = match?.MatchedName,
                MatchScore = match?.Score,
                MatchedViaAlias = match?.ViaAlias ?? false,
                ConflictIngredients = itemConflicts,
            });
        }

        return (items, conflicts);
    }

    /// <summary>
    /// 批量预检忌口冲突。
    /// </summary>
    /// <remarks>
    /// 确认是一次「全有或全无」的操作：只要有一道菜含忌口食材，
    /// 就整体拦下来让用户确认，而不是写一半再报错。
    /// </remarks>
    private async Task EnsureNoBatchConflictAsync(
        Guid userId,
        IReadOnlyList<ConfirmItemRequest> items,
        CancellationToken ct)
    {
        var preference = await db.UserPreferences
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        if (preference is null || preference.AvoidIngredients.Count == 0)
        {
            return;
        }

        var dishIds = items
            .Where(i => i.DishId is not null)
            .Select(i => i.DishId!.Value)
            .Distinct()
            .ToList();

        if (dishIds.Count == 0)
        {
            return;
        }

        var expanded = HardFilter.ExpandAvoidList(preference.AvoidIngredients);

        var ingredientsByDish = await db.Dishes
            .AsNoTracking()
            .Where(d => dishIds.Contains(d.Id))
            .Select(d => new { d.Id, d.Name, d.Ingredients })
            .ToDictionaryAsync(d => d.Id, ct);

        var conflicts = new List<string>();

        foreach (var item in items)
        {
            if (item.DishId is not { } id || !ingredientsByDish.TryGetValue(id, out var dish))
            {
                continue;
            }

            var hits = dish.Ingredients
                .Where(ing => expanded.Any(a =>
                    ing.Name.Contains(a, StringComparison.OrdinalIgnoreCase)
                    || (ing.Name.Length >= 2 && a.Contains(ing.Name, StringComparison.OrdinalIgnoreCase))))
                .Select(ing => ing.Name)
                .Distinct()
                .ToList();

            if (hits.Count > 0)
            {
                conflicts.Add($"{dish.Name}（{string.Join("、", hits)}）");
            }
        }

        if (conflicts.Count > 0)
        {
            throw new BusinessException(
                ApiErrorCode.AvoidIngredientConflict,
                $"这些菜含你忌口的食材：{string.Join("；", conflicts)}。确认要记录吗？");
        }
    }

    private async Task<Dish?> ResolveOrCreateDishAsync(
        Guid userId,
        ConfirmItemRequest item,
        string name,
        CancellationToken ct)
    {
        if (item.DishId is { } dishId)
        {
            var dish = await db.Dishes
                .FirstOrDefaultAsync(d => d.Id == dishId && !d.IsDeleted, ct);

            if (dish is null)
            {
                throw BusinessException.NotFound($"菜品 {dishId} 不存在。");
            }

            if (!dish.IsBuiltin && dish.OwnerUserId != userId)
            {
                throw BusinessException.Forbidden("无权使用他人的自定义菜品。");
            }

            return dish;
        }

        // 没传 dishId：先按名字在库里找（可能是别名写法），找不到才新建
        var existing = await db.Dishes
            .FirstOrDefaultAsync(d => d.Name == name
                                      && !d.IsDeleted
                                      && (d.IsBuiltin || d.OwnerUserId == userId), ct);

        if (existing is not null)
        {
            return existing;
        }

        // 新建自定义菜品。分类与辣度留空——AI 识别给不出可靠值，宁可标「未分类」
        var created = new Dish
        {
            Id = Guid.NewGuid(),
            Name = name,
            Cuisine = Cuisine.Other,
            Category = DishCategory.Unknown,
            SpicyLevel = 0,
            Calories = item.EstimatedCalories,
            IsBuiltin = false,
            OwnerUserId = userId,
            Popularity = 0,
            IsActive = true,
            IsDeleted = false,
            CreatedAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow,
        };

        db.Dishes.Add(created);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("AI 识别新建了自定义菜品「{Name}」({Id})", name, created.Id);

        return created;
    }

    private async Task<AiRecognitionLog> WriteLogAsync(
        Guid userId,
        string imageUrl,
        bool isTimeout,
        string? error,
        StoredRecognitionPayload? payload,
        CancellationToken ct)
    {
        var log = new AiRecognitionLog
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ImageUrl = imageUrl,
            ModelName = vision.ModelName,
            Status = error is null
                ? AiLogStatus.Success
                : isTimeout ? AiLogStatus.ModelFailed : AiLogStatus.ParseFailed,
            ParsedResult = payload is null ? null : JsonSerializer.Serialize(payload, JsonOpts),
            ErrorMessage = error,
            LatencyMs = payload?.LatencyMs is { } ms ? (int)ms : 0,
            PromptTokens = payload?.PromptTokens,
            CompletionTokens = payload?.CompletionTokens,
            IsCorrected = false,
            CreatedAt = clock.UtcNow,
        };

        db.AiRecognitionLogs.Add(log);
        await db.SaveChangesAsync(ct);

        return log;
    }

    private StoredRecognitionPayload BuildPayload(
        VisionRecognitionResult recognition,
        IReadOnlyList<RecognizedItemDto> items)
        => new()
        {
            ModelName = vision.ModelName,
            IsStub = vision.IsStub,
            Scene = recognition.Scene,
            OverallConfidence = recognition.OverallConfidence,
            LatencyMs = recognition.LatencyMs,
            PromptTokens = recognition.PromptTokens,
            CompletionTokens = recognition.CompletionTokens,
            Items =
            [
                .. items.Select(i => new StoredRecognitionItem
                {
                    Index = i.Index,
                    Name = i.Name,
                    Confidence = i.Confidence,
                    EstimatedCalories = i.EstimatedCalories,
                    Portion = i.Portion,
                    MatchedDishId = i.MatchedDishId,
                    MatchScore = i.MatchScore,
                }),
            ],
        };

    private static StoredRecognitionPayload? DeserializePayload(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<StoredRecognitionPayload>(json, JsonOpts);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task EnsureQuotaAsync(Guid userId, CancellationToken ct)
    {
        var perHour = await CountRecentAsync(userId, TimeSpan.FromHours(1), ct);

        if (perHour >= Options.RecognizePerHour)
        {
            throw new BusinessException(
                ApiErrorCode.AiQuotaExceeded,
                $"一小时最多识别 {Options.RecognizePerHour} 次，先歇一会儿吧。");
        }

        var perDay = await CountRecentAsync(userId, TimeSpan.FromDays(1), ct);

        if (perDay >= Options.RecognizePerDay)
        {
            throw new BusinessException(
                ApiErrorCode.AiQuotaExceeded,
                $"今日识别次数已用完（{Options.RecognizePerDay} 次），明天再来。");
        }
    }

    /// <summary>
    /// 文本调用的预算检查。
    /// </summary>
    /// <remarks>
    /// 目前只有识别调用会写 <c>ai_recognition_logs</c>，菜谱/清单调用没有独立计数。
    /// 因此这里用「当日日志条数」作为该用户今日 AI 使用量的代理指标，
    /// 预算取识别上限与菜谱上限之和。<b>M4 应改为按调用类型分别计数。</b>
    /// </remarks>
    private async Task EnsureTextQuotaAsync(Guid userId, CancellationToken ct)
    {
        var today = await CountRecentAsync(userId, TimeSpan.FromDays(1), ct);
        var budget = Options.RecognizePerDay + Options.RecipePerDay;

        if (today >= budget)
        {
            throw new BusinessException(
                ApiErrorCode.AiQuotaExceeded,
                "今日 AI 调用次数已达上限，明天再来。");
        }
    }

    private Task<int> CountRecentAsync(Guid userId, TimeSpan window, CancellationToken ct)
    {
        var since = clock.UtcNow - window;

        return db.AiRecognitionLogs
            .AsNoTracking()
            .CountAsync(l => l.UserId == userId && l.CreatedAt >= since, ct);
    }

    private static RecipeDto ToDto(Recipe recipe, bool fromCache, bool isStub) => new()
    {
        DishId = recipe.DishId,
        DishName = recipe.Dish?.Name ?? string.Empty,
        Servings = recipe.Servings,
        CookMinutes = recipe.CookMinutes,
        Difficulty = recipe.Difficulty,
        DifficultyLabel = EnumLabels.Difficulty(recipe.Difficulty),
        Steps = [.. recipe.Steps.Select(s => new RecipeStepDto(s.Order, s.Text, s.DurationMinutes))],
        Tips = recipe.Tips,
        FromCache = fromCache,
        IsStubModel = isStub,
    };

    private static string SceneLabel(string scene) => scene switch
    {
        "dine_in" => "堂食",
        "takeout" => "外卖",
        "homemade" => "自己做",
        _ => "未知",
    };

    // ── 日志载荷 ────────────────────────────────────────────

    /// <summary>写入 <c>ai_recognition_logs.parsed_result</c> / <c>corrected_result</c> 的结构。</summary>
    internal sealed record StoredRecognitionPayload
    {
        public string ModelName { get; init; } = string.Empty;

        public bool IsStub { get; init; }

        public string Scene { get; init; } = "unknown";

        public double OverallConfidence { get; init; }

        public long LatencyMs { get; init; }

        public int? PromptTokens { get; init; }

        public int? CompletionTokens { get; init; }

        public List<StoredRecognitionItem> Items { get; init; } = [];
    }

    /// <summary>日志载荷中的单条识别结果。</summary>
    internal sealed record StoredRecognitionItem
    {
        public int Index { get; init; }

        public string Name { get; init; } = string.Empty;

        public double Confidence { get; init; }

        public int? EstimatedCalories { get; init; }

        public double Portion { get; init; } = 1.0;

        public Guid? MatchedDishId { get; init; }

        public double? MatchScore { get; init; }
    }
}
