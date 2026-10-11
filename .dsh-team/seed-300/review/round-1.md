# 第 1 轮验收报告 · 菜品种子扩到 300+

- 验收人：产品/QA 验收人（非任何交付物产出者）
- 验收时间：本轮
- 工作目录：`C:\Work\美食伴侣`
- 任务目录：`.dsh-team/seed-300`
- **判定：FAIL（1 条阻塞项 C3 + 1 条次要阻塞项）**

---

## 一、结论速览

| # | 验收标准 | 结果 | 实测值 |
|---|---|---|---|
| C1 | 菜品总数 ≥ 300 | ✅ 达成 | `dishCount = 301` |
| C2 | 甜点 ≥ 30、饮品 ≥ 25 | ✅ 达成 | 甜点 42、饮品 35 |
| C3 | ≥ 40 道带完整菜谱（steps ≥ 3） | ❌ **未达成** | recipe != null **7** 道，其中 steps ≥ 3 仅 **6** 道 |
| C4 | 种子文件可解析、无重复菜名 | ✅ 达成 | 5 个文件全部解析，301 条导入，重名 0 |
| C5 | `dotnet test` 全绿（314） | ✅ 达成 | 通过 184 + 130 = **314**，失败 0 |
| C6 | `npm run build:mp-weixin` | ✅ 达成 | `DONE Build complete.` |
| C7 | 新增菜必填字段齐全、ingredients ≥ 2 | ✅ 达成 | 145 道新增菜，缺失 0，ingredients < 2 的 0 道 |

**阻塞项：C3（菜谱作者未交付任何产物）。**

---

## 二、环境与验证方式（全部亲自动手，未采信任何自述）

### 2.1 构建

```
PS> cd C:\Work\美食伴侣; dotnet build FoodMate.slnx
已成功生成。 0 个警告 0 个错误
```

### 2.2 后端启动（先杀旧进程，保证加载新种子）

`Get-NetTCPConnection -LocalPort 5199 -State Listen` 起初返回空（无旧进程）。启动：

```
PS> dotnet run --project src/FoodMate.Api --no-build --urls http://localhost:5199
info: FoodMate.Infrastructure.Data.DbInitializer[0]
      已导入内置菜品 301 道（原有 0 道，种子总数 301 道）。
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5199
```

启动日志**无任何异常/警告级错误**；`Database:RecreateOnStartup = true`（appsettings.Development.json）确保每次重建库、不存在旧 156 道残留。

### 2.3 内嵌资源确认

```
PS> [Assembly]::LoadFrom("...\FoodMate.Infrastructure.dll").GetManifestResourceNames()
FoodMate.Infrastructure.Data.Seed.dishes.seed.dessert.json
FoodMate.Infrastructure.Data.Seed.dishes.seed.drink.json
FoodMate.Infrastructure.Data.Seed.dishes.seed.json
FoodMate.Infrastructure.Data.Seed.dishes.seed.more.json
FoodMate.Infrastructure.Data.Seed.dishes.seed.more2.json
```

`.csproj` 用通配符 `<EmbeddedResource Include="Data\Seed\dishes.seed*.json" />`，新文件自动打包。

---

## 三、逐条验收明细

### C1 ✅ 菜品总数 301 ≥ 300

```
PS> GET http://localhost:5199/health/ready
{"status":"ready","checks":{"database":"ok","dishCount":301}, ...}
```

重启后端复测仍为 `dishCount = 301`（确定性，非偶发）。

**交叉核对（证明 301 不是虚高）**：5 个种子文件原始条目合计 301，跨文件去重后**distinct name = 301，重复组 = 0** —— 即加载器一条都没合并、也没丢弃，301 与原始条目数一致。加载器日志自述「种子总数 301 道」也吻合。

### C2 ✅ 甜点 42 ≥ 30，饮品 35 ≥ 25

全量分页（`pageSize=100`，4 页，`fetched = 301`）后按 `categoryLabel` 分组：

| categoryLabel | 数量 |
|---|---|
| 荤菜 | 61 |
| 火锅烧烤 | 6 |
| 素菜 | 35 |
| 汤羹 | 28 |
| **甜点** | **42** |
| 小吃 | 27 |
| **饮品** | **35** |
| 早餐 | 28 |
| 主食 | 39 |

甜点构成：`dishes.seed.dessert.json` 40 道 + `dishes.seed.more.json` 的「冰糖葫芦」+ `dishes.seed.json` 的「银耳莲子羹」（category 6）。
饮品构成：`dishes.seed.drink.json` 35 道（此前为 0）。

