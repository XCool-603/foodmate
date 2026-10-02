# 美食伴侣 · API 接口契约

> 配套文档：[总设计文档](./DESIGN.md) · [数据库设计](./DATABASE.md) · [决策引擎设计](./DECISION-ENGINE.md)

| 项目 | 内容 |
|---|---|
| 文档版本 | v1.0 |
| Base URL | `https://api.example.com/api/v1` |
| 协议 | HTTPS + JSON（UTF-8） |
| 认证 | JWT Bearer |
| 框架 | ASP.NET Core 10 Minimal API |

---

## 1. 通用约定

### 1.1 统一响应结构

**所有**接口（含错误）都返回同一外层结构：

```jsonc
{
  "code": 0,                      // 0 = 成功，非 0 = 业务错误码
  "message": "ok",                // 面向开发者的描述
  "data": { },                    // 业务数据；失败时为 null
  "traceId": "0HN7GQ2K9V3LM"      // 链路追踪 ID，排查问题时提供给后端
}
```

**成功**

```json
{ "code": 0, "message": "ok", "data": { "id": "3f2a…" }, "traceId": "0HN7…" }
```

**失败**

```json
{
  "code": 2003,
  "message": "菜品包含忌口食材，无法添加",
  "data": null,
  "traceId": "0HN7…"
}
```

> 💡 **实现方式**：`ApiResponseFilter`（`IEndpointFilter`）统一包装返回值，`ExceptionFilter` 统一转换异常。端点方法直接返回 DTO，不手动包 `code/message`。

### 1.2 HTTP 状态码使用

| 状态码 | 使用场景 |
|---|---|
| `200` | 业务成功（**包括业务失败**，如 `code=2003`） |
| `400` | 请求格式错误（JSON 解析失败、必填字段缺失） |
| `401` | 未登录 / token 无效或过期 |
| `403` | 无权访问该资源 |
| `404` | 路由不存在 |
| `429` | 触发限流 |
| `500` | 服务器内部错误 |

> ⚠️ **约定**：业务逻辑失败（如"忌口冲突"）返回 **HTTP 200 + 非零 code**，而不是 4xx。这样前端拦截器只需处理 401/429/5xx 三类异常，业务错误在 `then` 分支统一处理。

### 1.3 认证

除 `/auth/login`、`/health`、`/meta/*` 外，**所有**接口需要：

```http
Authorization: Bearer eyJhbGciOiJIUzI1NiIs...
```

**Token 策略**

| Token | 有效期 | 存储 | 说明 |
|---|---|---|---|
| Access Token | 2 小时 | 客户端内存 + Storage | JWT，无状态 |
| Refresh Token | 30 天 | 客户端 Storage，服务端存库 | 可撤销 |

### 1.4 时间与金额

| 类型 | 格式 | 示例 |
|---|---|---|
| 时间 | ISO 8601 UTC（带 `Z`） | `"2025-06-15T04:00:00Z"` |
| 日期 | `yyyy-MM-dd` | `"2025-06-15"` |
| 金额 | **整数，单位「分」**，字段名 `*Cents` | `3800` = ¥38.00 |
| 距离 | **整数，单位「米」**，字段名 `*Meters` | `800` = 800 米 |

> ⚠️ **客户端负责时区转换与金额格式化**。服务端只传 UTC 和「分」，避免时区/浮点问题。

### 1.5 分页

**请求**（Query String）

| 参数 | 类型 | 默认 | 约束 |
|---|---|---|---|
| `page` | int | 1 | ≥ 1 |
| `pageSize` | int | 20 | 1–100 |

**响应**

```jsonc
{
  "code": 0,
  "data": {
    "items": [ /* … */ ],
    "total": 137,
    "page": 1,
    "pageSize": 20,
    "hasMore": true
  }
}
```

### 1.6 幂等性

写操作（`POST`）支持幂等键，防止移动端双击/重试产生重复数据：

```http
Idempotency-Key: 550e8400-e29b-41d4-a716-446655440000
```

- 服务端缓存 `(userId, key)` → 首次响应，TTL 24 小时。
- 相同 key 重复请求直接返回首次结果，不重复执行。
- **必须支持**的接口：`POST /records`、`POST /ai/recognize/{logId}/confirm`、`POST /decisions/{id}/choose`。

### 1.7 错误码表

| code | HTTP | 含义 | 前端处理建议 |
|---|---|---|---|
| `0` | 200 | 成功 | — |
| `1001` | 401 | 未登录 / token 无效 | 触发重新登录 |
| `1002` | 401 | token 已过期 | 用 refresh token 续期后重放 |
| `1003` | 403 | 无权访问 | 提示并返回 |
| `1004` | 200 | 不支持的平台 | 提示「当前平台暂不支持」 |
| `1005` | 401 | refresh token 失效 | 清空本地凭证，重新登录 |
| `2001` | 200 | 参数校验失败 | 展示 `message` |
| `2002` | 200 | 资源不存在 | 提示并刷新列表 |
| `2003` | 200 | **忌口冲突** | 高亮提示，禁止提交 |
| `2004` | 200 | 记录不存在 | 刷新列表 |
| `2005` | 200 | 菜品已存在（重名） | 提示改名 |
| `3001` | 200 | AI 识别失败 | 降级为手动输入 |
| `3002` | 200 | AI 服务超时 | 提示重试 |
| `3003` | 429 | AI 配额超限 | 提示「今日识别次数已用完」 |
| `3004` | 200 | 图片格式不支持 | 提示支持格式 |
| `3005` | 200 | 图片过大 | 提示压缩 |
| `3006` | 200 | AI 返回解析失败 | 降级为手动输入 |
| `4001` | 200 | 候选菜品为空 | 提示放宽条件 |
| `5000` | 500 | 服务器内部错误 | 通用错误提示 |
| `5001` | 500 | 数据库错误 | 通用错误提示 |
| `5002` | 500 | 第三方服务不可用 | 通用错误提示 |

