using FoodMate.Core.Abstractions;
using FoodMate.Core.Ai;
using FoodMate.Core.Auth;
using FoodMate.Core.Decision;
using FoodMate.Core.Decision.Scoring;
using FoodMate.Core.Profile;
using FoodMate.Infrastructure.Ai;
using FoodMate.Infrastructure.Auth;
using FoodMate.Infrastructure.Data;
using FoodMate.Infrastructure.Decisions;
using FoodMate.Infrastructure.Identity;
using FoodMate.Infrastructure.Profile;
using FoodMate.Infrastructure.Records;
using FoodMate.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FoodMate.Infrastructure;

/// <summary>Infrastructure 层的依赖注入注册。</summary>
public static class DependencyInjection
{
    /// <summary>数据库 Provider 名称：SQLite（默认）。</summary>
    public const string SqliteProvider = "Sqlite";

    /// <summary>数据库 Provider 名称：PostgreSQL。</summary>
    public const string PostgreSqlProvider = "PostgreSql";

    private const string DefaultSqliteConnection = "Data Source=foodmate.dev.db";

    /// <summary>注册数据库上下文、初始化器与系统时钟。</summary>
    public static IServiceCollection AddFoodMatePersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var provider = configuration["Database:Provider"] ?? SqliteProvider;
        var connectionString = configuration.GetConnectionString("Default");

        var usePostgres = provider.Equals(PostgreSqlProvider, StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            if (usePostgres)
            {
                throw new InvalidOperationException(
                    "生产环境需要配置 ConnectionStrings:Default（PostgreSQL 连接串）。");
            }

            connectionString = DefaultSqliteConnection;
        }

        services.AddDbContext<FoodMateDbContext>(options =>
        {
            if (usePostgres)
            {
                options.UseNpgsql(connectionString, npg => npg.EnableRetryOnFailure());
            }
            else
            {
                options.UseSqlite(connectionString);
            }
        });

        services.AddScoped<DbInitializer>();
        services.AddSingleton<IClock, SystemClock>();

