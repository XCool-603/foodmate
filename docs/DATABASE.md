# 美食伴侣 · 数据库设计

> 配套文档：[总设计文档](./DESIGN.md) · [决策引擎设计](./DECISION-ENGINE.md) · [API 接口契约](./API.md)

| 项目 | 内容 |
|---|---|
| 文档版本 | v1.0 |
| 开发库 | SQLite（零配置） |
| 生产库 | PostgreSQL 16+ |
| ORM | EF Core 10 |

---

## 1. 设计原则

| # | 原则 | 说明 |
|---|---|---|
| P1 | **主键统一用 UUID** | 避免自增序列跨库迁移问题；客户端可预生成；防遍历攻击。EF Core 自动映射 PG `uuid` / SQLite `TEXT` |
| P2 | **金额用整数「分」** | 杜绝浮点误差。字段名统一 `*_cents` |
| P3 | **JSON 字段存 `text`** | 不使用 PG 专有 `jsonb` 类型，保证 SQLite/PG 双跑（见 ADR-001）。由 EF Core `ValueConverter` 序列化 |
| P4 | **记录表存快照** | `meal_records` 冗余菜名与菜品关键属性，菜品改名/下架不影响历史 |
| P5 | **硬约束靠过滤，不靠打分** | 忌口/过敏在查询层排除，不进入打分流程 |
| P6 | **统计表可重建** | `user_dish_stats` / `user_cuisine_stats` 是派生数据，任何时候可从 `meal_records` 全量重算 |
| P7 | **软删除优先** | 菜品、记录用 `is_deleted` 标记；用户数据删除请求走硬删（合规） |

---

## 2. 实体关系

```
                    ┌──────────────┐
                    │    users     │
                    └──────┬───────┘
           ┌───────────────┼───────────────┬────────────────┐
           │ 1:1           │ 1:N           │ 1:N            │ 1:N
           ▼               ▼               ▼                ▼
  ┌─────────────────┐ ┌──────────┐ ┌──────────────┐ ┌──────────────────┐
  │user_identities  │ │user_     │ │meal_records  │ │decision_sessions │
  │(platform,openId)│ │preferences│ │              │ │                  │
  └─────────────────┘ └──────────┘ └──────┬───────┘ └────────┬─────────┘
                                           │ N:1              │ N:1
                                           ▼                  │
                                    ┌─────────────┐           │
                                    │   dishes    │◄──────────┘
                                    └──────┬──────┘  (chosen_dish_id)
                                           │ 1:1
                                           ▼
                                    ┌─────────────┐
                                    │   recipes   │
                                    └─────────────┘

  ┌──────────────────────┐   ┌──────────────────┐   ┌───────────────────┐
  │ai_recognition_logs   │   │user_dish_stats   │   │user_cuisine_stats │
  │(派生 → meal_records) │   │(派生, 可重建)     │   │(派生, 可重建)      │
  └──────────────────────┘   └──────────────────┘   └───────────────────┘
```

**核心表**：`users`、`user_identities`、`user_preferences`、`dishes`、`recipes`、`meal_records`
**辅助表**：`decision_sessions`、`ai_recognition_logs`
**派生表**（可随时重算）：`user_dish_stats`、`user_cuisine_stats`

---

## 3. 枚举定义

> 所有枚举以 `smallint` 存库。**枚举值一旦发布不可复用/修改**，只能追加。

```csharp
// FoodMate.Core/Enums/

/// <summary>登录平台</summary>
public enum Platform : short
{
    WeChat  = 1,
    Alipay  = 2,
    Douyin  = 3,
    H5      = 4,
}

/// <summary>餐次</summary>
public enum MealType : short
{
    Breakfast = 1,  // 早餐 05:00–10:00
    Lunch     = 2,  // 午餐 10:00–15:00
    Dinner    = 3,  // 晚餐 15:00–21:00
    LateNight = 4,  // 夜宵 21:00–05:00
}

/// <summary>就餐方式</summary>
public enum DiningMode : short
{
    Whatever = 0,  // 随便（不限制）
    Takeout  = 1,  // 外卖
    DineIn   = 2,  // 堂食
    Homemade = 3,  // 自己做
}

/// <summary>菜系</summary>
public enum Cuisine : short
{
    Other      = 0,
    Sichuan    = 1,   // 川菜
    Cantonese  = 2,   // 粤菜
    Hunan      = 3,   // 湘菜
    Shandong   = 4,   // 鲁菜
    Jiangsu    = 5,   // 苏菜
    Zhejiang   = 6,   // 浙菜
    Fujian     = 7,   // 闽菜
    Anhui      = 8,   // 徽菜
    Northeast  = 9,   // 东北菜
    Northwest  = 10,  // 西北菜
    YunnanGuizhou = 11, // 云贵菜
    Japanese   = 12,  // 日料
    Korean     = 13,  // 韩餐
    Western    = 14,  // 西餐
    SoutheastAsian = 15, // 东南亚
    Snack      = 16,  // 快餐小吃
}

/// <summary>菜品分类</summary>
public enum DishCategory : short
{
    Staple    = 1,  // 主食（米饭/面/馒头）
    Meat      = 2,  // 荤菜
    Vegetable = 3,  // 素菜
    Soup      = 4,  // 汤羹
    Snack     = 5,  // 小吃
    Dessert   = 6,  // 甜点
    Drink     = 7,  // 饮品
    Breakfast = 8,  // 早餐单品
    Hotpot    = 9,  // 火锅烧烤
}

/// <summary>记录来源</summary>
public enum RecordSource : short
{
    Manual       = 1,  // 手动输入
    AiRecognized = 2,  // AI 拍照识别
    Decision     = 3,  // 决策页「就吃这个」
    DishLibrary  = 4,  // 菜品库选择
}

/// <summary>AI 识别日志状态</summary>
public enum AiLogStatus : short
{
    Success        = 1,  // 识别并解析成功
    ParseFailed    = 2,  // 模型返回但解析失败
    ModelFailed    = 3,  // 模型调用失败
    Confirmed      = 4,  // 用户已确认入库
}

/// <summary>时段位掩码（可组合）</summary>
[Flags]
public enum MealTimeMask : short
{
    None      = 0,
    Breakfast = 1 << 0,  // 1
    Lunch     = 1 << 1,  // 2
    Dinner    = 1 << 2,  // 4
    LateNight = 1 << 3,  // 8
    All       = Breakfast | Lunch | Dinner | LateNight, // 15
}

/// <summary>季节位掩码（0 = 四季皆宜）</summary>
[Flags]
public enum SeasonMask : short
{
    AllYear = 0,
    Spring  = 1 << 0,
    Summer  = 1 << 1,
    Autumn  = 1 << 2,
    Winter  = 1 << 3,
}
```

