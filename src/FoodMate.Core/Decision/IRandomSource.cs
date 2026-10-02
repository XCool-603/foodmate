namespace FoodMate.Core.Decision;

/// <summary>
/// 随机源。抽象出来是为了让抖动<b>可复现</b>——测试注入固定种子。
/// </summary>
public interface IRandomSource
{
    /// <summary>标准正态分布采样。</summary>
    double NextGaussian();
}

/// <summary>基于 <see cref="Random"/> 的随机源。Box–Muller 变换。</summary>
public sealed class RandomSource : IRandomSource
{
    private readonly Random _random;

    /// <summary>用随机种子构造。</summary>
    public RandomSource() => _random = new Random();

    /// <summary>用固定种子构造，保证可复现。</summary>
    public RandomSource(int seed) => _random = new Random(seed);

    /// <inheritdoc />
    public double NextGaussian()
    {
        // Box–Muller：u1 需 > 0，否则 Log 会得到 -∞
        var u1 = 1.0 - _random.NextDouble();
        var u2 = 1.0 - _random.NextDouble();

        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
    }
}

/// <summary>不产生任何抖动的随机源。用于「必须完全确定性」的场景与测试。</summary>
public sealed class DeterministicRandomSource : IRandomSource
{
    /// <summary>单例。</summary>
    public static DeterministicRandomSource Instance { get; } = new();

    /// <inheritdoc />
    public double NextGaussian() => 0.0;
}