### 1.8 限流

| 接口组 | 限制 | 超限响应 |
|---|---|---|
| 全局（按用户） | 300 次/分钟 | 429 + `Retry-After` |
| `POST /ai/recognize` | **20 次/小时，100 次/天** | 429 + `code=3003` |
| `POST /ai/recipe` | 30 次/天 | 429 + `code=3003` |
| `POST /auth/login` | 20 次/小时（按 IP） | 429 |
| `POST /uploads/image` | 60 次/小时 | 429 |

响应头：

```http
X-RateLimit-Limit: 20
X-RateLimit-Remaining: 3
X-RateLimit-Reset: 1718438400
Retry-After: 1800
```

---

## 2. 端点总览

| # | 方法 | 路径 | 认证 | 说明 |
|---|---|---|---|---|
| **系统** | | | | |
| 1 | GET | `/health` | ❌ | 存活探针 |
| 2 | GET | `/health/ready` | ❌ | 就绪探针（含数据库） |
| 3 | GET | `/meta/enums` | ❌ | 枚举字典 |
| 4 | GET | `/meta/engine` | ❌ | 引擎版本与权重（用于展示打分明细） |
| **认证** | | | | |
| 5 | POST | `/auth/login` | ❌ | 三端统一登录 |
| 6 | POST | `/auth/refresh` | ❌ | 刷新 token |
| 7 | POST | `/auth/logout` | ✅ | 登出（撤销 refresh token） |
| 8 | GET | `/auth/me` | ✅ | 当前用户 |
| 9 | PUT | `/auth/me` | ✅ | 更新昵称/头像 |
| **画像** | | | | |
| 10 | GET | `/me/preference` | ✅ | 获取口味画像 |
| 11 | PUT | `/me/preference` | ✅ | 更新口味画像 |
| 12 | POST | `/me/preference/onboarding` | ✅ | 完成新用户引导 |
| 13 | GET | `/me/summary` | ✅ | 我的页数据卡片 |
| **决策** | | | | |
| 14 | POST | `/decisions/suggest` | ✅ | **核心：获取推荐** |
| 15 | POST | `/decisions/{sessionId}/choose` | ✅ | 记录用户选择 |
| 16 | GET | `/decisions/history` | ✅ | 决策历史 |
| **菜品** | | | | |
| 17 | GET | `/dishes` | ✅ | 菜品列表/搜索 |
| 18 | GET | `/dishes/{id}` | ✅ | 菜品详情 |
| 19 | POST | `/dishes` | ✅ | 创建自定义菜品 |
| 20 | PUT | `/dishes/{id}` | ✅ | 更新自定义菜品 |
| 21 | DELETE | `/dishes/{id}` | ✅ | 删除自定义菜品 |
| **记录** | | | | |
| 22 | POST | `/records` | ✅ | 创建记录 |
| 23 | GET | `/records` | ✅ | 记录列表 |
| 24 | GET | `/records/{id}` | ✅ | 记录详情 |
| 25 | PUT | `/records/{id}` | ✅ | 更新记录 |
| 26 | DELETE | `/records/{id}` | ✅ | 删除记录 |
| 27 | POST | `/records/{id}/rating` | ✅ | 快速评分 |
| 28 | GET | `/records/pending-rating` | ✅ | 待评分提醒 |
| 29 | GET | `/records/stats` | ✅ | 统计报表 |
| **AI** | | | | |
| 30 | POST | `/uploads/image` | ✅ | 上传图片 |
| 31 | POST | `/ai/recognize` | ✅ | 菜品识别 |
| 32 | POST | `/ai/recognize/{logId}/confirm` | ✅ | 确认识别结果 |
| 33 | POST | `/ai/recipe` | ✅ | 生成菜谱 |
| 34 | POST | `/ai/shopping-list` | ✅ | 生成买菜清单 |

---

## 3. 系统接口

### 3.1 `GET /health`

**响应** `200`

```json
{ "status": "healthy", "version": "1.0.0", "serverTime": "2025-06-15T04:00:00Z" }
```

> 此接口**不套统一响应结构**，便于监控系统直接解析。

### 3.2 `GET /health/ready`

```json
{ "status": "ready", "checks": { "database": "ok", "aiProvider": "ok" } }
```

数据库不通时返回 `503`。

### 3.3 `GET /meta/enums`

前端**不硬编码**枚举，启动时拉取并缓存。

```jsonc
{
  "code": 0,
  "data": {
    "platform":        [{ "value": 1, "label": "微信" }, { "value": 2, "label": "支付宝" }],
    "mealType":        [{ "value": 1, "label": "早餐" }, { "value": 2, "label": "午餐" },
                        { "value": 3, "label": "晚餐" }, { "value": 4, "label": "夜宵" }],
    "diningMode":      [{ "value": 0, "label": "随便" }, { "value": 1, "label": "外卖" },
                        { "value": 2, "label": "堂食" }, { "value": 3, "label": "自己做" }],
    "cuisine":         [{ "value": 1, "label": "川菜" }, { "value": 2, "label": "粤菜" }],
    "dishCategory":    [{ "value": 1, "label": "主食" }, { "value": 2, "label": "荤菜" }],
    "spicyLevel":      [{ "value": 0, "label": "不辣" }, { "value": 1, "label": "微辣" },
                        { "value": 2, "label": "小辣" }, { "value": 3, "label": "中辣" },
                        { "value": 4, "label": "重辣" }, { "value": 5, "label": "变态辣" }],
    "moodTag":         [{ "value": "辣的", "label": "想吃辣的" }],
    "avoidIngredient": [{ "value": "花生", "label": "花生" }],
    "recordSource":    [{ "value": 1, "label": "手动" }, { "value": 2, "label": "AI 识别" }]
  }
}
```