---

## 4. 表结构

### 4.1 `users` — 用户

| 字段 | 类型 | 约束 | 说明 |
|---|---|---|---|
| `id` | uuid | PK | |
| `nickname` | varchar(50) | null | 各端昵称，用户可改 |
| `avatar_url` | varchar(500) | null | |
| `status` | smallint | NOT NULL, default 1 | 1 正常 / 2 禁用 |
| `last_login_at` | timestamptz | null | |
| `created_at` | timestamptz | NOT NULL | |
| `updated_at` | timestamptz | NOT NULL | |

```sql
CREATE TABLE users (
    id            uuid         PRIMARY KEY,
    nickname      varchar(50),
    avatar_url    varchar(500),
    status        smallint     NOT NULL DEFAULT 1,
    last_login_at timestamptz,
    created_at    timestamptz  NOT NULL,
    updated_at    timestamptz  NOT NULL
);
```

---

### 4.2 `user_identities` — 三方身份（多端登录核心）

| 字段 | 类型 | 约束 | 说明 |
|---|---|---|---|
| `id` | uuid | PK | |
| `user_id` | uuid | FK → users, NOT NULL | |
| `platform` | smallint | NOT NULL | 见 `Platform` |
| `open_id` | varchar(128) | NOT NULL | 各端唯一标识 |
| `union_id` | varchar(128) | null | 仅微信有，且需绑定开放平台 |
| `created_at` | timestamptz | NOT NULL | |

```sql
CREATE TABLE user_identities (
    id         uuid         PRIMARY KEY,
    user_id    uuid         NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    platform   smallint     NOT NULL,
    open_id    varchar(128) NOT NULL,
    union_id   varchar(128),
    created_at timestamptz  NOT NULL
);

-- 登录查找的唯一入口
CREATE UNIQUE INDEX ux_user_identities_platform_openid
    ON user_identities (platform, open_id);

CREATE INDEX ix_user_identities_user_id ON user_identities (user_id);

-- 为将来跨端账号合并预留
CREATE INDEX ix_user_identities_union_id
    ON user_identities (union_id) WHERE union_id IS NOT NULL;
```

> ⚠️ **v1 不做跨端账号合并**。同一人在微信和抖音是两个独立 `users` 记录。`union_id` 字段先落库，为 v2 合并预留。

---

### 4.3 `user_preferences` — 口味画像

| 字段 | 类型 | 约束 | 说明 |
|---|---|---|---|
| `user_id` | uuid | PK, FK → users | 1:1 |
| `spicy_level` | smallint | NOT NULL, default 2 | 0–5，0 完全不吃辣 |
| `budget_min_cents` | int | NOT NULL, default 1500 | ¥15 |
| `budget_max_cents` | int | NOT NULL, default 5000 | ¥50 |
| `avoid_ingredients` | text | NOT NULL, default `'[]'` | JSON `string[]`，**硬过滤** |
| `preferred_cuisines` | text | NOT NULL, default `'[]'` | JSON `number[]`（Cuisine 值） |
| `dining_mode_weights` | text | NOT NULL, default `'{}'` | JSON `{"takeout":0.33,"dineIn":0.33,"homemade":0.34}` |
| `max_distance_m` | int | NOT NULL, default 2000 | 可接受距离（米） |
| `onboarding_completed` | bool | NOT NULL, default false | 是否完成 5 道偏好题 |
| `updated_at` | timestamptz | NOT NULL | |

```sql
CREATE TABLE user_preferences (
    user_id              uuid        PRIMARY KEY REFERENCES users(id) ON DELETE CASCADE,
    spicy_level          smallint    NOT NULL DEFAULT 2,
    budget_min_cents     int         NOT NULL DEFAULT 1500,
    budget_max_cents     int         NOT NULL DEFAULT 5000,
    avoid_ingredients    text        NOT NULL DEFAULT '[]',
    preferred_cuisines   text        NOT NULL DEFAULT '[]',
    dining_mode_weights  text        NOT NULL DEFAULT '{}',
    max_distance_m       int         NOT NULL DEFAULT 2000,
    onboarding_completed boolean     NOT NULL DEFAULT false,
    updated_at           timestamptz NOT NULL,

    CONSTRAINT ck_spicy_range CHECK (spicy_level BETWEEN 0 AND 5),
    CONSTRAINT ck_budget_range CHECK (budget_min_cents >= 0 AND budget_max_cents >= budget_min_cents)
);
```

**画像更新来源**（`PreferenceUpdater`）：

| 画像字段 | 冷启动 | 自动学习来源 |
|---|---|---|
| `spicy_level` | 偏好题 | 最近 30 天记录中菜品的 `spicy_level` 加权中位数 |
| `budget_*` | 偏好题 | 最近 20 条记录的 `dish.estimated_price` 分位数 |
| `preferred_cuisines` | 偏好题 | 最近 60 天记录中菜系频次 Top 3 + 高评分菜系 |
| `dining_mode_weights` | 默认均分 | 各 `dining_mode` 的历史占比 |
| `avoid_ingredients` | 偏好题 | **不自动学习**（安全字段，只由用户显式设置） |

