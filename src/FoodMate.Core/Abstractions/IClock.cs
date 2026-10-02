namespace FoodMate.Core.Abstractions;

/// <summary>
/// 时间源。注入而非直接调用 <see cref="DateTimeOffset.UtcNow"/>，
/// 使决策引擎与业务逻辑可确定性测试。
/// </summary>
public interface IClock
{
    /// <summary>当前 UTC 时间。</summary>
    DateTimeOffset UtcNow { get; }
}

/// <summary>系统时钟。</summary>
public sealed class SystemClock : IClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