### 3.4 `GET /meta/engine`

前端用权重把 `breakdown` 转成「贡献度」条形图。

```jsonc
{
  "code": 0,
  "data": {
    "version": "1.0.0",
    "weights": {
      "taste": 0.25, "freshness": 0.20, "affinity": 0.15, "timeSlot": 0.15,
      "budget": 0.10, "context": 0.10, "exploration": 0.05
    },
    "dimensionLabels": {
      "taste": "口味匹配", "freshness": "新鲜度", "affinity": "你的偏好",
      "timeSlot": "时段合适", "budget": "预算匹配", "context": "场景契合",
      "exploration": "没吃过"
    }
  }
}
```

---

## 4. 认证接口

### 4.1 `POST /auth/login`

三端统一登录入口。前端先调 `platform.login()` 拿 `code`，再调此接口。

**请求**

```jsonc
{
  "platform": 1,                    // 1微信 2支付宝 3抖音 4H5
  "code": "081Abc123...",           // 各端登录凭证
  "nickname": "小明",                // 可选，部分平台前端能直接拿到
  "avatarUrl": "https://…"          // 可选
}
```

**响应** `200`

```jsonc
{
  "code": 0,
  "data": {
    "accessToken": "eyJhbGciOi…",
    "refreshToken": "dGhpcyBpcy…",
    "expiresIn": 7200,                       // 秒
    "isNewUser": true,                       // 首次注册
    "needsOnboarding": true,                 // 需完成偏好引导
    "user": {
      "id": "9c1f…",
      "nickname": "小明",
      "avatarUrl": "https://…",
      "createdAt": "2025-06-15T04:00:00Z"
    }
  }
}
```

> 💡 `needsOnboarding` 由服务端判断（`preference.onboardingCompleted == false`），前端据此决定是否跳引导页。

**错误**

| code | 场景 |
|---|---|
| `1004` | 不支持的 platform 值 |
| `2001` | code 为空 |
| `5002` | 平台服务器不可达 / code 无效 |

### 4.2 `POST /auth/refresh`

**请求**

```json
{ "refreshToken": "dGhpcyBpcy…" }
```

**响应**：同 `/auth/login` 的 token 字段（不含 `user`）。

**错误**：`1005` refresh token 失效 → 前端清空凭证重新登录。

### 4.3 `POST /auth/logout`

撤销当前 refresh token。**响应**：`{ "code": 0, "data": null }`

### 4.4 `GET /auth/me`

```jsonc
{
  "code": 0,
  "data": {
    "id": "9c1f…",
    "nickname": "小明",
    "avatarUrl": "https://…",
    "platform": 1,
    "createdAt": "2025-06-15T04:00:00Z",
    "needsOnboarding": false
  }
}
```

### 4.5 `PUT /auth/me`

```jsonc
// 请求（字段可选，只传要改的）
{ "nickname": "美食家小明", "avatarUrl": "https://…" }
```

---

## 5. 口味画像接口

### 5.1 `GET /me/preference`

```jsonc
{
  "code": 0,
  "data": {
    "spicyLevel": 2,
    "budgetMinCents": 1500,
    "budgetMaxCents": 5000,
    "avoidIngredients": ["香菜", "花生"],
    "preferredCuisines": [1, 2],
    "diningModeWeights": { "1": 0.4, "2": 0.4, "3": 0.2 },
    "maxDistanceMeters": 2000,
    "onboardingCompleted": true,
    "updatedAt": "2025-06-15T04:00:00Z"
  }
}
```

> 若用户从未设置过，服务端返回**默认画像**（不返回 404），保证前端无需处理空态。

### 5.2 `PUT /me/preference`

**请求**（全量覆盖，字段可选）

```jsonc
{
  "spicyLevel": 1,
  "budgetMinCents": 2000,
  "budgetMaxCents": 6000,
  "avoidIngredients": ["香菜"],
  "preferredCuisines": [1, 2, 3],
  "maxDistanceMeters": 3000
}
```

**响应**：更新后的完整画像。

**校验**

| 字段 | 规则 | 失败 code |
|---|---|---|
| `spicyLevel` | 0–5 | `2001` |
| `budgetMinCents` | ≥ 0 且 ≤ `budgetMaxCents` | `2001` |
| `avoidIngredients` | ≤ 20 项，每项 ≤ 10 字 | `2001` |
| `preferredCuisines` | 值必须在 Cuisine 枚举内，≤ 17 项 | `2001` |
| `maxDistanceMeters` | 100–20000 | `2001` |

> ⚠️ **注意**：修改 `avoidIngredients` 会**立即影响**后续决策的硬过滤。前端应提示「已更新，推荐结果会相应调整」。

### 5.3 `POST /me/preference/onboarding`

新用户 5 道偏好题的提交入口。语义上等同 `PUT /me/preference` + 置 `onboardingCompleted = true`。

```jsonc
// 请求
{
  "spicyLevel": 2,
  "budgetMinCents": 1500,
  "budgetMaxCents": 5000,
  "avoidIngredients": [],
  "preferredCuisines": [1],
  "diningMode": 1
}

// 响应
{ "code": 0, "data": { "onboardingCompleted": true } }
```

### 5.4 `GET /me/summary`

「我的」页数据卡片。

```jsonc
{
  "code": 0,
  "data": {
    "totalRecords": 137,
    "totalDishesTried": 86,
    "currentStreakDays": 12,
    "longestStreakDays": 21,
    "thisWeekRecords": 12,
    "thisWeekNewDishes": 4,
    "favoriteCuisine": { "value": 1, "label": "川菜", "count": 32 },
    "averageRating": 4.2
  }
}
```