> ⚠️ `avoid_ingredients` **绝不由算法推断**。误判会导致过敏风险。

---

### 4.4 `dishes` — 菜品库

| 字段 | 类型 | 约束 | 说明 |
|---|---|---|---|
| `id` | uuid | PK | |
| `name` | varchar(60) | NOT NULL | 菜名 |
| `aliases` | text | NOT NULL, default `'[]'` | JSON `string[]`，别名（如「西红柿炒蛋」/「番茄炒蛋」） |
| `cuisine` | smallint | NOT NULL | 见 `Cuisine` |
| `category` | smallint | NOT NULL | 见 `DishCategory` |
| `spicy_level` | smallint | NOT NULL, default 0 | 0–5 |
| `price_min_cents` | int | null | 参考价下限 |
| `price_max_cents` | int | null | 参考价上限 |
| `calories` | int | null | 每份估算 kcal |
| `ingredients` | text | NOT NULL, default `'[]'` | JSON `[{name, amount, is_common_allergen}]` |
| `tags` | text | NOT NULL, default `'[]'` | JSON `string[]`（重口/清淡/快手/下饭/暖胃…） |
| `meal_times` | smallint | NOT NULL, default 15 | `MealTimeMask` 位掩码 |
| `seasons` | smallint | NOT NULL, default 0 | `SeasonMask` 位掩码，0=四季 |
| `image_url` | varchar(500) | null | |
| `description` | varchar(500) | null | 一句话描述 |
| `is_builtin` | bool | NOT NULL, default true | true=平台内置，false=用户自定义 |
| `owner_user_id` | uuid | null, FK → users | 仅用户自定义菜品有值 |
| `popularity` | int | NOT NULL, default 0 | 全局热度，冷启动兜底 |
| `is_active` | bool | NOT NULL, default true | 下架标记 |
| `is_deleted` | bool | NOT NULL, default false | |
| `created_at` | timestamptz | NOT NULL | |
| `updated_at` | timestamptz | NOT NULL | |

```sql
CREATE TABLE dishes (
    id              uuid         PRIMARY KEY,
    name            varchar(60)  NOT NULL,
    aliases         text         NOT NULL DEFAULT '[]',
    cuisine         smallint     NOT NULL,
    category        smallint     NOT NULL,
    spicy_level     smallint     NOT NULL DEFAULT 0,
    price_min_cents int,
    price_max_cents int,
    calories        int,
    ingredients     text         NOT NULL DEFAULT '[]',
    tags            text         NOT NULL DEFAULT '[]',
    meal_times      smallint     NOT NULL DEFAULT 15,
    seasons         smallint     NOT NULL DEFAULT 0,
    image_url       varchar(500),
    description     varchar(500),
    is_builtin      boolean      NOT NULL DEFAULT true,
    owner_user_id   uuid         REFERENCES users(id) ON DELETE CASCADE,
    popularity      int          NOT NULL DEFAULT 0,
    is_active       boolean      NOT NULL DEFAULT true,
    is_deleted      boolean      NOT NULL DEFAULT false,
    created_at      timestamptz  NOT NULL,
    updated_at      timestamptz  NOT NULL,

    CONSTRAINT ck_dish_spicy CHECK (spicy_level BETWEEN 0 AND 5),
    CONSTRAINT ck_dish_owner CHECK (
        (is_builtin = true  AND owner_user_id IS NULL) OR
        (is_builtin = false AND owner_user_id IS NOT NULL)
    )
);
```

**索引策略**

```sql
-- 决策候选集主查询：内置菜 + 当前用户自定义菜 + 未删除 + 上架
CREATE INDEX ix_dishes_candidates
    ON dishes (is_active, is_deleted, cuisine, category);

CREATE INDEX ix_dishes_owner
    ON dishes (owner_user_id) WHERE owner_user_id IS NOT NULL;

-- 冷启动兜底排序
CREATE INDEX ix_dishes_popularity
    ON dishes (popularity DESC) WHERE is_active = true AND is_deleted = false;

-- 菜名检索（前缀匹配）
CREATE INDEX ix_dishes_name ON dishes (name);
```

> 💡 **检索实现说明**：MVP 阶段菜品总量约 300–2000 条，**全部加载到内存做过滤/匹配**比数据库模糊查询更快也更灵活（支持别名、拼音、模糊匹配）。`GET /dishes?keyword=` 在应用层用 `string.Contains` + 别名匹配实现。等数据量超过 1 万再引入 PG `pg_trgm` 或全文检索。

**`ingredients` JSON 结构**

```json
[
  { "name": "牛肉", "amount": "300g", "isCommonAllergen": false },
  { "name": "花生", "amount": "50g",  "isCommonAllergen": true  }
]
```

> `isCommonAllergen` 标记常见过敏原（花生、坚果、海鲜、鸡蛋、牛奶、麸质、大豆）。忌口过滤时同时匹配 `name` 与常见过敏原，双保险。

---

### 4.5 `recipes` — 菜谱（「自己做」模式）

| 字段 | 类型 | 约束 | 说明 |
|---|---|---|---|
| `id` | uuid | PK | |
| `dish_id` | uuid | FK → dishes, UNIQUE | 1:1 |
| `servings` | smallint | NOT NULL, default 2 | 份数 |
| `cook_minutes` | smallint | NOT NULL | 烹饪时长（分钟） |
| `difficulty` | smallint | NOT NULL, default 1 | 1 简单 / 2 中等 / 3 复杂 |
| `steps` | text | NOT NULL | JSON `[{order, text, durationMinutes?}]` |
| `tips` | varchar(500) | null | 小贴士 |
| `created_at` | timestamptz | NOT NULL | |

