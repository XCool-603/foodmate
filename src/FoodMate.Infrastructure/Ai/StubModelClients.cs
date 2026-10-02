using System.Diagnostics;
using FoodMate.Core.Ai;

namespace FoodMate.Infrastructure.Ai;

/// <summary>
/// 视觉模型的<b>本地桩实现</b>。
/// </summary>
/// <remarks>
/// <para>
/// 用途：在没有 API Key 的环境下也能把「拍照 → 识别 → 确认 → 入库」整条链路跑通，
/// 并让自动化测试不依赖任何外部服务。
/// </para>
/// <para>
/// ⚠️ <b>它不会真的看图。</b>结果是按图片地址的哈希从固定池里确定性挑出来的——
/// 同一张图永远得到同一组结果（便于测试断言），但内容与图片本身无关。
/// <see cref="IsStub"/> 为 <c>true</c>，API 响应会如实标注，
/// 避免有人把桩数据误当成真实识别能力。
/// </para>
/// <para>
/// 其中「米饭」刻意不在内置菜品库里，用于覆盖「识别到新菜 → 确认时创建自定义菜品」这条路径。
/// </para>
/// </remarks>
public sealed class StubVisionModelClient : IVisionModelClient
{
    private static readonly string[][] DishPool =
    [
        ["番茄牛腩", "米饭", "凉拌黄瓜"],
        ["宫保鸡丁", "米饭"],
        ["麻婆豆腐", "米饭", "酸辣汤"],
        ["清蒸鲈鱼", "米饭"],
        ["西红柿炒蛋", "米饭"],
        ["红烧肉", "米饭", "青椒土豆丝"],
        ["小笼包", "皮蛋瘦肉粥"],
        ["农家小炒肉", "米饭"],
    ];

    /// <inheritdoc />
    public string ModelName => "stub-vision";

    /// <inheritdoc />
    public bool IsStub => true;

    /// <inheritdoc />
    public Task<VisionRecognitionResult> RecognizeDishesAsync(
        string imageUrl,
        CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();

        // 用地址的稳定哈希选一组结果：同图同结果，便于测试
        var hash = StableHash(imageUrl);
        var names = DishPool[hash % DishPool.Length];

        var items = names
            .Select((name, index) => new RecognizedDishItem(
                Name: name,
                // 首项给高置信度，后续递减，制造出「需要复核」的样本
                Confidence: Math.Round(0.94 - index * 0.17, 2),
                EstimatedCalories: EstimateCalories(name),
                Ingredients: [],
                Portion: 1.0))
            .ToList();

        stopwatch.Stop();

        return Task.FromResult(new VisionRecognitionResult
        {
            Items = items,
            Scene = hash % 2 == 0 ? "dine_in" : "homemade",
            OverallConfidence = items.Count == 0 ? 0 : Math.Round(items.Average(i => i.Confidence), 2),
            RawResponse = $"[stub] imageUrl={imageUrl} hash={hash}",
            LatencyMs = stopwatch.ElapsedMilliseconds,
        });
    }

    /// <summary>跨进程稳定的哈希（<see cref="string.GetHashCode()"/> 每次启动都不同）。</summary>
    private static int StableHash(string value)
    {
        unchecked
        {
            var hash = 17;

            foreach (var c in value)
            {
                hash = hash * 31 + c;
            }

            return Math.Abs(hash);
        }
    }

    private static int? EstimateCalories(string dishName) => dishName switch
    {
        "米饭" => 230,
        "番茄牛腩" => 420,
        "宫保鸡丁" => 480,
        "麻婆豆腐" => 260,
        "清蒸鲈鱼" => 220,
        "西红柿炒蛋" => 210,
        "红烧肉" => 680,
        "青椒土豆丝" => 180,
        "小笼包" => 380,
        "皮蛋瘦肉粥" => 280,
        "农家小炒肉" => 520,
        "凉拌黄瓜" => 90,
        "酸辣汤" => 160,
        _ => null,
    };
}

/// <summary>
/// 文本模型的<b>本地桩实现</b>。
/// </summary>
/// <remarks>
/// 生成的是模板化内容，<b>不是真的 AI 生成</b>。
/// <see cref="IsStub"/> 为 <c>true</c>，API 响应会如实标注。
/// </remarks>
public sealed class StubTextModelClient : ITextModelClient
{
    /// <inheritdoc />
    public string ModelName => "stub-text";

    /// <inheritdoc />
    public bool IsStub => true;

    /// <inheritdoc />
    public Task<GeneratedRecipe> GenerateRecipeAsync(
        string dishName,
        short servings,
        CancellationToken ct = default)
        => Task.FromResult(new GeneratedRecipe
        {
            DishName = dishName,
            Servings = servings,
            CookMinutes = 30,
            Difficulty = 1,
            Steps =
            [
                new GeneratedRecipeStep(1, $"备齐{dishName}所需食材，洗净切好", 8),
                new GeneratedRecipeStep(2, "热锅冷油，下配料爆香", 3),
                new GeneratedRecipeStep(3, $"下主料翻炒至断生，加调料调味", 10),
                new GeneratedRecipeStep(4, "加水或高汤，中小火焖煮入味", 8),
                new GeneratedRecipeStep(5, "大火收汁，出锅装盘", 1),
            ],
            Tips = "这是本地桩生成的模板菜谱，接入真实文本模型后会替换为实际内容。",
        });

    /// <inheritdoc />
    public Task<ShoppingList> GenerateShoppingListAsync(
        IReadOnlyList<string> dishNames,
        short servings,
        CancellationToken ct = default)
    {
        var items = dishNames
            .Select(name => new ShoppingListItem(
                Name: $"{name}所需食材",
                TotalAmount: $"{servings} 人份",
                Category: "待补全",
                ForDishes: [name]))
            .ToList();

        var summary = string.Join(
            "\n",
            dishNames.Select(n => $"· {n}（{servings} 人份）"));

        return Task.FromResult(new ShoppingList
        {
            Items = items,
            TextSummary = $"{summary}\n\n（本地桩生成，接入真实文本模型后会列出具体食材与用量）",
        });
    }
}