---

## 6. 决策接口（核心）

### 6.1 `POST /decisions/suggest`

**这是整个产品的核心接口。**

**请求**

```jsonc
{
  "mealType": 2,                       // 可空，空则按服务端时间推断
  "diningMode": 2,                     // 0随便 1外卖 2堂食 3自己做
  "partySize": 1,
  "budgetMinCents": 2000,              // 可空，空则用画像值
  "budgetMaxCents": 5000,
  "location": {                        // 可空
    "latitude": 31.2304,
    "longitude": 121.4737
  },
  "weather": "rain",                   // 可空：clear/rain/hot/cold/snow
  "moodTags": ["暖胃的"],               // 可空，最多 3 个
  "excludeDishIds": [],                // 「换一批」时传上一批的 Top N
  "exploreJitter": 0                   // 0 = 确定性；「换一批」传 3
}
```

**响应** `200`

```jsonc
{
  "code": 0,
  "message": "ok",
  "traceId": "0HN7GQ2K9V3LM",
  "data": {
    "sessionId": "b7e3…",
    "engineVersion": "1.0.0",
    "elapsedMs": 7,
    "candidateCount": 286,             // 硬过滤后候选数
    "filteredOutCount": 34,            // 被硬过滤掉的数量
    "relaxedLevel": 0,                 // 0=未降级；>0 表示放宽了条件（前端可提示）
    "wheelPicks": ["3f2a…", "8b1c…", "…"],   // 转盘用的 8 个 dishId，与 ranked 顺序一致

    "ranked": [
      {
        "rank": 1,
        "dishId": "3f2a…",
        "name": "番茄牛腩",
        "score": 84.6,
        "breakdown": {
          "taste": 0.97, "freshness": 0.70, "affinity": 0.79,
          "timeSlot": 1.00, "budget": 1.00, "context": 0.78, "exploration": 0.30
        },
        "penalties": [],
        "reasons": ["微辣，正合你口味", "午餐吃这个正合适"],
        "dish": {
          "id": "3f2a…",
          "name": "番茄牛腩",
          "cuisine": 1,
          "cuisineLabel": "川菜",
          "category": 2,
          "categoryLabel": "荤菜",
          "spicyLevel": 1,
          "spicyLabel": "微辣",
          "priceMinCents": 3200,
          "priceMaxCents": 4800,
          "calories": 420,
          "imageUrl": "https://…",
          "tags": ["下饭", "暖胃", "炖菜"],
          "hasRecipe": true,
          "cookMinutes": 90,
          "distanceMeters": 800,
          "description": "酸甜开胃，牛腩软烂，汤汁拌饭一绝"
        }
      },
      {
        "rank": 2,
        "dishId": "8b1c…",
        "name": "清蒸鲈鱼",
        "score": 81.2,
        "breakdown": { "…": 0 },
        "penalties": [
          { "code": "MEH", "multiplier": 0.70, "description": "上次评分一般" }
        ],
        "reasons": ["你爱吃粤菜"],
        "dish": { "…": null }
      }
    ]
  }
}
```

**字段说明**

| 字段 | 说明 |
|---|---|
| `wheelPicks` | 转盘候选的 `dishId` 列表（默认 Top 8），**顺序与 `ranked` 前 8 项一致**，前端据此绘制扇区 |
| `ranked` | 默认返回 Top 20。`rank` 从 1 开始 |
| `relaxedLevel` | 降级层级（见决策引擎文档 §10）。`> 0` 时前端应提示「已为你放宽条件」 |
| `breakdown` | 各维度**归一化分**（0–1）。前端用 `/meta/engine` 的权重换算贡献度 |
| `penalties` | 已应用的乘性惩罚，用于「为什么这个分数不高」的解释 |
| `reasons` | 1–2 条人话理由，**保证非空** |

**错误**

| code | 场景 | 前端处理 |
|---|---|---|
| `4001` | 硬过滤后候选为空 | 提示「没有符合条件的菜品，试试放宽预算或忌口」+ 提供「重置条件」按钮 |
| `2001` | `moodTags` 超过 3 个 / `partySize` ≤ 0 | 提示参数错误 |

**性能预期**

| 指标 | 目标 |
|---|---|
| 服务端 P95 | < 50ms |
| 端到端（含网络） | < 300ms |

> 💡 前端应在**进入决策页时预取**（可用默认条件先调一次），用户点 CTA 时直接展示，感知延迟接近 0。

---

### 6.2 `POST /decisions/{sessionId}/choose`

记录用户最终选择。**这是权重调优的核心数据来源**，务必调用。

```jsonc
// 请求
{
  "dishId": "3f2a…",
  "source": "wheel"          // wheel（转盘）/ list（榜单）/ other
}

// 响应
{
  "code": 0,
  "data": {
    "sessionId": "b7e3…",
    "dishId": "3f2a…",
    "rank": 1,               // 用户选的菜在推荐中的排名
    "chosenAt": "2025-06-15T04:05:00Z"
  }
}
```

> 💡 **前端时机**：用户点「就吃这个」时，**并行**调用本接口与 `POST /records`。即使用户没记录，也应调用本接口（选择行为本身就是信号）。

### 6.3 `GET /decisions/history`

| 参数 | 类型 | 默认 | 说明 |
|---|---|---|---|
| `page` / `pageSize` | int | 1 / 20 | |
| `from` / `to` | date | — | 时间范围 |