```sql
CREATE TABLE recipes (
    id           uuid         PRIMARY KEY,
    dish_id      uuid         NOT NULL UNIQUE REFERENCES dishes(id) ON DELETE CASCADE,
    servings     smallint     NOT NULL DEFAULT 2,
    cook_minutes smallint     NOT NULL,
    difficulty   smallint     NOT NULL DEFAULT 1,
    steps        text         NOT NULL,
    tips         varchar(500),
    created_at   timestamptz  NOT NULL,

    CONSTRAINT ck_recipe_difficulty CHECK (difficulty BETWEEN 1 AND 3)
);

CREATE INDEX ix_recipes_cook_minutes ON recipes (cook_minutes);
```

**`steps` JSON 结构**

```json
[
  { "order": 1, "text": "牛腩切块，冷水下锅焯水 3 分钟，捞出洗净", "durationMinutes": 3 },
  { "order": 2, "text": "热锅冷油，下姜片葱段爆香", "durationMinutes": 1 }
]
```

---

### 4.6 `meal_records` — 饮食记录（留存核心）

| 字段 | 类型 | 约束 | 说明 |
|---|---|---|---|
| `id` | uuid | PK | |
| `user_id` | uuid | FK → users, NOT NULL | |
| `dish_id` | uuid | null, FK → dishes | 可能为 null（自定义输入且未入库） |
| `dish_name` | varchar(60) | NOT NULL | **快照**，菜品改名不影响历史 |
| `dish_snapshot` | text | NOT NULL, default `'{}'` | **快照** JSON，见下 |
| `meal_type` | smallint | NOT NULL | 见 `MealType` |
| `dining_mode` | smallint | NOT NULL | 见 `DiningMode` |
| `eaten_at` | timestamptz | NOT NULL | 实际就餐时间 |
| `servings` | numeric(4,2) | NOT NULL, default 1.0 | 份量系数（0.5 = 半份） |
| `calories` | int | null | 估算热量（已乘 servings） |
| `rating` | smallint | null | 1–5，**决策引擎最强信号** |
| `would_eat_again` | bool | null | 还想再吃吗 |
| `photo_url` | varchar(500) | null | |
| `note` | varchar(200) | null | |
| `source` | smallint | NOT NULL | 见 `RecordSource` |
| `decision_session_id` | uuid | null, FK → decision_sessions | 来自哪次决策 |
| `ai_recognition_log_id` | uuid | null, FK → ai_recognition_logs | 来自哪次 AI 识别 |
| `is_deleted` | bool | NOT NULL, default false | |
| `created_at` | timestamptz | NOT NULL | |
| `updated_at` | timestamptz | NOT NULL | |

```sql
CREATE TABLE meal_records (
    id                    uuid          PRIMARY KEY,
    user_id               uuid          NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    dish_id               uuid          REFERENCES dishes(id) ON DELETE SET NULL,
    dish_name             varchar(60)   NOT NULL,
    dish_snapshot         text          NOT NULL DEFAULT '{}',
    meal_type             smallint      NOT NULL,
    dining_mode           smallint      NOT NULL,
    eaten_at              timestamptz   NOT NULL,
    servings              numeric(4,2)  NOT NULL DEFAULT 1.0,
    calories              int,
    rating                smallint,
    would_eat_again       boolean,
    photo_url             varchar(500),
    note                  varchar(200),
    source                smallint      NOT NULL,
    decision_session_id   uuid          REFERENCES decision_sessions(id) ON DELETE SET NULL,
    ai_recognition_log_id uuid          REFERENCES ai_recognition_logs(id) ON DELETE SET NULL,
    is_deleted            boolean       NOT NULL DEFAULT false,
    created_at            timestamptz   NOT NULL,
    updated_at            timestamptz   NOT NULL,

    CONSTRAINT ck_record_rating   CHECK (rating IS NULL OR rating BETWEEN 1 AND 5),
    CONSTRAINT ck_record_servings CHECK (servings > 0 AND servings <= 10)
);

-- 记录列表：按用户 + 时间倒序（最高频查询）
CREATE INDEX ix_meal_records_user_eaten
    ON meal_records (user_id, eaten_at DESC) WHERE is_deleted = false;

-- 新鲜度计算：某用户最近吃过哪些菜
CREATE INDEX ix_meal_records_user_dish
    ON meal_records (user_id, dish_id, eaten_at DESC) WHERE is_deleted = false;

-- 画像学习：按菜系聚合
CREATE INDEX ix_meal_records_user_created
    ON meal_records (user_id, created_at DESC) WHERE is_deleted = false;

-- 待评分提醒
CREATE INDEX ix_meal_records_pending_rating
    ON meal_records (user_id, eaten_at DESC)
    WHERE rating IS NULL AND is_deleted = false;
```

**`dish_snapshot` JSON 结构**

```json
{
  "cuisine": 1,
  "category": 2,
  "spicyLevel": 3,
  "caloriesPerServing": 420,
  "priceCents": 3800,
  "tags": ["重口", "下饭"]
}
```

> 💡 **为什么要快照？** 菜品可能被平台改名、调整辣度、下架。历史记录必须反映**当时吃的是什么**。决策引擎计算新鲜度时用 `dish_id`，展示和统计时用快照。

---

### 4.7 `decision_sessions` — 决策会话

| 字段 | 类型 | 约束 | 说明 |
|---|---|---|---|
| `id` | uuid | PK | |
| `user_id` | uuid | FK → users, NOT NULL | |
| `meal_type` | smallint | NOT NULL | 请求时的餐次 |
| `dining_mode` | smallint | NOT NULL | |
| `party_size` | smallint | NOT NULL, default 1 | 人数 |
| `budget_min_cents` | int | null | 本次覆盖值 |
| `budget_max_cents` | int | null | |
| `latitude` | numeric(9,6) | null | |
| `longitude` | numeric(9,6) | null | |
| `weather` | varchar(30) | null | 如 `rain` / `hot` / `cold` / `clear` |
| `mood_tags` | text | NOT NULL, default `'[]'` | JSON `string[]`（辣/清淡/暖胃/快手） |
| `candidate_count` | int | NOT NULL | 候选集大小（诊断用） |
| `candidates` | text | NOT NULL | JSON，Top 20 明细含打分明细 |
| `chosen_dish_id` | uuid | null, FK → dishes | 用户最终选了哪个 |
| `chosen_at` | timestamptz | null | |
| `engine_version` | varchar(20) | NOT NULL | 引擎版本，便于 A/B 与回溯 |
| `elapsed_ms` | int | NOT NULL | 引擎耗时 |
| `created_at` | timestamptz | NOT NULL | |