### C3 ❌ 未达成（阻塞）

**证据 1 —— 交付物文件根本不存在。** `菜谱作者` 的指定交付物 `src/FoodMate.Infrastructure/Data/Seed/dishes.seed.recipes.json` 在磁盘上不存在：

```
PS> Get-ChildItem src\FoodMate.Infrastructure\Data\Seed
dishes.seed.dessert.json  15749
dishes.seed.drink.json    12608
dishes.seed.json          16164
dishes.seed.more.json     48357
dishes.seed.more2.json    27058
DishSeedLoader.cs          7040
```

```
PS> git status --short
 M src/FoodMate.Infrastructure/Data/Seed/DishSeedLoader.cs
?? .dsh-team/
?? src/FoodMate.Infrastructure/Data/Seed/dishes.seed.dessert.json
?? src/FoodMate.Infrastructure/Data/Seed/dishes.seed.drink.json
?? src/FoodMate.Infrastructure/Data/Seed/dishes.seed.more2.json
```

只有 3 个新增种子文件，`dishes.seed.recipes.json` 从未出现（也搜遍全仓无 `*recipes*` 文件，仅命中 `src/FoodMate.Core/Entities/RecipeStep.cs`）。`.dsh-team/seed-300/work/` 与 `review/` 下也没有任何草稿。

**证据 2 —— 新增 145 道菜里菜谱为 0。** 逐文件统计 `recipe` 键：

```
dishes.seed.dessert.json: total=40  withRecipe=0
dishes.seed.drink.json:   total=35  withRecipe=0
dishes.seed.json:         total=20  withRecipe=7
dishes.seed.more.json:    total=136 withRecipe=0
dishes.seed.more2.json:   total=70  withRecipe=0
```

`Select-String -Pattern '"recipe"'` 全部命中只落在 `dishes.seed.json`（7 次），即**本轮的 145 道新增菜一道菜谱都没有**，仍是扩产前的 7 道。

**证据 3 —— 后端实际加载结果。** 列表接口 `hasRecipe=true` 共 **7** 道；再遍历 301 个 `GET /api/v1/dishes/{id}` 复核：

```
RECIPE 西红柿炒蛋   steps=4
RECIPE 蛋炒饭       steps=4
RECIPE 番茄鸡蛋面   steps=4
RECIPE 番茄牛腩     steps=5
RECIPE 青椒土豆丝   steps=5
RECIPE 皮蛋瘦肉粥   steps=4
RECIPE 凉拌黄瓜     steps=2
TOTAL recipe!=null = 7; steps>=3 = 6
```

**判定：`recipe != null && steps >= 3` 的菜品为 6 道，距离要求的 40 道差 34 道。C3 未达成。**

### C4 ✅ 种子文件全部可解析、无重复菜名

- 5 个 JSON 均被 `ConvertFrom-Json` 与加载器双重成功解析，后端启动无异常（见 2.2）。
- 跨 5 个文件按菜名统计：`raw total = 301`，`distinct names = 301`，`duplicate name groups = 0`。
- DB 层复核：全量 301 条 `name` 分组，`DB duplicate names = 0`。
- 枚举合法性：新 3 个文件 `badCuisine=0 badCategory=0 badSpicy=0 priceMin>max=0`。
  （`dishes.seed.json` 用数字枚举 1–9，是历史写法，`JsonStringEnumConverter` 允许整数取值，非缺陷。）

### C5 ✅ 314 个测试全绿

```
PS> dotnet test FoodMate.slnx
已通过! - 失败: 0，通过: 184，已跳过: 0，总计: 184 - FoodMate.Core.Tests.dll
已通过! - 失败: 0，通过: 130，已跳过: 0，总计: 130 - FoodMate.Infrastructure.Tests.dll
[exit code: 0]
```

184 + 130 = **314**，与要求数字完全一致。加载器改动（`DishSeedLoader.cs`）未打破任何既有测试。

### C6 ✅ 小程序构建成功

```
PS> cd web; npm run build:mp-weixin
Compiler version: 5.24（vue3）
Compiling...
DONE  Build complete.
Run method: open Weixin Mini Program Devtools, import dist\build\mp-weixin run.
[exit code: 0]
```

仅有 Dart Sass `legacy-js-api` 弃用警告（stderr），与本次改动无关，不影响构建结论。

### C7 ✅ 新增菜字段齐全、ingredients ≥ 2

对 3 个新增文件共 145 道菜逐条校验 `cuisine / category / spicyLevel / priceMinCents / priceMaxCents / calories / ingredients / popularity`：