```jsonc
{
  "code": 0,
  "data": {
    "items": [
      {
        "sessionId": "b7e3…",
        "mealType": 2,
        "mealTypeLabel": "午餐",
        "diningMode": 2,
        "diningModeLabel": "堂食",
        "chosenDishId": "3f2a…",
        "chosenDishName": "番茄牛腩",
        "chosenRank": 1,
        "topPickName": "番茄牛腩",
        "createdAt": "2025-06-15T04:00:00Z"
      }
    ],
    "total": 42, "page": 1, "pageSize": 20, "hasMore": true
  }
}
```

---

## 7. 菜品接口

### 7.1 `GET /dishes`

| 参数 | 类型 | 默认 | 说明 |
|---|---|---|---|
| `keyword` | string | — | 菜名/别名模糊匹配 |
| `cuisine` | int | — | 菜系筛选 |
| `category` | int | — | 分类筛选 |
| `spicyMax` | int | — | 最大辣度 |
| `mealType` | int | — | 适用餐次 |
| `hasRecipe` | bool | — | 是否有菜谱 |
| `sort` | string | `popularity` | `popularity` / `name` / `calories` |
| `page` / `pageSize` | int | 1 / 20 | |

> 💡 **搜索实现**：MVP 阶段服务端把菜品全量加载到内存做 `Contains` 匹配（含别名），比数据库 LIKE 更灵活。数据量超 1 万后再引入全文检索。

```jsonc
{
  "code": 0,
  "data": {
    "items": [
      {
        "id": "3f2a…",
        "name": "番茄牛腩",
        "cuisine": 1, "cuisineLabel": "川菜",
        "category": 2, "categoryLabel": "荤菜",
        "spicyLevel": 1, "spicyLabel": "微辣",
        "priceMinCents": 3200, "priceMaxCents": 4800,
        "calories": 420,
        "imageUrl": "https://…",
        "tags": ["下饭", "暖胃"],
        "hasRecipe": true,
        "cookMinutes": 90,
        "isBuiltin": true,
        "popularity": 88
      }
    ],
    "total": 320, "page": 1, "pageSize": 20, "hasMore": true
  }
}
```

### 7.2 `GET /dishes/{id}`

详情，含食材、菜谱、个人历史。

```jsonc
{
  "code": 0,
  "data": {
    "id": "3f2a…",
    "name": "番茄牛腩",
    "aliases": ["西红柿炖牛腩"],
    "cuisine": 1, "cuisineLabel": "川菜",
    "category": 2, "categoryLabel": "荤菜",
    "spicyLevel": 1, "spicyLabel": "微辣",
    "priceMinCents": 3200, "priceMaxCents": 4800,
    "calories": 420,
    "imageUrl": "https://…",
    "description": "酸甜开胃，牛腩软烂，汤汁拌饭一绝",
    "tags": ["下饭", "暖胃", "炖菜"],
    "mealTimes": 6,
    "mealTimeLabels": ["午餐", "晚餐"],
    "ingredients": [
      { "name": "牛腩", "amount": "500g", "isCommonAllergen": false },
      { "name": "番茄", "amount": "3个",  "isCommonAllergen": false }
    ],
    "isBuiltin": true,

    "recipe": {
      "servings": 2,
      "cookMinutes": 90,
      "difficulty": 2,
      "difficultyLabel": "中等",
      "steps": [
        { "order": 1, "text": "牛腩切块冷水下锅焯水 3 分钟", "durationMinutes": 3 }
      ],
      "tips": "一定要加热水，冷水会让牛腩肉质变紧"
    },

    "myHistory": {
      "eatCount": 4,
      "averageRating": 4.5,
      "lastEatenAt": "2025-06-03T11:30:00Z",
      "wouldEatAgain": true
    },

    "conflictWarning": {                 // 与用户忌口冲突时返回
      "hasConflict": false,
      "conflictIngredients": []
    }
  }
}
```

> 💡 `conflictWarning`：菜品详情**不做硬过滤**（用户可能只是想看看），但要明确提示冲突。

### 7.3 `POST /dishes`

创建用户自定义菜品。

```jsonc
// 请求
{
  "name": "外婆红烧肉",
  "cuisine": 5,
  "category": 2,
  "spicyLevel": 0,
  "priceMinCents": 3000,
  "priceMaxCents": 4000,
  "calories": 520,
  "ingredients": [
    { "name": "五花肉", "amount": "500g", "isCommonAllergen": false }
  ],
  "tags": ["下饭", "家常"],
  "mealTimes": 6,
  "description": "外婆的秘方"
}

// 响应
{ "code": 0, "data": { "id": "c4d9…" } }
```

**校验**：`name` 必填 1–60 字；同名（含别名）时返回 `2005`。

> ⚠️ **内容安全**：自定义菜名需过敏感词过滤（个人主体需自行承担内容责任）。

### 7.4 `PUT /dishes/{id}` / `DELETE /dishes/{id}`

**仅允许操作 `isBuiltin == false` 且 `ownerUserId == 当前用户`** 的菜品，否则 `1003`。
内置菜品返回 `1003`。

---

## 8. 记录接口

### 8.1 `POST /records`

**必须携带 `Idempotency-Key`**（防双击重复）。

```jsonc
// 请求
{
  "dishId": "3f2a…",              // 可空（纯手动输入时）
  "dishName": "番茄牛腩",          // dishId 为空时必填
  "mealType": 2,
  "diningMode": 2,
  "eatenAt": "2025-06-15T04:00:00Z",
  "servings": 1.0,
  "rating": 5,                    // 可空，稍后补
  "wouldEatAgain": true,          // 可空
  "photoUrl": "https://…",        // 可空
  "note": "汤汁拌饭绝了",          // 可空，≤ 200 字
  "source": 3,                    // 1手动 2AI识别 3决策 4菜品库
  "decisionSessionId": "b7e3…"    // 可空
}

// 响应
{
  "code": 0,
  "data": {
    "id": "e5f6…",
    "dishName": "番茄牛腩",
    "mealType": 2, "mealTypeLabel": "午餐",
    "eatenAt": "2025-06-15T04:00:00Z",
    "calories": 420,
    "rating": 5,
    "createdAt": "2025-06-15T04:06:00Z"
  }
}
```

