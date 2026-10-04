# 美食伴侣 🍜

> 一个帮你不假思索决定「今天吃什么」，并顺手记住你吃了什么的小程序。

**当前进度：M5 部署就绪** —— 三大闭环 + 三端登录 + Docker 一键部署/升级全部完成，具备上线条件。

---

## 这是什么

每天中午 11:30 和晚上 6:00，「今天吃什么」是全人类最高频的决策困难。

大众点评、美团是**搜索**逻辑 —— 你得先知道自己想吃什么。

美食伴侣做的是**替你做决定**：

```
选条件 → 加权打分 → 转盘 → 就吃这个 → 记录 → 画像更准 → 下次更准
```

## 核心差异

| 常规推荐 | 美食伴侣 |
|---|---|
| 黑盒推荐，不知道为什么 | **每个结果都给出人话理由**（「你最近 5 天吃了 3 次川菜，今天换个花样」） |
| 永远推荐你最爱的那道 | 新鲜度权重(0.20) > 偏好强度(0.15)，**主动推你换口味** |
| 记录要填表单 | 「就吃这个」一键完成，或**拍照识别** |
| 苛责式卡路里管理 | 只呈现，不批判 |

## 产品红线（个人主体约束）

> ⚠️ 以下为**不可协商**的约束，所有设计都必须遵守。

- ❌ 不做外卖下单 / 跳转外卖平台
- ❌ 不做团购券 / 优惠券 / 返佣
- ❌ 不做商家入驻 / 公开点评体系
- ❌ 不做支付

**产品定位严格保持在「工具」范畴**：帮你决定、帮你记录、帮你了解自己。不碰交易，就不碰资质。

---

## 快速开始

### 方式一：Docker 一键部署（推荐）

```bash
git clone https://github.com/XCool-603/foodmate.git && cd foodmate
./deploy/deploy.sh
```

脚本会自动生成随机密钥、构建镜像、启动 API + PostgreSQL，并等到健康检查通过才宣告成功。

Windows 用 `.\deploy\deploy.ps1`。