        return services;
    }

    /// <summary>
    /// 注册决策引擎与应用服务。
    /// </summary>
    /// <remarks>
    /// 引擎本身是<b>无状态纯函数</b>，注册为单例。
    /// Scorer 通过 <see cref="IScorer"/> 多注册注入，
    /// 新增维度只需在此加一行，<b>无需改动引擎代码</b>。
    /// </remarks>
    public static IServiceCollection AddFoodMateApplication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<EngineOptions>()
            .Bind(configuration.GetSection(EngineOptions.SectionName))
            .Validate(
                options =>
                {
                    try
                    {
                        options.Validate();
                        return true;
                    }
                    catch (InvalidOperationException)
                    {
                        return false;
                    }
                },
                "决策引擎参数非法：请检查 DecisionEngine 配置节（权重之和必须为 1.0）。")
            .ValidateOnStart();

        services.AddOptions<PreferenceLearningOptions>()
            .Bind(configuration.GetSection("PreferenceLearning"));

        // ── 七维打分器 ──────────────────────────────────────
        services.AddSingleton<IScorer, TasteScorer>();
        services.AddSingleton<IScorer, FreshnessScorer>();
        services.AddSingleton<IScorer, AffinityScorer>();
        services.AddSingleton<IScorer, TimeSlotScorer>();
        services.AddSingleton<IScorer, BudgetScorer>();
        services.AddSingleton<IScorer, ContextScorer>();
        services.AddSingleton<IScorer, ExplorationScorer>();

        // ── 引擎组件 ────────────────────────────────────────
        services.AddSingleton<PenaltyEvaluator>();
        services.AddSingleton<ReasonGenerator>();
        services.AddSingleton<IRandomSource>(_ => new RandomSource());
        services.AddSingleton<IDecisionEngine, DecisionEngine>();

        // ── 应用服务 ────────────────────────────────────────
        services.AddScoped<DecisionService>();
        services.AddScoped<GuestUserService>();
        services.AddScoped<RecordService>();
        services.AddScoped<RecordStatsService>();
        services.AddScoped<PreferenceService>();

        return services;
    }

    /// <summary>
    /// 注册 AI 能力（视觉识别 + 文本生成）与图片存储。
    /// </summary>
    /// <remarks>
    /// <para>
    /// Provider 有两种：<c>Stub</c>（默认）与 <c>OpenAiCompatible</c>。
    /// 前者不依赖任何外部服务与密钥，用于本地开发与自动化测试；
    /// 后者对接通义千问 VL / 豆包 / OpenAI 等实现了 <c>/chat/completions</c> 的服务。
    /// </para>
    /// <para>
    /// 业务代码只依赖 <see cref="IVisionModelClient"/> / <see cref="ITextModelClient"/>，
    /// <b>换供应商只改配置，不改代码</b>。
    /// </para>
    /// </remarks>
    public static IServiceCollection AddFoodMateAi(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<AiOptions>()
            .Bind(configuration.GetSection(AiOptions.SectionName))
            .ValidateOnStart();

        var provider = configuration[$"{AiOptions.SectionName}:Provider"] ?? "Stub";
        var timeoutSeconds = configuration.GetValue($"{AiOptions.SectionName}:TimeoutSeconds", 30);

        if (provider.Equals("OpenAiCompatible", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient<IVisionModelClient, OpenAiCompatibleVisionClient>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
            });

            services.AddHttpClient<ITextModelClient, OpenAiCompatibleTextClient>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
            });
        }
        else
        {
            services.AddSingleton<IVisionModelClient, StubVisionModelClient>();
            services.AddSingleton<ITextModelClient, StubTextModelClient>();
        }

        services.AddSingleton<IImageStorage>(sp => new LocalImageStorage(
            sp.GetRequiredService<IHostEnvironment>().ContentRootPath,
            sp.GetRequiredService<ILogger<LocalImageStorage>>()));

        services.AddScoped<AiRecognitionService>();

        return services;
    }

    /// <summary>
    /// 注册认证能力：JWT 签发、三端身份提供者、登录服务。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">配置。</param>
    /// <param name="isProduction">是否生产环境（决定配置校验的严格程度）。</param>
    /// <remarks>
    /// <para>
    /// 身份提供者通过 <c>IEnumerable&lt;IIdentityProvider&gt;</c> 多注册注入，
    /// <see cref="IdentityProviderRegistry"/> 按平台路由。
    /// <b>新增平台只需在此加一行。</b>
    /// </para>
    /// <para>
    /// 游客登录（H5）也走同一套接口，因此全站只有一套认证机制，
    /// 不存在「开发环境绕过鉴权」的旁路。
    /// </para>
    /// </remarks>
    public static IServiceCollection AddFoodMateAuth(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isProduction)
    {
        services.AddOptions<AuthOptions>()
            .Bind(configuration.GetSection(AuthOptions.SectionName))
            .Validate(
                options =>
                {
                    try
                    {
                        options.Validate(isProduction);
                        return true;
                    }
                    catch (InvalidOperationException)
                    {
                        return false;
                    }
                },
                "认证配置非法：生产环境必须配置 Auth:Jwt:SigningKey（≥32 字节）。")
            .ValidateOnStart();

        // 签发与校验共用同一把密钥
        services.AddSingleton<JwtSigningKey>();
        services.AddSingleton<IJwtIssuer, JwtIssuer>();

        // ── 三端身份提供者 ──────────────────────────────────
        services.AddHttpClient<IIdentityProvider, WeChatIdentityProvider>();
        services.AddHttpClient<IIdentityProvider, DouyinIdentityProvider>();
        services.AddHttpClient<IIdentityProvider, AlipayIdentityProvider>();

        var allowGuest = configuration.GetValue($"{AuthOptions.SectionName}:AllowGuestLogin", true);

        if (allowGuest)
        {
            services.AddSingleton<IIdentityProvider, GuestIdentityProvider>();
        }

        services.AddSingleton<IdentityProviderRegistry>();
        services.AddScoped<AuthService>();
        services.AddScoped<GuestUserService>();

        return services;
    }
}