**服务端行为**

1. 校验参数；`dishId` 存在时校验归属与可见性。
2. **写入 `dish_snapshot`**（菜系/分类/辣度/热量/价格/标签）。
3. 若 `dishId` 为空但 `dishName` 能在菜品库匹配到 → 自动关联（提升后续统计质量）。
4. **同一事务内**更新 `user_dish_stats` 与 `user_cuisine_stats`。
5. 返回记录。

**错误**

| code | 场景 |
|---|---|
| `2003` | 菜品含用户忌口食材（**仍允许记录**，但需前端二次确认后带 `force=true` 重试） |
| `2002` | `dishId` 不存在 |
| `2001` | `eatenAt` 超过未来 1 小时 / 早于 1 年前 |

> 💡 **`2003` 的设计**：记录是「我吃了什么」的客观事实，不该因为忌口被阻止。服务端返回 `2003` 是为了让前端**确认一次**（「这道菜含花生，确定要记录吗？」），用户确认后带 `"force": true` 重试即可。

### 8.2 `GET /records`

| 参数 | 类型 | 默认 | 说明 |
|---|---|---|---|
| `page` / `pageSize` | int | 1 / 20 | |
| `from` / `to` | date | — | 按 `eatenAt` 过滤 |
| `mealType` | int | — | |
| `dishId` | uuid | — | 某道菜的历史 |
| `hasRating` | bool | — | `false` = 只看未评分 |

**响应**（按天分组由前端做，服务端只保证按 `eatenAt` 倒序）

```jsonc
{
  "code": 0,
  "data": {
    "items": [
      {
        "id": "e5f6…",
        "dishId": "3f2a…",
        "dishName": "番茄牛腩",
        "dishSnapshot": {
          "cuisine": 1, "cuisineLabel": "川菜",
          "category": 2, "spicyLevel": 1, "caloriesPerServing": 420, "priceCents": 3800
        },
        "mealType": 2, "mealTypeLabel": "午餐",
        "diningMode": 2, "diningModeLabel": "堂食",
        "eatenAt": "2025-06-15T04:00:00Z",
        "servings": 1.0,
        "calories": 420,
        "rating": 5,
        "wouldEatAgain": true,
        "photoUrl": "https://…",
        "note": "汤汁拌饭绝了",
        "source": 3, "sourceLabel": "决策推荐"
      }
    ],
    "total": 137, "page": 1, "pageSize": 20, "hasMore": true
  }
}
```

### 8.3 `GET /records/{id}`

同列表项结构，额外返回 `decisionSession`（若来源是决策）。

### 8.4 `PUT /records/{id}`

```jsonc
// 请求（字段可选）
{
  "mealType": 3,
  "eatenAt": "2025-06-15T10:00:00Z",
  "servings": 1.5,
  "rating": 4,
  "wouldEatAgain": true,
  "note": "改了备注",
  "photoUrl": "https://…"
}
```

**服务端行为**：若 `rating` 或 `wouldEatAgain` 变化 → **重算** `user_dish_stats`（先减旧值再加新值）。

### 8.5 `DELETE /records/{id}`

软删除（`is_deleted = true`），**同一事务内回滚统计**。

### 8.6 `POST /records/{id}/rating`

快速评分，用于「待评分提醒」的一键五星。

```jsonc
// 请求
{ "rating": 5, "wouldEatAgain": true }

// 响应
{ "code": 0, "data": { "id": "e5f6…", "rating": 5, "wouldEatAgain": true } }
```

### 8.7 `GET /records/pending-rating`

返回最近 7 天内未评分的记录，供首页/记录页提醒。

| 参数 | 类型 | 默认 |
|---|---|---|
| `limit` | int | 3 |

```jsonc
{
  "code": 0,
  "data": {
    "items": [
      {
        "id": "e5f6…",
        "dishName": "麻辣香锅",
        "photoUrl": "https://…",
        "eatenAt": "2025-06-14T10:30:00Z",
        "daysAgo": 1
      }
    ]
  }
}
```

### 8.8 `GET /records/stats`

| 参数 | 类型 | 默认 | 说明 |
|---|---|---|---|
| `range` | string | `week` | `week` / `month` / `year` / `all` |
| `from` / `to` | date | — | 自定义范围（优先于 `range`） |

```jsonc
{
  "code": 0,
  "data": {
    "range": { "from": "2025-06-09", "to": "2025-06-15" },
    "totalRecords": 12,
    "totalCalories": 8420,
    "averageCaloriesPerMeal": 702,
    "averageRating": 4.2,
    "recordedDays": 5,
    "newDishesTried": 4,
    "mealTypeDistribution": [
      { "value": 1, "label": "早餐", "count": 2 },
      { "value": 2, "label": "午餐", "count": 5 },
      { "value": 3, "label": "晚餐", "count": 4 },
      { "value": 4, "label": "夜宵", "count": 1 }
    ],
    "cuisineDistribution": [
      { "value": 1, "label": "川菜", "count": 5 },
      { "value": 2, "label": "粤菜", "count": 3 }
    ],
    "diningModeDistribution": [
      { "value": 1, "label": "外卖", "count": 6 },
      { "value": 2, "label": "堂食", "count": 4 },
      { "value": 3, "label": "自己做", "count": 2 }
    ],
    "dailyCalories": [
      { "date": "2025-06-09", "calories": 1800 },
      { "date": "2025-06-10", "calories": 2100 }
    ]
  }
}
```

