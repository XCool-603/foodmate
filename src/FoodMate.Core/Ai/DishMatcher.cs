using System.Text;

namespace FoodMate.Core.Ai;

/// <summary>匹配用的菜品视图。</summary>
/// <param name="Id">菜品 ID。</param>
/// <param name="Name">菜名。</param>
/// <param name="Aliases">别名。</param>
public sealed record DishMatchTarget(Guid Id, string Name, IReadOnlyList<string> Aliases);

/// <summary>匹配结果。</summary>
/// <param name="DishId">匹配到的菜品。</param>
/// <param name="MatchedName">菜品库中的正式名称。</param>
/// <param name="Score">匹配置信度 0–1。</param>
/// <param name="ViaAlias">是否通过别名命中。</param>
public sealed record DishMatch(Guid DishId, string MatchedName, double Score, bool ViaAlias);

/// <summary>
/// 把模型识别出的菜名匹配到菜品库。
/// </summary>
/// <remarks>
/// <para>
/// 这一步决定了 AI 识别到底有没有用：匹配上，用户就能复用菜品库的热量/辣度/食材数据，
/// 后续的决策与统计也能连起来；匹配不上，就只是一条孤立的文本记录。
/// </para>
/// <para>
/// 模型输出的菜名是<b>自由文本</b>——「番茄炒蛋」「西红柿炒鸡蛋」「番茄炒鸡蛋」都指同一道菜。
/// 因此匹配必须容忍别名、标点、空格与语序差异。
/// </para>
/// <para>
/// <b>纯函数</b>，不碰数据库，便于单元测试。
/// </para>
/// </remarks>
public static class DishMatcher
{
    /// <summary>低于此分数视为匹配失败。</summary>
    public const double MinAcceptableScore = 0.60;

    /// <summary>为单个菜名找最佳匹配；找不到返回 <c>null</c>。</summary>
    public static DishMatch? Match(string recognizedName, IReadOnlyList<DishMatchTarget> candidates)
    {
        if (string.IsNullOrWhiteSpace(recognizedName) || candidates.Count == 0)
        {
            return null;
        }

        var query = Normalize(recognizedName);
        if (query.Length == 0)
        {
            return null;
        }

        DishMatch? best = null;

        foreach (var candidate in candidates)
        {
            var name = Normalize(candidate.Name);

            // ① 正式名完全相同
            if (name == query)
            {
                return new DishMatch(candidate.Id, candidate.Name, 1.0, ViaAlias: false);
            }

            // ② 别名完全相同
            var viaAlias = false;
            foreach (var alias in candidate.Aliases)
            {
                if (Normalize(alias) == query)
                {
                    viaAlias = true;
                    break;
                }
            }

            if (viaAlias)
            {
                best = Better(best, new DishMatch(candidate.Id, candidate.Name, 0.95, true));
                continue;
            }

            // ③ 双向包含：「番茄炒蛋」↔「番茄炒蛋（微辣）」
            var containment = ContainmentScore(query, name);
            if (containment > 0)
            {
                best = Better(best, new DishMatch(candidate.Id, candidate.Name, containment, false));
                continue;
            }

            // ④ 别名包含
            var aliasContainment = 0.0;
            foreach (var alias in candidate.Aliases)
            {
                var aliasScore = ContainmentScore(query, Normalize(alias));
                if (aliasScore > aliasContainment)
                {
                    aliasContainment = aliasScore;
                }
            }

            if (aliasContainment > 0)
            {
                // 通过别名模糊命中，略降权
                best = Better(best, new DishMatch(
                    candidate.Id, candidate.Name, aliasContainment * 0.9, ViaAlias: true));
                continue;
            }

            // ⑤ 字符级兜底：处理「番茄炒鸡蛋」↔「番茄炒蛋」这类插入/省略差异
            var overlap = CharacterOverlapScore(query, name);
            if (overlap > 0)
            {
                best = Better(best, new DishMatch(candidate.Id, candidate.Name, overlap, false));
                continue;
            }

            foreach (var alias in candidate.Aliases)
            {
                var aliasOverlap = CharacterOverlapScore(query, Normalize(alias));
                if (aliasOverlap > 0)
                {
                    best = Better(best, new DishMatch(
                        candidate.Id, candidate.Name, aliasOverlap * 0.95, ViaAlias: true));
                    break;
                }
            }
        }

        return best is not null && best.Score >= MinAcceptableScore ? best : null;
    }