```
=== dishes.seed.dessert.json: entries=40 bad=0
=== dishes.seed.drink.json:   entries=35 bad=0
=== dishes.seed.more2.json:   entries=70 bad=0
```

进一步查空值/空串/空菜名/空食材名：三个文件 `issues=0`。
数值合理性抽查：`price<=0:0  calories<=0:0  popularity 越界:0  priceMin>priceMax:0`，popularity 分布 70–93，无异常值。

**API 层复核（防止加载器丢字段）**：全量 301 道列表 DTO 中 7 个标量字段 `null` 计数 = **0**；再遍历 301 个详情接口统计 `ingredients.length`：

```
new dishes (by name) = 145
dishes with ingredients<2 = 0 (of which new = 0)
```

「新增」判定方式：以 `dishes.seed.json` + `dishes.seed.more.json` 的 156 个历史菜名为基线，DB 中不在基线的即新增，恰为 **145 道**（40 + 35 + 70），与三个交付物条目数一致。

---

## 四、阻塞项

### B1 · C3 完全未达成：菜谱交付物缺失（critical）

- **现象**：`dishes.seed.recipes.json` 不存在；DB 中带菜谱的菜仍为扩产前的 7 道，其中 steps ≥ 3 的仅 6 道，要求 ≥ 40 道。
- **证据**：
  - `Get-ChildItem src\FoodMate.Infrastructure\Data\Seed` 输出无 `dishes.seed.recipes.json`；`git status --short` 仅 3 个未跟踪种子文件。
  - `dishes.seed.dessert.json / drink.json / more2.json` 的 `withRecipe` 均为 0；`Select-String '"recipe"'` 只命中 `dishes.seed.json` 7 次。
  - 遍历 301 个详情接口：`TOTAL recipe!=null = 7; steps>=3 = 6`。
- **影响**：任务核心目标之一（≥40 道完整菜谱）零完成度，缺口 34 道。
- **修复建议**：菜谱作者新建 `src/FoodMate.Infrastructure/Data/Seed/dishes.seed.recipes.json`，覆盖 `dishes.seed.json` / `dishes.seed.more.json` / 新增 3 个文件中的至少 40 道菜，每份 `steps` ≥ 3，字段用 `{ "name": "...", "recipe": { "servings":.., "cookMinutes":.., "difficulty":.., "steps":[{ "order":1,"text":"..","durationMinutes":.. }], "tips":".." } }`。加载器按名合并（`DishSeedLoader.cs:143 existing.Recipe ??= item.Recipe;`），且资源序 `dishes.seed.recipes.json` 排在最后，补空缺路径可用。
- **owner_role**：菜谱作者

### B2 · 「凉拌黄瓜」菜谱只有 2 步，违反「每份菜谱 ≥ 3 步」（minor）

- **现象**：该菜在列表接口 `hasRecipe=true`，但详情接口只有 2 步。
- **证据**：`src/FoodMate.Infrastructure/Data/Seed/dishes.seed.json:427-436`，`steps` 数组仅两条（第 432、433 行）：
  ```
  432: { "order": 1, "text": "黄瓜拍裂切段，加盐腌 5 分钟杀出水分", "durationMinutes": 5 },
  433: { "order": 2, "text": "倒掉渗出的水，加蒜末、醋、生抽、香油、辣椒油拌匀", "durationMinutes": 2 }
  ```
  详情接口复核：`RECIPE 凉拌黄瓜 steps=2`。
- **影响**：按 C3 的统计口径它不计入「完整菜谱」，是 7 道里被扣掉的那 1 道；字面上也违反「每份菜谱 ≥ 3 步」。
- **注意（重要，会影响返工方式）**：**不能**靠补充文件修掉。加载器合并用的是 `??=`（`DishSeedLoader.cs:143`），已有 `Recipe` 非空时补充定义会被直接丢弃；而 `Array.Sort(..., StringComparer.Ordinal)` 的实际顺序为
  ```
  1. dishes.seed.dessert.json
  2. dishes.seed.drink.json
  3. dishes.seed.json          ← 凉拌黄瓜 2 步菜谱在此
  4. dishes.seed.more.json
  5. dishes.seed.more2.json
  6. dishes.seed.recipes.json  ← 补充文件在此，永远覆盖不掉第 3 步
  ```
  所以修法只有两条：**(a)** 直接改 `dishes.seed.json:431-434` 把该菜谱补到 ≥3 步；或 **(b)** 把加载器合并语义从「只补空缺」改成「后加载的同名菜谱覆盖」。