```sql
CREATE TABLE decision_sessions (
    id               uuid         PRIMARY KEY,
    user_id          uuid         NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    meal_type        smallint     NOT NULL,
    dining_mode      smallint     NOT NULL,
    party_size       smallint     NOT NULL DEFAULT 1,
    budget_min_cents int,
    budget_max_cents int,
    latitude         numeric(9,6),
    longitude        numeric(9,6),
    weather          varchar(30),
    mood_tags        text         NOT NULL DEFAULT '[]',
    candidate_count  int          NOT NULL,
    candidates       text         NOT NULL,
    chosen_dish_id   uuid         REFERENCES dishes(id) ON DELETE SET NULL,
    chosen_at        timestamptz,
    engine_version   varchar(20)  NOT NULL,
    elapsed_ms       int          NOT NULL,
    created_at       timestamptz  NOT NULL
);

CREATE INDEX ix_decision_sessions_user_created
    ON decision_sessions (user_id, created_at DESC);
```

**`candidates` JSON 结构**

```json
[
  {
    "dishId": "…",
    "name": "番茄牛腩",
    "totalScore": 92.4,
    "breakdown": {
      "taste": 0.88, "freshness": 0.95, "affinity": 0.90,
      "timeSlot": 1.00, "budget": 1.00, "context": 0.80, "exploration": 0.30
    },
    "penalties": [],
    "reasons": ["你最近吃辣偏多，换点温和的", "上次你给它打了 5 分"]
  }
]
```

> 💡 **这张表是产品改进的金矿**：记录了每次推荐的完整上下文和用户最终选择。可以分析「推荐了但没选」的模式，反推权重是否合理。

---

### 4.8 `ai_recognition_logs` — AI 识别日志（数据资产）

| 字段 | 类型 | 约束 | 说明 |
|---|---|---|---|
| `id` | uuid | PK | |
| `user_id` | uuid | FK → users, NOT NULL | |
| `image_url` | varchar(500) | NOT NULL | |
| `model_name` | varchar(60) | NOT NULL | 如 `qwen-vl-max` |
| `status` | smallint | NOT NULL | 见 `AiLogStatus` |
| `raw_response` | text | null | 模型原始返回（诊断用） |
| `parsed_result` | text | null | 解析后的结构化 JSON |
| `corrected_result` | text | null | **用户修正后**的 JSON ← 黄金数据 |
| `is_corrected` | bool | NOT NULL, default false | |
| `error_message` | varchar(500) | null | |
| `prompt_tokens` | int | null | |
| `completion_tokens` | int | null | |
| `latency_ms` | int | NOT NULL | |
| `created_at` | timestamptz | NOT NULL | |

```sql
CREATE TABLE ai_recognition_logs (
    id                uuid         PRIMARY KEY,
    user_id           uuid         NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    image_url         varchar(500) NOT NULL,
    model_name        varchar(60)  NOT NULL,
    status            smallint     NOT NULL,
    raw_response      text,
    parsed_result     text,
    corrected_result  text,
    is_corrected      boolean      NOT NULL DEFAULT false,
    error_message     varchar(500),
    prompt_tokens     int,
    completion_tokens int,
    latency_ms        int          NOT NULL,
    created_at        timestamptz  NOT NULL
);

CREATE INDEX ix_ai_logs_user_created
    ON ai_recognition_logs (user_id, created_at DESC);

-- 监控：识别失败率
CREATE INDEX ix_ai_logs_status_created
    ON ai_recognition_logs (status, created_at DESC);

-- 数据资产：被修正过的样本
CREATE INDEX ix_ai_logs_corrected
    ON ai_recognition_logs (created_at DESC) WHERE is_corrected = true;
```

**`parsed_result` / `corrected_result` JSON 结构**

```json
{
  "items": [
    {
      "name": "番茄牛腩",
      "confidence": 0.92,
      "estimatedCalories": 420,
      "ingredients": ["牛腩", "番茄", "洋葱"],
      "matchedDishId": "…",
      "portion": 1.0
    },
    {
      "name": "米饭",
      "confidence": 0.61,
      "estimatedCalories": 230,
      "ingredients": ["大米"],
      "matchedDishId": null,
      "portion": 1.0
    }
  ],
  "scene": "dine_in",
  "overallConfidence": 0.78
}
```

> 💡 **`corrected_result` 是最有价值的数据**：它是「模型错在哪」的标注。积累到一定量后可用于：
> ① 优化 Prompt ② few-shot 示例库 ③ 微调模型 ④ 扩充菜品库别名

---

### 4.9 `user_dish_stats` — 用户×菜品统计（派生，可重建）

| 字段 | 类型 | 约束 | 说明 |
|---|---|---|---|
| `user_id` | uuid | PK(联合), FK → users | |
| `dish_id` | uuid | PK(联合), FK → dishes | |
| `eat_count` | int | NOT NULL, default 0 | 累计次数（按 servings 加权） |
| `last_eaten_at` | timestamptz | null | **新鲜度计算核心** |
| `rating_sum` | int | NOT NULL, default 0 | 评分总和 |
| `rating_count` | int | NOT NULL, default 0 | 评分次数 |
| `would_eat_again_count` | int | NOT NULL, default 0 | |
| `updated_at` | timestamptz | NOT NULL | |