    /// <summary>批量匹配，返回「识别序号 → 匹配结果」。</summary>
    public static IReadOnlyDictionary<int, DishMatch> MatchAll(
        IReadOnlyList<string> recognizedNames,
        IReadOnlyList<DishMatchTarget> candidates)
    {
        var result = new Dictionary<int, DishMatch>();
        var used = new HashSet<Guid>();

        for (var i = 0; i < recognizedNames.Count; i++)
        {
            var match = Match(recognizedNames[i], candidates);

            // 一顿饭里同一道菜不会出现两次——已被占用的菜品不再重复匹配
            if (match is null || !used.Add(match.DishId))
            {
                continue;
            }

            result[i] = match;
        }

        return result;
    }

    /// <summary>
    /// 双向包含打分。
    /// </summary>
    /// <remarks>
    /// 用「较短串占较长串的比例」作为基础分，避免「蛋」匹配上「蛋炒饭」这类
    /// 过短命中。完全包含且长度接近时接近 0.9。
    /// </remarks>
    private static double ContainmentScore(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0)
        {
            return 0;
        }

        var (shorter, longer) = a.Length <= b.Length ? (a, b) : (b, a);

        // 至少 2 个字才做包含匹配，否则「饭」「汤」会大面积误配
        if (shorter.Length < 2 || !longer.Contains(shorter, StringComparison.Ordinal))
        {
            return 0;
        }

        var ratio = (double)shorter.Length / longer.Length;

        // 比例 0.5 → 0.60；比例 1.0 → 0.90
        return 0.30 + 0.60 * ratio;
    }

    /// <summary>
    /// 字符级相似度兜底。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 处理「番茄炒鸡蛋」↔「番茄炒蛋」这类<b>插入/省略</b>差异——
    /// 它们既不完全相同，也没有包含关系，但显然是同一道菜。
    /// </para>
    /// <para>
    /// 阈值刻意收紧，因为模糊匹配一旦放宽就会张冠李戴：
    /// </para>
    /// <list type="bullet">
    ///   <item>交集至少 2 个字——防止「饭」匹配上「蛋炒饭」。</item>
    ///   <item>较短一方的字符<b>覆盖率 ≥ 0.8</b>——「番茄炒鸡蛋」与「番茄牛腩」
    ///         只共享「番茄」2 字，覆盖率仅 0.5，必须排除。</item>
    ///   <item>整体 Jaccard ≥ 0.5。</item>
    /// </list>
    /// <para>
    /// 即便偶有误配，用户在确认页改一下即可，而这次修正会被记录下来——
    /// 宁可匹配得宽一点让用户纠错，也不要一个都匹配不上。
    /// </para>
    /// </remarks>
    private static double CharacterOverlapScore(string a, string b)
    {
        if (a.Length < 2 || b.Length < 2)
        {
            return 0;
        }

        var setA = a.ToHashSet();
        var setB = b.ToHashSet();

        var intersection = setA.Intersect(setB).Count();

        if (intersection < 2)
        {
            return 0;
        }

        var coverage = (double)intersection / Math.Min(setA.Count, setB.Count);
        var jaccard = (double)intersection / setA.Union(setB).Count();

        if (coverage < 0.8 || jaccard < 0.5)
        {
            return 0;
        }

        // jaccard 0.5 → 0.725；1.0 → 0.90
        return 0.55 + 0.35 * jaccard;
    }

    private static DishMatch? Better(DishMatch? current, DishMatch candidate)
        => current is null || candidate.Score > current.Score ? candidate : current;

    /// <summary>
    /// 归一化菜名：去掉空白与常见标点，统一大小写。
    /// </summary>
    /// <remarks>
    /// 中餐菜名里的括号、顿号、空格都不承载语义
    /// （「宫保鸡丁（微辣）」「番茄、鸡蛋」），去掉后才能正确比对。
    /// </remarks>
    public static string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(input.Length);

        foreach (var c in input)
        {
            if (char.IsWhiteSpace(c) || IsPunctuation(c))
            {
                continue;
            }

            sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString();
    }

    private static bool IsPunctuation(char c)
        => char.IsPunctuation(c)
           || char.IsSymbol(c)
           || c is '（' or '）' or '【' or '】' or '·' or '、' or '～' or '～';
}