- **owner_role**：菜谱作者

---

## 五、非阻塞观察（不影响判定，供编排者收尾参考）

1. **README 仍是旧数字，未更新。** `README.md:86`（「导入 156 道内置菜品」）、`:173`、`:319`（「**156 道菜**，覆盖 17 个菜系、8 个分类」）、`:494`（「M5.5 … 进行中（**156 道**）」）、`:516-517`（「已从 20 道扩到 **156 道** … 甜点只有 2 道、饮品 0 道」）均未同步为 301 / 甜点 42 / 饮品 35。`plan.md:103` 已把「README 的菜品数量更新」定为编排者的收尾动作，本轮尚未执行。此项不在 C1–C7 内，故不阻塞。
2. **C1 余量只有 1 道。** 301 vs 阈值 300，任何一道菜因重名合并、空名跳过或后续改动丢失都会立刻击穿 C1。当前 5 个文件重名 0、空名 0，暂时稳定，但没有缓冲。
3. **加载器改动目前是「未被执行的代码路径」。** 本轮 5 个文件重名数为 0，`DishSeedLoader.cs:141-152` 的合并分支从未进入 —— 该改动是为「菜谱补充文件」铺路的，而该文件尚未交付（见 B1）。另外该合并只补 `Recipe / Description / ImageUrl / PriceMin / PriceMax / Calories / Aliases / Tags / Ingredients`，同名但 `category`/`cuisine` 冲突的定义会被**静默吞掉**且无日志告警，未来若真出现重名将难以排查。
4. **甜点/饮品达标数含 2 道历史菜。** 42 = 40 新增 + 冰糖葫芦（`dishes.seed.more.json`）+ 银耳莲子羹（`dishes.seed.json`）；饮品 35 全为新增。增量贡献 40 + 35 = 75 道，与角色卡目标一致。
5. **扩充厨师未越界。** `dishes.seed.more2.json` 70 道分布为 早餐 16 / 小吃 16 / 汤羹 14 / 主食 12 / 素菜 12，未写甜点与饮品，符合 `plan.md:78` 的边界要求。
6. **菜名内容抽查未见灌水。** 40 道甜点（杨枝甘露、双皮奶、姜撞奶、驴打滚、豌豆黄…）与 35 道饮品（酸梅汤、港式丝袜奶茶、生椰拿铁…）均为真实存在的菜品，无占位符、无重复命名、无「菜品1/菜品2」式填充。

---

## 六、复现步骤（供返工后复验）

```powershell
cd C:\Work\美食伴侣
dotnet build FoodMate.slnx

# 杀旧后端，避免旧种子残留
Get-NetTCPConnection -LocalPort 5199 -State Listen -EA SilentlyContinue |
  ForEach-Object { Stop-Process -Id $_.OwningProcess -Force }

dotnet run --project src/FoodMate.Api --no-build --urls http://localhost:5199   # 后台

# C1
Invoke-RestMethod http://localhost:5199/health/ready

# C2 / C4（全量分页后按 categoryLabel 分组）
$p=1; $all=@()
while ($true) {
  $r = Invoke-RestMethod "http://localhost:5199/api/v1/dishes?page=$p&pageSize=100"
  if ($r.data.items.Count -eq 0) { break }
  $all += $r.data.items; $p++
}
$all | Group-Object categoryLabel | Sort-Object Name | Format-Table Name,Count

# C3（遍历详情，统计 recipe != null && steps >= 3）
$all | ForEach-Object {
  $d = (Invoke-RestMethod "http://localhost:5199/api/v1/dishes/$($_.id)" `
        -Headers @{ 'X-Device-Id' = 'qa' }).data
  if ($d.recipe) { "{0} steps={1}" -f $d.name, @($d.recipe.steps).Count }
}

# C5
dotnet test FoodMate.slnx

# C6
cd web; npm run build:mp-weixin
```

> 注：详情接口需要身份。开发环境下 `CurrentUser.ResolveAsync`（`src/FoodMate.Api/Endpoints/AuthEndpoints.cs:199-208`）允许 `X-Device-Id` 头降级为游客身份，故 `-Headers @{ 'X-Device-Id' = 'qa' }` 即可，无需先登录。

---

## 七、判定

**FAIL。** C1、C2、C4、C5、C6、C7 六条达成，**C3 未达成**（阻塞项 B1，缺口 34 道菜谱），另有次要阻塞项 B2。返工后需重新验收 C3 全量口径。