> ⚠️ **Docker 配置尚未经实际构建验证** —— 开发机未安装 Docker。
> 应用本身（314 个测试、五条端到端链路）已验证，但 `docker build` 未跑过。
> 详见 [部署手册的验证状态说明](docs/DEPLOY.md#️-关于验证状态)。

**一键升级**：

```bash
./deploy/upgrade.sh          # 备份数据库 → 标记旧镜像 → 构建 → 替换 → 健康检查 → 失败自动回滚
./deploy/upgrade.sh --pull   # 先 git pull 再升级
./deploy/upgrade.sh --rollback
```

完整说明见 [部署手册](docs/DEPLOY.md)。

### 方式二：本地开发

#### 环境要求

```
.NET SDK  10.0.400    ✅ 已验证
Node      v26.8.1     ✅ 已验证（Vite 5.2.8 在 Node 26 上构建正常）
npm       11.19.0     ✅ 已验证
```

#### 1. 启动后端

```bash
dotnet run --project src/FoodMate.Api
```

首次启动会自动建表（SQLite）并导入 20 道内置菜品。

服务地址：**http://localhost:5199**

> ⚠️ **端口说明**：本机 `5080` 已被其他项目占用，因此默认端口设为 `5199`
> （见 `src/FoodMate.Api/Properties/launchSettings.json`）。换端口时记得同步改
> `web/.env.development` 里的 `VITE_API_BASE_URL`。

验证：

```bash
curl http://localhost:5199/health          # 存活探针（裸结构）
curl http://localhost:5199/health/ready    # 就绪探针，含菜品数量
```

试一下决策引擎：

```bash
# 1. 登录拿令牌（platform=4 是 H5 游客，本地开发用；生产环境应关闭）
TOKEN=$(curl -s -X POST http://localhost:5199/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"platform":4,"code":"my-device-001"}' | jq -r .data.accessToken)

# 2. 带着令牌调业务接口
curl -X POST http://localhost:5199/api/v1/decisions/suggest \
  -H "Content-Type: application/json" -H "Authorization: Bearer $TOKEN" \
  -d '{"mealType":2,"diningMode":0,"partySize":1,"moodTags":["暖胃的"]}'
```

> 💡 开发环境（`ASPNETCORE_ENVIRONMENT=Development`）额外支持 `X-Device-Id` 请求头直接访问，
> 方便用 curl 快速联调。它走的是与正式登录**完全相同**的游客身份提供者，
> 不存在绕过鉴权的后门；生产环境会自动拒绝。

记录与画像：

```bash
curl http://localhost:5199/api/v1/records/stats -H "Authorization: Bearer $TOKEN"
curl http://localhost:5199/api/v1/me/summary -H "Authorization: Bearer $TOKEN"
curl http://localhost:5199/api/v1/me/preference/suggestion -H "Authorization: Bearer $TOKEN"
```

AI 拍照识别（默认走**本地桩模型**，无需任何密钥）：

```bash
# 1. 上传图片
curl -X POST http://localhost:5199/api/v1/uploads/image \
  -H "Authorization: Bearer $TOKEN" -F "file=@dish.jpg"

# 2. 识别（只识别不入库）
curl -X POST http://localhost:5199/api/v1/ai/recognize \
  -H "Content-Type: application/json" -H "Authorization: Bearer $TOKEN" \
  -d '{"imageUrl":"/uploads/<userId>/<yyyyMM>/<file>.jpg"}'

# 3. 用户确认（可修正菜名）后入库
curl -X POST http://localhost:5199/api/v1/ai/recognize/<logId>/confirm \
  -H "Content-Type: application/json" -H "Authorization: Bearer $TOKEN" \
  -d '{"mealType":2,"diningMode":2,"items":[{"index":0,"name":"番茄牛腩","portion":1}]}'
```

#### 2. 启动前端

```bash
cd web
npm install

npm run dev:h5            # 浏览器预览：http://localhost:5173
npm run dev:mp-weixin     # 微信开发者工具导入 web/dist/dev/mp-weixin
npm run dev:mp-alipay
npm run dev:mp-toutiao
```

> 💡 微信开发者工具需在「详情 → 本地设置」勾选 **不校验合法域名**，否则访问不了 `localhost`。

#### 3. 跑测试

```bash
dotnet test
```

### 菜品图片怎么来的

**当前所有菜品都没有真实照片**，前端渲染的是**程序化生成的「霓虹卡面」**：
按菜系取色（川菜品红 / 粤菜青绿 / 日料靛紫…）、按分类给图标（主食 🍚 / 荤菜 🍖 / 汤羹 🍲…）、
按辣度调光晕。

为什么这么做：

- 20 道菜需要 20 张图，图库有版权问题，也没法凭空生成照片
- 赛博朋克风格里，**风格化图形本来就比照片更贴主题**（赛博朋克 2077 的物品卡也是图形）
- 程序化生成零素材、离线可用，且配色与菜系语义相关

**想换成真实照片**：

```bash
# 1. 把图片放进这里（文件名随意，建议用拼音）
src/FoodMate.Api/wwwroot/images/dishes/tomato-beef.jpg

# 2. 在种子数据里填地址
#    src/FoodMate.Infrastructure/Data/Seed/dishes.seed.json
{
  "name": "番茄牛腩",
  "imageUrl": "/images/dishes/tomato-beef.jpg",
  ...
}

# 3. 重启后端（开发库会重建并重新导入种子）
dotnet run --project src/FoodMate.Api
```

有 `imageUrl` 就显示图片，没有就回落到程序化卡面——**不会出现空占位**。

用户自己拍的照片（饮食记录里的 `photoUrl`）优先级最高，记录列表会优先显示它。

---

## 目录结构

```
美食伴侣/
├─ deploy/                        一键部署与升级
│  ├─ deploy.sh / deploy.ps1        生成密钥 → 构建 → 启动 → 等健康检查
│  └─ upgrade.sh / upgrade.ps1      备份 → 标记镜像 → 构建 → 替换 → 失败自动回滚
├─ Dockerfile                     多阶段构建（非 root 运行 + 健康检查）
├─ docker-compose.yml             API + PostgreSQL 编排
├─ .env.example                   环境变量模板
│
├─ docs/                          设计文档（5 份）
│  ├─ DESIGN.md                     产品定义、架构、页面交互、多端适配、里程碑、风险
│  ├─ DECISION-ENGINE.md            七维打分公式、权重、惩罚、冷启动、完整算例
│  ├─ DATABASE.md                   11 张表、枚举、DDL、索引、种子数据、容量规划
│  ├─ API.md                        端点契约、统一响应、错误码、限流、前端封装
│  └─ DEPLOY.md                     部署手册（一键部署/升级/回滚/排障/生产清单）
│
├─ src/
│  ├─ FoodMate.Api/                 ASP.NET Core 10 Minimal API
│  │  ├─ Endpoints/                   Health / Auth / Meta / Dish / Decision / Record / Profile / AI / Upload
│  │  ├─ Filters/                     ApiResponseFilter（统一包装）
│  │  │                               GlobalExceptionHandler（异常 → 错误码）
│  │  └─ AppInfo.cs                   版本号解析
│  ├─ FoodMate.Core/                领域模型（零外部依赖）
│  │  ├─ Entities/                    11 个实体 + 值对象
│  │  ├─ Enums/                       10 个枚举
│  │  ├─ Decision/                    ★ 决策引擎（纯函数，不碰数据库）
│  │  │  ├─ DecisionEngine.cs          主流程：过滤 → 打分 → 惩罚 → 排序 → 理由
│  │  │  ├─ HardFilter.cs              硬过滤 + 忌口同义词展开
│  │  │  ├─ PenaltyEvaluator.cs        乘性惩罚
│  │  │  ├─ ReasonGenerator.cs         人话理由生成（含信息量系数）
│  │  │  ├─ EngineOptions.cs           全部可调参数
│  │  │  └─ Scoring/                   7 个 Scorer + IScorer
│  │  ├─ Profile/                     ★ 画像学习（纯函数）
│  │  │  ├─ PreferenceUpdater.cs       从记录反推辣度/预算/菜系/就餐方式
│  │  │  └─ StreakCalculator.cs        连续打卡天数
│  │  ├─ Ai/                          ★ AI 抽象与纯逻辑
│  │  │  ├─ IVisionModelClient.cs      视觉识别接口 + 结果类型
│  │  │  ├─ ITextModelClient.cs        文本生成接口（菜谱/清单）
│  │  │  ├─ DishMatcher.cs             菜名模糊匹配（含字符级兜底）
│  │  │  └─ AiOptions.cs               模型配置与配额
│  │  ├─ Auth/                        ★ 认证抽象
│  │  │  └─ AuthOptions.cs             JWT 与三端凭据配置 + 校验
│  │  ├─ Exceptions/                  ApiErrorCode / BusinessException
│  │  ├─ Abstractions/                IClock / IIdentityProvider / IImageStorage
│  │  ├─ ApiResponse.cs              统一响应外层
│  │  ├─ PagedResult.cs              分页结果
│  │  ├─ EnumLabels.cs               枚举中文标签（集中定义）
│  │  ├─ MealTimes.cs                餐次推断规则
│  │  └─ FoodVocabulary.cs           快捷标签 / 常见过敏原
│  ├─ FoodMate.Infrastructure/
│  │  ├─ Data/
│  │  │  ├─ FoodMateDbContext.cs      含 SQLite DateTimeOffset 兼容处理
│  │  │  ├─ Configurations/           EF 实体配置（5 个文件）
│  │  │  ├─ Migrations/               EF 迁移（生产 PostgreSQL 用）
│  │  │  ├─ DesignTimeDbContextFactory.cs  设计时工厂（生成迁移用）
│  │  │  ├─ Json/                     JSON 列的值转换器与比较器（不转义中文）
│  │  │  ├─ SnakeCaseConvention.cs    表名/列名自动转 snake_case
│  │  │  ├─ DbInitializer.cs          迁移/建表 + 种子导入 + 开发期重建开关
│  │  │  └─ Seed/dishes.seed.json     内置菜品（内嵌资源）
│  │  ├─ Decisions/DecisionService.cs 组装引擎输入 + 落库
│  │  ├─ Records/                     ★ 记录与统计
│  │  │  ├─ RecordService.cs           CRUD + 统计表重算 + 忌口冲突检查
│  │  │  └─ RecordStatsService.cs      报表 + 「我的」页卡片
│  │  ├─ Profile/PreferenceService.cs 画像读写 + 引导 + 学习建议
│  │  ├─ Ai/                          ★ AI 实现
│  │  │  ├─ StubModelClients.cs        本地桩（无外部依赖，确定性输出）
│  │  │  ├─ OpenAiCompatibleClientBase.cs  /chat/completions 公共部分
│  │  │  ├─ OpenAiCompatibleVisionClient.cs
│  │  │  ├─ OpenAiCompatibleTextClient.cs
│  │  │  └─ AiRecognitionService.cs    识别 + 确认流 + 修正数据沉淀
│  │  ├─ Auth/                        ★ 认证实现
│  │  │  ├─ JwtSigningKey.cs           签发与校验共用的密钥单例
│  │  │  ├─ JwtIssuer.cs               HS256 签发 + 校验参数
│  │  │  └─ AuthService.cs             登录 / 续期轮换 / 重放检测 / 登出
│  │  ├─ Identity/                    ★ 三端身份
│  │  │  ├─ WeChatAndDouyinProviders.cs  微信 code2session + 抖音 jscode2session
│  │  │  ├─ AlipayIdentityProvider.cs    支付宝（含 RSA2 签名）
│  │  │  ├─ IdentityProviderRegistry.cs  按平台路由 + 游客提供者
│  │  │  └─ GuestUserService.cs          开发环境设备 ID 降级
│  │  ├─ Storage/LocalImageStorage.cs  本地磁盘图片存储（防目录穿越）
│  │  └─ DependencyInjection.cs       Provider 切换 + 应用服务装配
│  └─ FoodMate.Contracts/             DTO（Auth / Dishes / Meta / Decisions / Records / Profile / Ai）
│
├─ tests/
│  ├─ FoodMate.Core.Tests/           184 个引擎、画像与匹配单测
│  └─ FoodMate.Infrastructure.Tests/  130 个集成测试（SQLite 真实 Provider）
└─ web/                             uni-app（Vue 3 + TS + Vite + Pinia）
   └─ src/
      ├─ platform/                  ★ 多端适配层（全项目唯一有 #ifdef 的地方）
      ├─ api/                       请求封装（JWT 注入 / 401 自动续期 / 幂等键 / 重试）
      ├─ stores/                    Pinia（meta / decision / record / ai / auth）
      ├─ components/DishWheel.vue   Canvas 转盘（缓出动画 + 震动反馈）
      ├─ pages/
      │  ├─ decide/index.vue          决策条件页
      │  ├─ decide/result.vue         结果页（转盘 + 榜单 + 打分明细 + 一键记录）
      │  ├─ records/index.vue         记录列表（周统计 + 待评分提醒 + 按天分组）
      │  ├─ records/edit.vue          记录编辑（菜品搜索 / 时间 / 份量 / 评分 / 忌口确认）
      │  ├─ ai/recognize.vue          拍照识别（含桩模型提示与手动记录兜底）
      │  ├─ ai/confirm.vue            确认页（低置信度置顶 + 冲突标红 + 改菜名/份量）
      │  ├─ mine/index.vue            我的（账号 + 数据卡片 + 连接诊断）
      │  └─ mine/preference.vue       口味画像（含学习建议一键应用）
      └─ utils/                     format / device
```

---

## 已验证的事实

| 项目 | 结果 |
|---|---|
| 解决方案构建 | 6 个项目，**0 警告 0 错误** |
| 单元测试 | **184 个**（决策引擎、硬过滤、惩罚、理由生成、画像学习、连续打卡、菜名匹配） |
| 集成测试 | **130 个**（SQLite 真实 Provider：EF 查询翻译、时间序正确性、决策/记录/画像/AI/**认证** 五条全链路） |
| 前端类型检查 | `vue-tsc --noEmit` **通过** |
| 数据库结构 | 11 张表，snake_case 列名，检查约束与索引均按设计生成 |
| EF 迁移 | 已生成 `InitialCreate`；PostgreSQL DDL 实测产出 `uuid` / `timestamp with time zone` / 23 索引 + 3 唯一索引 |
| 种子数据 | 20 道菜（含 7 份完整菜谱），启动自动导入，幂等 |
| 决策接口 | 冷启动推荐、时段/就餐方式约束、排除+抖动、选择回传，全部端到端跑通 |
| 记录闭环 | 创建/更新/评分/删除，**统计表自动重算**；实测 14 顿记录后画像学习给出「辣度 3、¥23–40、川菜」并成功应用 |
| AI 识别闭环 | 实测：上传 → 识别（3 道菜，含 1 道需复核）→ 修正 1 条 → 确认入库 3 条记录 + 新建 1 道自定义菜品；修正数据正确沉淀 |
| 认证 | 实测：无令牌 401 统一格式 → 登录发 JWT → 令牌访问受保护端点 → 续期轮换 → **重放检测撤销整条链** → 登出失效 |
| 忌口硬过滤 | 单测 + 集成测试双重覆盖；识别确认时**批量预检**，冲突则一条记录都不写 |
| 多端条件编译 | 微信产物 `platform/` 下**只有 `wechat.js`** |
| 前端构建 | H5 与 mp-weixin 均 `Build complete`，8 个页面全部进入产物 |

### 实施期间发现并处理的问题

**M0：**

1. **端口 5080 被占用** —— 本机已有其他项目监听，已改用 `5199`。
2. **`build:*` 走生产模式** —— 会读 `.env.production`（`https://api.example.com`）。本地联调请用 `dev:*`。
3. **适配器被 tree-shaking** —— 尚未调用的适配器方法不会进产物，属预期行为。

**M1：**

4. **SQLite 不支持 `DateTimeOffset` 的比较与排序**（EF Core Provider 限制）。
   `Where(r => r.EatenAt >= since)` 会在运行时抛「could not be translated」，
   而这正是决策引擎算新鲜度的核心查询。
   已用 **Provider 条件化的值转换器**解决：SQLite 下存为定宽 ISO-8601 UTC 字符串，
   字典序即时间序；PostgreSQL 仍映射原生 `timestamptz`。

5. **理由生成被「时段」维度淹没** —— 时段对几乎每道候选菜都满分，
   于是「午餐吃这个正合适」这类**无信息量**的理由会稳定挤掉
   「上次你给它打了 4.5 分」这类真正有说服力的理由。
   已引入**信息量系数**：个人化维度加权，普适维度降权。

6. **降级链从 4 级收缩为 2 级** —— 实施时发现**只有就餐方式是硬过滤**：
   时段与距离被移到了打分环节，因为它们属于「不太合适」而非「不能吃」。

**M2：**

7. **`Servings` 用 `decimal` 会被 SQLite 存成 TEXT，让检查约束失效** ——
   `servings > 0 AND servings <= 10` 退化成字符串比较，
   于是 `'2' <= '10'` 为 **false**，正常的双份记录反而被数据库拒绝。
   已改为 `double` —— 份量是比例而非金额。

8. **画像学习「只建议，不静默覆盖」（设计取舍）** ——
   用户在手填画像里表达的是**意图**（「我在减脂，想吃清淡」），
   历史记录反映的是**行为**（「最近总被拉去吃火锅」）。
   静默覆盖会抹掉用户的意图，因此改为用户显式确认后才写入。

9. **冷启动分数压缩** —— 新用户 Top 6 挤在 83 分左右。
   M2 接入记录后 `S_fresh` / `S_affinity` / `S_exploration` 立即产生区分度，已明显缓解。
   剩余头部压缩建议等真实数据再调参（见 [DECISION-ENGINE.md §16](docs/DECISION-ENGINE.md)）。

10. **种子数据只有 20 道，「自己做」模式会触发降级** ——
    只有 7 道菜带菜谱，少于转盘容量 8，因此 `relaxedLevel = 1`。

**M3：**

11. **菜品名匹配需要字符级模糊** —— 模型输出的菜名是自由文本，
    「番茄炒鸡蛋」与菜品库里的「番茄炒蛋」既不完全相同、也没有包含关系。
    已补上字符重叠兜底，但**阈值刻意收紧**（交集 ≥ 2 字、较短一方覆盖率 ≥ 0.8、Jaccard ≥ 0.5），
    否则「番茄炒鸡蛋」会误配到「番茄牛腩」。

12. **修正数据里的中文被转义成 `\uXXXX`** ——
    数据层特意选了 `UnsafeRelaxedJsonEscaping`（为了让 JSON 列人眼可读），
    AI 服务却用了自己的一套序列化选项。而 `corrected_result` 恰恰是**要给人看的数据资产**。
    已统一复用数据层的序列化选项。这个 bug 是测试断言抓出来的。

13. **图片读取失败会变成 500** ——
    文件不存在时存储层抛 `ImageStorageException`，没被转成 `BusinessException`，
    前端会误判为服务端故障。已转换为业务错误码。这个 bug 是端到端压测时撞出来的。

14. **AI 是增强而非依赖（设计原则）** ——
    桩模型与真实模型可无缝切换；模型不可用时一律降级为手动输入。

**M4：**

15. **签发与校验必须共用同一把密钥** ——
    最初把「没配置密钥就随机生成」放在 `JwtIssuer` 里，但认证中间件是另一处构造。
    结果是签发方和校验方各拿到一把随机密钥，**token 一签发就校验不过**。
    已把密钥收敛成单例 `JwtSigningKey`。

16. **刷新令牌轮换必须做并发去重** ——
    前端多个请求同时收到 401 时会并发续期。而服务端采用令牌轮换，
    第一个成功、其余全被判为**重放**，直接把用户强制登出。
    已在前端把续期 Promise 去重。

17. **支付宝是三端里唯一需要签名的** ——
    微信和抖音都是普通 HTTP 调用，支付宝的网关要求 RSA2 签名。
    已实现并对签名做了**用对应公钥验签**的测试；兼容 PKCS#8 与 PKCS#1 两种私钥格式。

18. **统一 401 响应格式** ——
    ASP.NET 默认的 401 响应体是 ProblemDetails，与全站的 `{code,message,data,traceId}` 不一致。
    已接管 `OnChallenge`。

19. **游客登录也走标准接口（安全设计）** ——
    没有为开发环境单独开「绕过鉴权」的旁路，而是把设备 ID 实现成一种
    标准的 `IIdentityProvider`。**全站只有一套认证机制**。

**部署：**

20. **没有 EF 迁移 = 生产库是空的** ——
    开发用 SQLite 的 `EnsureCreated` 建表，而 PostgreSQL 走 `Migrate()`。
    如果一条迁移都没有，容器起来后数据库里一张表都没有。
    已补上 `InitialCreate` 迁移与设计时工厂。

21. **⚠️ PowerShell 5.1 的 `Get-Content` 会损坏 UTF-8 文件** ——
    在中文 Windows 上，`Get-Content -Raw` 默认用系统 ANSI（GBK）读取，
    再按 UTF-8 写回就会**双重编码**，中文全部变成乱码且**不可逆**。
    本项目开发期间 README.md 因此被毁过一次（313 行里 160 行受损，575 个字符丢失）。
    **改文本文件请一律用编辑器，不要用 PowerShell 的
    `Get-Content` + `Set-Content` / `WriteAllText` 组合。**
    确需脚本处理时，必须显式指定编码：
    `[System.IO.File]::ReadAllText($p, [System.Text.Encoding]::UTF8)`。

22. **`RUN chown -R` 应该写成 `COPY --chown`** ——
    镜像里始终有**两套身份**：构建期的 root 和运行期的非 root 用户。
    `COPY` 出来的文件默认属主是 `root:root`，运行期用户写不了；
    用 `RUN chown -R` 补救会**多生成一整个数据层**（镜像更大、构建更慢），
    而且顺序写错（chown 之后再 COPY）就完全失效。
    正确做法是 `COPY --chown=foodmate:foodmate`，
    镜像内**新建**的目录才单独用 `RUN install -d -o foodmate -g foodmate`。
    这也是 compose 里命名卷属主能对齐的前提 —— 命名卷首次创建时会连同
    镜像中该路径的属主一起复制，挂载点必须在镜像里就已存在且属主正确。

23. **⚠️ 改了 `web/src/uni.scss` 必须重启 dev server** ——
    uni-app 是把 `uni.scss` 的内容作为 `additionalData` **在构建配置阶段注入**
    到每个 `<style lang="scss">` 里的。也就是说它是**编译配置的一部分，不是普通模块**：

    - ✅ `npm run build:*` —— 每次都是新进程，会自动读到最新内容
    - ❌ `npm run dev:*` —— 进程启动时就固定了，**HMR 不会重新注入**

    症状很有迷惑性：构建能过，但 dev server 报
    `[plugin:vite:css] [sass] Undefined variable`，
    而且只在**新增**了变量的文件上报错（旧变量因为旧注入里还有，一切正常）。

    本项目换赛博朋克主题时就踩了这个坑：`uni.scss` 新增了 `$cy-*` 变量，
    但 dev server 注入的还是只有 `$fm-*` 的旧版本，于是 App.vue 全线报未定义。

    **规则：动过 `uni.scss` 就重启 `npm run dev:*`。**

---

## 里程碑

| 阶段 | 内容 | 状态 |
|---|---|---|
| **M0** | 骨架跑通：解决方案结构、EF Core + SQLite、统一响应、uni-app 工程、3 个 Tab、前后端打通 | ✅ **完成** |
| **M1** | 七维决策引擎 + 7 个 Scorer + 硬过滤 + 乘性惩罚 + 理由生成、`/decisions/suggest` 与 `/choose`、结果页 + 转盘组件 | ✅ **完成** |
| **M2** | 记录 CRUD + 统计表自动重算、画像读写与**学习建议**、统计报表、连续打卡、记录页/编辑页/画像页 | ✅ **完成** |
| **M3** | AI 抽象层（视觉/文本 + 本地桩）、菜名模糊匹配、识别→确认→修正沉淀闭环、图片上传、拍照页 + 确认页、菜谱生成、买菜清单 | ✅ **完成** |
| **M4** | JWT 认证（访问 + 刷新令牌轮换 + 重放检测）、三端身份提供者、`/auth/*` 端点、前端静默登录与自动续期 | ✅ **完成** |
| **M5** | Docker 一键部署 / 一键升级 / 回滚、EF 迁移、部署手册 | ✅ **完成** |
| **M5.5** | 菜品种子扩到约 300 道、接入真实视觉模型、对象存储、新用户引导页、提审材料 | 待开始 |

---

## 设计文档

| 文档 | 内容 |
|---|---|
| [总设计文档](docs/DESIGN.md) | 产品定义、架构、页面与交互、多端适配、部署、里程碑、风险 |
| [决策引擎设计](docs/DECISION-ENGINE.md) | **核心**：七维打分公式、权重取舍、乘性惩罚、冷启动、完整算例、测试用例 |
| [数据库设计](docs/DATABASE.md) | 11 张表、枚举定义、DDL、索引策略、种子数据方案、容量规划 |
| [API 接口契约](docs/API.md) | 端点、统一响应、错误码、限流、前端封装 |
| [部署手册](docs/DEPLOY.md) | 一键部署 / 升级 / 回滚、配置说明、排障、生产清单、Nginx 示例 |

---

## 待确认事项（上线前必须解决）

1. **域名备案** —— 备案周期 1–2 周，是唯一无法靠写代码压缩的外部依赖。**代码已就绪，就差这一步。**
2. **三端开放平台凭据** —— 需要注册小程序并拿到 AppId/AppSecret（支付宝还需应用私钥）。
3. **JWT 签名密钥** —— 生产环境必须配置 `Auth__Jwt__SigningKey`（≥32 字节），否则启动直接报错。
4. **接入哪个视觉模型** —— 抽象层已就绪，改配置即可切换；需要密钥与预算确认。
5. **菜品种子数据来源** —— 人工整理 / AI 批量生成 + 人工校对？目前仍是 20 道占位数据。
6. **对象存储选型** —— 当前用本地卷，多实例部署时各实例文件不共享。
7. **三端优先级** —— 建议 微信 → 抖音 → 支付宝（支付宝接入成本最高，因为要签名）。
8. **决策引擎权重是否认可** —— 建议等真实使用数据再调（见 [DECISION-ENGINE.md §14](docs/DECISION-ENGINE.md)）。

### 生产环境配置清单

```bash
ASPNETCORE_ENVIRONMENT=Production
Auth__Jwt__SigningKey=<≥32 字节的随机串>        # 必填，否则启动失败
Auth__AllowGuestLogin=false                    # 关闭游客登录
Auth__WeChat__AppId=<...>
Auth__WeChat__AppSecret=<...>
ConnectionStrings__Default=<PostgreSQL 连接串>
Database__Provider=PostgreSql
Ai__Provider=OpenAiCompatible
Ai__ApiKey=<...>
```

用 Docker 部署时这些都在 `.env` 里配置，详见 [部署手册](docs/DEPLOY.md)。

---

## 重要提示

> 📌 文档中标注「**待核实**」的政策类信息（小程序类目资质、域名备案、云托管支持情况、
> 大模型视觉能力），请以各平台**当前官方口径**为准。
> 本设计按最保守假设（个人主体、无资质）规划。