---

## 9. AI 接口

### 9.1 `POST /uploads/image`

`multipart/form-data` 上传。

| 字段 | 类型 | 说明 |
|---|---|---|
| `file` | File | 图片文件 |
| `scene` | string | `dish`（菜品）/ `avatar`（头像） |

**限制**：≤ 5MB；`jpg/jpeg/png/webp`；建议前端先压缩到长边 ≤ 1280px。

```jsonc
// 响应
{
  "code": 0,
  "data": {
    "url": "https://cdn.example.com/u/9c1f/202506/3f2a.jpg",
    "width": 1280,
    "height": 960,
    "sizeBytes": 384210
  }
}
```

**错误**：`3004`（格式不支持）、`3005`（过大）。

### 9.2 `POST /ai/recognize`

**识别但不入库**。用户确认后才写记录（见 ADR-005）。

```jsonc
// 请求
{ "imageUrl": "https://cdn.example.com/u/9c1f/202506/3f2a.jpg" }

// 响应
{
  "code": 0,
  "data": {
    "logId": "a1b2…",
    "modelName": "qwen-vl-max",
    "latencyMs": 2840,
    "overallConfidence": 0.78,
    "scene": "dine_in",
    "sceneLabel": "堂食",
    "items": [
      {
        "index": 0,
        "name": "番茄牛腩",
        "confidence": 0.92,
        "needsReview": false,
        "estimatedCalories": 420,
        "ingredients": ["牛腩", "番茄", "洋葱"],
        "portion": 1.0,
        "matchedDishId": "3f2a…",
        "matchedDishName": "番茄牛腩",
        "imageUrl": "https://…/crop-0.jpg"
      },
      {
        "index": 1,
        "name": "米饭",
        "confidence": 0.61,
        "needsReview": true,          // 置信度 < 0.70 → 前端标黄置顶
        "estimatedCalories": 230,
        "ingredients": ["大米"],
        "portion": 1.0,
        "matchedDishId": null,
        "matchedDishName": null,
        "imageUrl": "https://…/crop-1.jpg"
      }
    ]
  }
}
```

**字段说明**

| 字段 | 说明 |
|---|---|
| `logId` | 识别日志 ID，确认时必传 |
| `needsReview` | `confidence < 0.70`，前端应**标黄并置顶**引导修正 |
| `matchedDishId` | 匹配到菜品库的 ID；为 null 表示是新菜，确认时可创建 |
| `imageUrl` | 该菜品的裁剪图（可选，用于确认页缩略图） |

**错误与降级**

| code | 场景 | 前端处理 |
|---|---|---|
| `3001` | 模型调用失败 | 提示并降级为手动输入 |
| `3002` | 超时（> 15s） | 提示重试 |
| `3003` | 配额超限 | 提示「今日识别次数已用完」 |
| `3006` | 返回解析失败 | 降级为手动输入 |

> ⚠️ **降级是硬要求**：AI 是增强而非依赖。任何 AI 失败都必须能走手动输入路径。

### 9.3 `POST /ai/recognize/{logId}/confirm`

用户确认/修正后入库。**必须携带 `Idempotency-Key`**。

```jsonc
// 请求
{
  "mealType": 2,
  "diningMode": 2,
  "eatenAt": "2025-06-15T04:00:00Z",
  "items": [
    {
      "index": 0,
      "dishId": "3f2a…",              // 匹配到库则传 ID
      "name": "番茄牛腩",
      "portion": 1.0,
      "estimatedCalories": 420,
      "isCorrected": false            // 用户是否改过
    },
    {
      "index": 1,
      "dishId": null,                 // 未匹配 → 确认时创建自定义菜品
      "name": "米饭",
      "portion": 1.0,
      "estimatedCalories": 230,
      "isCorrected": true             // 用户把「白饭」改成了「米饭」
    }
  ]
}

// 响应
{
  "code": 0,
  "data": {
    "recordIds": ["e5f6…", "f7a8…"],
    "createdDishIds": ["d9c0…"],      // 新建的自定义菜品
    "correctedCount": 1
  }
}
```

**服务端行为**

1. 幂等检查。
2. 对每个 item：`dishId` 有则关联；无则**创建用户自定义菜品**（若同名已存在则复用）。
3. 批量创建 `meal_records`（`source = AiRecognized`，关联 `ai_recognition_log_id`）。
4. **写入 `corrected_result` 到 `ai_recognition_logs`**，`is_corrected = true`。
5. 更新统计表。

> 💡 第 4 步是**核心数据资产沉淀**。`corrected_result` 记录了「模型识别成什么 → 用户改成什么」，是后续优化 Prompt、构建 few-shot 库、微调模型的黄金数据。

### 9.4 `POST /ai/recipe`

「自己做」模式下生成菜谱。结果**缓存到 `recipes` 表**，避免重复调用。

```jsonc
// 请求
{ "dishName": "番茄牛腩", "dishId": "3f2a…", "servings": 2 }

// 响应
{
  "code": 0,
  "data": {
    "dishId": "3f2a…",
    "dishName": "番茄牛腩",
    "servings": 2,
    "cookMinutes": 90,
    "difficulty": 2,
    "difficultyLabel": "中等",
    "steps": [
      { "order": 1, "text": "牛腩切块冷水下锅焯水 3 分钟", "durationMinutes": 3 }
    ],
    "tips": "一定要加热水",
    "fromCache": false
  }
}
```

### 9.5 `POST /ai/shopping-list`

根据选中的多道菜生成买菜清单（合并同类食材）。