```sql
CREATE TABLE user_dish_stats (
    user_id                uuid        NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    dish_id                uuid        NOT NULL REFERENCES dishes(id) ON DELETE CASCADE,
    eat_count              int         NOT NULL DEFAULT 0,
    last_eaten_at          timestamptz,
    rating_sum             int         NOT NULL DEFAULT 0,
    rating_count           int         NOT NULL DEFAULT 0,
    would_eat_again_count  int         NOT NULL DEFAULT 0,
    updated_at             timestamptz NOT NULL,

    PRIMARY KEY (user_id, dish_id)
);

CREATE INDEX ix_user_dish_stats_last_eaten
    ON user_dish_stats (user_id, last_eaten_at DESC);
```

**维护方式**：记录增删改时**增量更新**（同一事务内）。提供 `POST /admin/rebuild-stats` 从 `meal_records` 全量重算。

**全量重算 SQL**

```sql
DELETE FROM user_dish_stats;

INSERT INTO user_dish_stats (
    user_id, dish_id, eat_count, last_eaten_at,
    rating_sum, rating_count, would_eat_again_count, updated_at
)
SELECT
    user_id,
    dish_id,
    SUM(servings)::int,
    MAX(eaten_at),
    COALESCE(SUM(rating), 0)::int,
    COUNT(rating)::int,
    COUNT(*) FILTER (WHERE would_eat_again = true)::int,
    now()
FROM meal_records
WHERE is_deleted = false AND dish_id IS NOT NULL
GROUP BY user_id, dish_id;
```

---

### 4.10 `user_cuisine_stats` — 用户×菜系统计（派生，可重建）

用于**菜系级新鲜度惩罚**（避免连续多天吃同一菜系）。

| 字段 | 类型 | 约束 | 说明 |
|---|---|---|---|
| `user_id` | uuid | PK(联合), FK → users | |
| `cuisine` | smallint | PK(联合) | 见 `Cuisine` |
| `eat_count_total` | int | NOT NULL, default 0 | |
| `last_eaten_at` | timestamptz | null | |
| `updated_at` | timestamptz | NOT NULL | |

```sql
CREATE TABLE user_cuisine_stats (
    user_id         uuid        NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    cuisine         smallint    NOT NULL,
    eat_count_total int         NOT NULL DEFAULT 0,
    last_eaten_at   timestamptz,
    updated_at      timestamptz NOT NULL,

    PRIMARY KEY (user_id, cuisine)
);

CREATE INDEX ix_user_cuisine_stats_last_eaten
    ON user_cuisine_stats (user_id, last_eaten_at DESC);
```

**全量重算 SQL**

```sql
DELETE FROM user_cuisine_stats;

INSERT INTO user_cuisine_stats (user_id, cuisine, eat_count_total, last_eaten_at, updated_at)
SELECT
    r.user_id,
    d.cuisine,
    SUM(r.servings)::int,
    MAX(r.eaten_at),
    now()
FROM meal_records r
JOIN dishes d ON d.id = r.dish_id
WHERE r.is_deleted = false
GROUP BY r.user_id, d.cuisine;
```

---

## 5. 决策引擎的数据读取路径

决策引擎需要的数据及其来源：

| 引擎输入 | 数据来源 | 查询 |
|---|---|---|
| 候选菜品集 | `dishes` | 内置菜 + 当前用户自定义菜，`is_active && !is_deleted` |
| 用户画像 | `user_preferences` | 按 `user_id` 单条 |
| 菜品级历史 | `user_dish_stats` | 按 `user_id` 取该用户全部（通常 < 500 条） |
| 菜系级历史 | `user_cuisine_stats` | 按 `user_id` 取全部（≤ 17 条） |
| 最近记录（7/30 天） | `meal_records` | 用于精细新鲜度，可选用 |

**典型查询（EF Core）**

```csharp
// 1. 候选集（含忌口硬过滤在应用层做，因为要匹配 JSON 内容）
var candidates = await db.Dishes
    .Where(d => d.IsActive && !d.IsDeleted
             && (d.IsBuiltin || d.OwnerUserId == userId))
    .ToListAsync(ct);

// 2. 画像
var pref = await db.UserPreferences
    .FirstOrDefaultAsync(p => p.UserId == userId, ct);

// 3. 历史统计（一次取全，内存 JOIN）
var dishStats = await db.UserDishStats
    .Where(s => s.UserId == userId)
    .ToDictionaryAsync(s => s.DishId, ct);

var cuisineStats = await db.UserCuisineStats
    .Where(s => s.UserId == userId)
    .ToDictionaryAsync(s => s.Cuisine, ct);
```

> 💡 **性能估算**：候选 2000 条 × 7 个 Scorer = 14000 次简单计算，C# 内存中 < 5ms。**不需要缓存**，每次实时计算即可保证数据最新。

---

## 6. 种子数据策略

### 6.1 数量与构成

| 分类 | 数量 | 说明 |
|---|---|---|
| 主食 | 30 | 米饭、面食、粉、馒头、粥 |
| 荤菜 | 100 | 各菜系代表性肉类/水产 |
| 素菜 | 60 | 家常蔬菜做法 |
| 汤羹 | 25 | |
| 早餐 | 30 | 豆浆油条、包子、三明治等 |
| 小吃 | 30 | |
| 饮品/甜点 | 25 | |
| 火锅烧烤 | 20 | |
| **合计** | **≈ 320** | |

**关键原则：宁少勿滥。** 300 道**高频家常菜**比 3000 道冷门菜有用得多。用户看到「没听过的菜」会降低信任。

### 6.2 数据来源方案对比

| 方案 | 质量 | 工作量 | 风险 |
|---|---|---|---|
| **A. 人工整理** | 高 | 大（约 15–20 小时） | 无 |
| **B. 公开数据集** | 中 | 小 | 需核实授权许可，字段常不匹配 |
| **C. AI 批量生成 + 人工校对** | 中高 | 中（生成 1h + 校对 6h） | 需检查事实性错误（辣度/热量） |
| **D. A + C 混合（推荐）** | 高 | 中 | 无 |