```jsonc
// 请求
{ "dishIds": ["3f2a…", "8b1c…"], "servings": 2 }

// 响应
{
  "code": 0,
  "data": {
    "items": [
      {
        "name": "牛腩",
        "totalAmount": "500g",
        "category": "肉类",
        "forDishes": ["番茄牛腩"]
      },
      {
        "name": "番茄",
        "totalAmount": "5个",
        "category": "蔬菜",
        "forDishes": ["番茄牛腩", "番茄炒蛋"]
      }
    ],
    "groupedByCategory": [
      { "category": "肉类", "items": ["牛腩"] },
      { "category": "蔬菜", "items": ["番茄", "洋葱"] }
    ],
    "textSummary": "【肉类】牛腩 500g\n【蔬菜】番茄 5个、洋葱 1个"
  }
}
```

> 💡 `textSummary` 供前端「一键复制」使用，方便分享到微信。

---

## 10. 前端调用封装

### 10.1 统一请求封装

```ts
// web/src/api/request.ts
import { platform } from '@/platform'

const BASE_URL = import.meta.env.VITE_API_BASE_URL
const TIMEOUT = 15_000

interface ApiResponse<T> {
  code: number
  message: string
  data: T
  traceId: string
}

let refreshing: Promise<boolean> | null = null   // 并发续期去重

async function refreshToken(): Promise<boolean> {
  if (refreshing) return refreshing
  refreshing = (async () => {
    try {
      const rt = uni.getStorageSync('refreshToken')
      if (!rt) return false
      const res = await rawRequest<{ accessToken: string; refreshToken: string }>({
        url: '/auth/refresh', method: 'POST', data: { refreshToken: rt }, skipAuth: true,
      })
      uni.setStorageSync('accessToken', res.accessToken)
      uni.setStorageSync('refreshToken', res.refreshToken)
      return true
    } catch {
      uni.removeStorageSync('accessToken')
      uni.removeStorageSync('refreshToken')
      return false
    } finally {
      refreshing = null
    }
  })()
  return refreshing
}

export async function request<T>(options: RequestOptions): Promise<T> {
  try {
    return await rawRequest<T>(options)
  } catch (err: any) {
    // 401 / 1002 → 续期一次后重放
    if (err.httpStatus === 401 || err.code === 1002) {
      const ok = await refreshToken()
      if (ok) return rawRequest<T>(options)
      await redirectToLogin()
      throw err
    }
    throw err
  }
}

async function rawRequest<T>(options: RequestOptions): Promise<T> {
  const token = uni.getStorageSync('accessToken')
  const res = await uni.request({
    url: BASE_URL + options.url,
    method: options.method ?? 'GET',
    data: options.data,
    header: {
      'Content-Type': 'application/json',
      ...(options.skipAuth || !token ? {} : { Authorization: `Bearer ${token}` }),
      ...(options.idempotencyKey ? { 'Idempotency-Key': options.idempotencyKey } : {}),
    },
    timeout: TIMEOUT,
  })

  const body = res.data as ApiResponse<T>

  // HTTP 层错误
  if (res.statusCode === 429) throw new ApiError(3003, '请求过于频繁，请稍后再试', 429)
  if (res.statusCode >= 500) throw new ApiError(5000, '服务开小差了', res.statusCode)

  // 业务层错误
  if (body.code !== 0) throw new ApiError(body.code, body.message, res.statusCode, body.traceId)

  return body.data
}
```

### 10.2 请求重试策略

| 场景 | 策略 |
|---|---|
| 网络超时 / `5xx` | 重试 **2 次**，指数退避（300ms → 900ms） |
| `429` | **不重试**，提示用户 |
| `401` / `1002` | 续期后重放 **1 次** |
| `POST` 重试 | **必须**带相同 `Idempotency-Key` |

### 10.3 关键调用示例

```ts
// 决策：进入页面时预取
const { data: prefetch } = await api.decision.suggest({
  diningMode: 0, partySize: 1, now: new Date().toISOString(),
})

// 用户点「帮我决定」→ 用真实条件重新请求
const result = await api.decision.suggest({
  mealType: 2, diningMode: 2, partySize: 1,
  budgetMinCents: 2000, budgetMaxCents: 5000,
  weather: 'rain', moodTags: ['暖胃的'],
})

// 「换一批」→ 排除上一批 + 开启抖动
const next = await api.decision.suggest({
  ...sameConditions,
  excludeDishIds: result.ranked.map(r => r.dishId),
  exploreJitter: 3,
})

// 用户点「就吃这个」→ 并行：记录选择 + 创建记录
await Promise.all([
  api.decision.choose(result.sessionId, { dishId, source: 'wheel' }),
  api.record.create({
    dishId, dishName, mealType: 2, diningMode: 2,
    eatenAt: new Date().toISOString(), servings: 1,
    source: 3, decisionSessionId: result.sessionId,
  }, { idempotencyKey: uuidv4() }),
])
```

---

## 11. 待确认问题

| # | 问题 | 影响 |
|---|---|---|
| Q1 | `POST /decisions/suggest` 是否需要在响应中直接内嵌完整菜品信息？ | 内嵌可减少请求数，但响应体变大（Top20 约 20KB）；不内嵌则前端需二次请求 |
| Q2 | 记录接口是否要支持**批量创建**（一次记多个菜）？ | AI 识别一次出多道菜，目前用 `confirm` 接口批量处理；手动记录暂为单条 |
| Q3 | 是否需要「分享卡片生成」的服务端接口？ | 小程序端 Canvas 可自行绘制，但服务端生成更可控且能做小程序码 |
| Q4 | 决策历史是否需要暴露给用户看？ | 涉及「你上次选了但没吃」这类尴尬数据，建议 v1 不暴露 |
| Q5 | 是否需要 WebSocket / 订阅消息推送「该吃饭了」？ | v1 不做，v2 用订阅消息（仅微信支持） |