**推荐 D**：先用 AI 生成 320 道菜的**结构化草稿**（菜名/菜系/分类/辣度/热量/食材/标签/时段），再人工逐条校对关键字段（**辣度、热量、食材过敏原**必须人工确认）。

### 6.3 种子数据格式

存为 `src/FoodMate.Infrastructure/Data/Seed/dishes.seed.json`：

```json
[
  {
    "name": "番茄牛腩",
    "aliases": ["西红柿炖牛腩", "番茄炖牛肉"],
    "cuisine": 1,
    "category": 2,
    "spicyLevel": 1,
    "priceMinCents": 3200,
    "priceMaxCents": 4800,
    "calories": 420,
    "ingredients": [
      { "name": "牛腩", "amount": "500g", "isCommonAllergen": false },
      { "name": "番茄", "amount": "3个",  "isCommonAllergen": false },
      { "name": "洋葱", "amount": "半个", "isCommonAllergen": false }
    ],
    "tags": ["下饭", "暖胃", "炖菜"],
    "mealTimes": 6,
    "seasons": 0,
    "description": "酸甜开胃，牛腩软烂，汤汁拌饭一绝",
    "popularity": 88,
    "recipe": {
      "servings": 2,
      "cookMinutes": 90,
      "difficulty": 2,
      "steps": [
        { "order": 1, "text": "牛腩切块冷水下锅，加姜片料酒焯水 3 分钟，捞出温水洗净", "durationMinutes": 3 },
        { "order": 2, "text": "番茄顶部划十字，开水烫 30 秒去皮切块", "durationMinutes": 2 },
        { "order": 3, "text": "热锅冷油下洋葱炒软，加番茄炒出汁", "durationMinutes": 5 },
        { "order": 4, "text": "下牛腩翻炒，加热水没过食材，大火烧开转小火炖 60 分钟", "durationMinutes": 60 },
        { "order": 5, "text": "加盐调味，大火收汁 5 分钟", "durationMinutes": 5 }
      ],
      "tips": "一定要加热水，冷水会让牛腩肉质变紧"
    }
  }
]
```

`mealTimes: 6` = `Lunch | Dinner`（2+4）。
`seasons: 0` = 四季皆宜。

### 6.4 导入方式

- **开发环境**：EF Core `HasData` 或启动时 `DbInitializer` 读取 JSON 导入（幂等，按 `name` 判重）。
- **生产环境**：`DbInitializer` 仅在 `dishes` 表为空时导入，避免覆盖运营调整。
- **增量更新**：后续菜品更新走单独的 migration + 数据脚本，不用 `HasData`。

---

## 7. EF Core 配置要点

### 7.1 JSON 字段 ValueConverter

```csharp
// FoodMate.Infrastructure/Data/Configurations/JsonConverters.cs
public static class JsonConverters
{
    public static readonly ValueConverter<List<string>, string> StringList =
        new(
            v => JsonSerializer.Serialize(v, JsonOpts.Default),
            v => JsonSerializer.Deserialize<List<string>>(v, JsonOpts.Default) ?? new());

    public static readonly ValueConverter<List<int>, string> IntList =
        new(
            v => JsonSerializer.Serialize(v, JsonOpts.Default),
            v => JsonSerializer.Deserialize<List<int>>(v, JsonOpts.Default) ?? new());

    public static readonly ValueConverter<List<Ingredient>, string> Ingredients =
        new(
            v => JsonSerializer.Serialize(v, JsonOpts.Default),
            v => JsonSerializer.Deserialize<List<Ingredient>>(v, JsonOpts.Default) ?? new());

    // ... 其他
}
```

### 7.2 双 Provider 切换

```csharp
// FoodMate.Infrastructure/DependencyInjection.cs
public static IServiceCollection AddPersistence(
    this IServiceCollection services,
    IConfiguration config)
{
    var provider = config["Database:Provider"] ?? "Sqlite";
    var connectionString = config.GetConnectionString("Default")!;

    services.AddDbContext<FoodMateDbContext>(opt =>
    {
        switch (provider)
        {
            case "PostgreSql":
                opt.UseNpgsql(connectionString);
                break;
            case "Sqlite":
            default:
                opt.UseSqlite(connectionString);
                break;
        }
        opt.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking); // 读多写少
    });

    return services;
}
```

```jsonc
// appsettings.Development.json
{
  "Database": {
    "Provider": "Sqlite"
  },
  "ConnectionStrings": {
    "Default": "Data Source=foodmate.dev.db"
  }
}

// appsettings.Production.json
{
  "Database": {
    "Provider": "PostgreSql"
  },
  "ConnectionStrings": {
    "Default": ""   // 从环境变量注入
  }
}
```

### 7.3 时间字段

统一用 `DateTimeOffset`（映射 PG `timestamptz` / SQLite `TEXT`）。
**全链路 UTC 存储，展示层转本地时区。** 避免跨时区与夏令时问题。

```csharp
// 统一在 DbContext 中设置
protected override void OnModelCreating(ModelBuilder b)
{
    foreach (var entity in b.Model.GetEntityTypes())
    {
        foreach (var prop in entity.GetProperties())
        {
            if (prop.ClrType == typeof(DateTimeOffset) || prop.ClrType == typeof(DateTimeOffset?))
                prop.SetColumnType("timestamptz");   // SQLite 下 EF 自动忽略
        }
    }
}
```

---

## 8. 迁移与版本管理

| 事项 | 做法 |
|---|---|
| 迁移工具 | `dotnet ef migrations add <Name> -p src/FoodMate.Infrastructure -s src/FoodMate.Api` |
| 开发环境 | 启动时 `db.Database.Migrate()` 自动应用 |
| 生产环境 | **不自动迁移**，生成 SQL 脚本人工审核后执行：`dotnet ef migrations script` |
| 迁移目录 | `src/FoodMate.Infrastructure/Data/Migrations/` |
| 破坏性变更 | 必须两步走（先加字段 → 发版 → 再删旧字段） |

> ⚠️ **双 Provider 的迁移注意**：EF Core 迁移脚本含 Provider 特有语法。若两个 Provider 都要迁移，需分开维护迁移程序集或只对 PostgreSQL 生成迁移（SQLite 用 `EnsureCreated` 于开发环境）。

**推荐做法**：
- **PostgreSQL** → 正式迁移（`Migrations/`）
- **SQLite** → 开发环境用 `EnsureCreated()`，不做迁移

这样避免双套迁移的维护成本，同时保证生产可控。

---

## 9. 数据量预估与容量

| 表 | 单用户量级 | 1 万用户 | 10 万用户 |
|---|---|---|---|
| `users` | 1 | 1 万 | 10 万 |
| `user_identities` | 1–3 | 3 万 | 30 万 |
| `user_preferences` | 1 | 1 万 | 10 万 |
| `dishes` | — | 320（内置）+ 用户自定义 | 同 |
| `meal_records` | ~700/年 | 700 万/年 | 7000 万/年 |
| `decision_sessions` | ~500/年 | 500 万/年 | 5000 万/年 |
| `ai_recognition_logs` | ~300/年 | 300 万/年 | 3000 万/年 |
| `user_dish_stats` | ~200 | 200 万 | 2000 万 |

**容量规划建议**：
- `meal_records` 是主增长表 → 超过 1000 万行时考虑按 `user_id` 哈希分表或按年分区。
- `decision_sessions.candidates` 是 JSON 大字段 → 设置 **90 天 TTL**，定期归档或清理（只保留 `chosen_dish_id` 用于分析）。
- `ai_recognition_logs.raw_response` 体积大 → 30 天后清空 `raw_response`，保留 `parsed_result` 与 `corrected_result`。

**定期清理任务（后台作业）**

```sql
-- 清理 90 天前的决策明细（保留摘要）
UPDATE decision_sessions
SET candidates = '[]'
WHERE created_at < now() - interval '90 days' AND candidates <> '[]';

-- 清理 30 天前的模型原始返回
UPDATE ai_recognition_logs
SET raw_response = NULL
WHERE created_at < now() - interval '30 days' AND raw_response IS NOT NULL;
```

---

## 10. 待确认问题

| # | 问题 | 影响 |
|---|---|---|
| Q1 | 菜品图片从哪来？（自制 / 授权图库 / 不用图只做文字） | 影响 `dishes.image_url` 是否必填、对象存储成本 |
| Q2 | 是否需要「同一道菜不同餐厅」的维度？（即菜品 × 商家） | v1 明确不做商家维度，若需要则表结构需大改 |
| Q3 | 用户自定义菜品是否需要审核？（防止不当内容） | 个人主体需自行承担内容责任，建议加敏感词过滤 |
| Q4 | 记录是否允许「补录历史」（如补昨天的）？ | 影响 `eaten_at` 的校验规则 |

---

## 11. M0 实现说明（与本文档的差异）

M0 骨架已落地，以下实现细节与前述章节的示意代码有出入，**以实际代码为准**：

| 项 | 文档示意 | 实际实现 | 原因 |
|---|---|---|---|
| `dining_mode_weights` JSON 形状 | `{"1":0.33,"2":0.33,"3":0.34}` | `{"takeout":0.33,"dineIn":0.33,"homemade":0.34}` | 用强类型 `DiningModeWeights` 类替代 `Dictionary<DiningMode, double>`，规避枚举作为 JSON 字典键时的序列化歧义 |
| snake_case 列名 | 逐列 `.HasColumnName("...")` | 全局约定 `SnakeCaseConvention.ApplySnakeCaseNames()` | 免去 10 个实体的逐列样板代码；表名与列名自动转换，索引名保持 EF 默认 |
| JSON 字段比较器 | 未提及 | `JsonValueConverters.Comparer<T>()`，基于 JSON 快照比较 | 让 EF 能正确检测集合的原地修改，避免「改了没保存」 |
| 中文 JSON 转义 | 未提及 | `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` | 数据库中的 JSON 列需人眼可读，对 `corrected_result`（数据资产）的人工审查尤其重要 |
| 全局 NoTracking | `opt.UseQueryTrackingBehavior(NoTracking)` | **未启用**，改为读查询显式 `AsNoTracking()` | 全局 NoTracking 会让写操作的更新追踪出现隐蔽 bug，显式声明更安全 |
| 时间列类型 | 遍历模型 `SetColumnType("timestamptz")` | 未设置 | 让 EF 按 Provider 自行决定（PG `timestamptz` / SQLite `TEXT`），避免双 Provider 冲突 |
| 种子导入 | 「表为空时导入」 | **按菜名增量导入** | 既幂等，又能在扩展种子数据后让已有开发库自动补齐 |
| 迁移策略 | 双 Provider 各自迁移 | SQLite 用 `EnsureCreated`，PG 用 `Migrate` | 避免维护两套迁移脚本；PG 迁移将在 M4 上线前生成 |

**实测验证结果**（`dotnet ef dbcontext script` 生成的 DDL）：

- 10 张表全部生成，表名与列名均为 snake_case
- 检查约束生效：`ck_dishes_spicy_range`、`ck_user_preferences_budget_range`、`ck_meal_records_rating`、`ck_meal_records_servings`、`ck_recipes_difficulty`
- 21 个索引全部生成，含过滤条件（`WHERE NOT is_deleted`、`WHERE rating IS NULL AND NOT is_deleted`、`WHERE union_id IS NOT NULL`、`WHERE is_corrected`）与 DESC 排序
- 唯一索引：`ux_user_identities_platform_openid`、`ux_recipes_dish_id`

**种子数据现状**：20 道菜（含 6 份完整菜谱），覆盖 8 个分类、7 个菜系、全时段与早餐专属，含 2 道带过敏原标记的菜（宫保鸡丁含花生、麻辣香锅含虾）用于验证忌口过滤。M1 需扩充到约 320 道。
