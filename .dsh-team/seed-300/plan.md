# 任务：菜品种子扩到 300+，补齐甜点与饮品

任务目录：`.dsh-team/seed-300/`
接单时间：本轮
前置状态：菜品 156 道（17 菜系 / 8 分类），其中**甜点仅 2 道、饮品 0 道**，带完整菜谱的仅 7 道

---

## 一、完成定义

菜品种子从 156 道扩到 **300 道以上**，补齐「甜点」「饮品」两个近乎空白的分类，
并让**至少 40 道菜带完整菜谱**，全部能被现有加载器正常加载、通过全部测试与小程序构建。

---

## 二、验收标准（7 条，均可机械判定）

| # | 标准 | 判定方式 |
|---|---|---|
| C1 | 菜品总数 ≥ 300 | 启动后端，`GET /health/ready` 的 `checks.dishCount` |
| C2 | 甜点分类 ≥ 30 道、饮品分类 ≥ 25 道 | `GET /api/v1/dishes` 全量分页后按 `categoryLabel` 分组计数 |
| C3 | 至少 40 道菜带完整菜谱，且每份菜谱 ≥ 3 步 | 遍历 `GET /api/v1/dishes/{id}`，统计 `recipe != null && recipe.steps.length >= 3` |
| C4 | 种子文件全部可被加载器解析，且无重复菜名 | 后端启动日志无异常；DB 中 `name` 唯一（加载器按名合并，重复不会产生两条） |
| C5 | `dotnet test` 全绿（314 个） | `dotnet test FoodMate.slnx` |
| C6 | `npm run build:mp-weixin` 构建成功 | `cd web && npm run build:mp-weixin` 输出 `Build complete` |
| C7 | 每道新增菜都含必需字段，且 `ingredients` 非空 | 脚本遍历新增 JSON：`cuisine/category/spicyLevel/priceMinCents/priceMaxCents/calories/ingredients/popularity` 均存在，`ingredients.length >= 2` |

---

## 三、工作流与依赖

```
[编排者] 先修加载器：去重从「先到先得丢弃」改为「按名合并且只补空缺」
         ↓（这是并行化的前提，否则「菜谱补充」无法独立成文件）
         ├── W1 甜点      → dishes.seed.dessert.json   （新文件，40 道）
         ├── W2 饮品      → dishes.seed.drink.json     （新文件，35 道）
         ├── W3 其他分类  → dishes.seed.more2.json     （新文件，70 道）
         └── W4 菜谱补全  → dishes.seed.recipes.json   （新文件，40+ 道菜的菜谱）
                    ↓
              [产品验收]（独立代理）
```

**为什么这四条能真并行**：每条工作流**只写自己那一个文件**，文件之间零重叠。
加载器按资源名排序后逐个读入，再按菜名合并 —— 所以「补充文件」只需要写
「菜名 + 要补的字段」，不会覆盖基础信息。

**为什么加载器要先改**：原实现是 `if (!seen.Add(name)) continue;`，即同名菜品
**丢弃后者**。若不改，「菜谱补充」文件里的菜谱会被前面已定义的同名菜品吞掉，
W4 整条工作流就是白干。这是编排者必须先拆掉的阻碍，不能丢给子代理。

---

## 四、角色卡（4 个产出角色）

### R1 甜点师

- **目标**：新增 40 道甜点，让「甜点」分类从 2 道变成 42 道
- **输入**：`src/FoodMate.Infrastructure/Data/Seed/dishes.seed.more.json`（读它了解紧凑格式）、
  `src/FoodMate.Core/Enums/Cuisine.cs`、`src/FoodMate.Core/Enums/DishCategory.cs`（枚举名以此为准）
- **交付物**：`src/FoodMate.Infrastructure/Data/Seed/dishes.seed.dessert.json`
- **验收标准**：C2（甜点部分）、C7
- **边界**：只写这一个文件；不改加载器、不改其他种子文件、不改代码

### R2 饮品师

- **目标**：新增 35 道饮品，让「饮品」分类从 0 道变成 35 道
- **输入**：同 R1
- **交付物**：`src/FoodMate.Infrastructure/Data/Seed/dishes.seed.drink.json`
- **验收标准**：C2（饮品部分）、C7
- **边界**：只写这一个文件；不改加载器、不改其他种子文件、不改代码

### R3 扩充厨师

- **目标**：在**除甜点、饮品外**的分类上新增 70 道，重点补早餐 / 小吃 / 汤羹 / 素菜 / 主食
- **输入**：同 R1
- **交付物**：`src/FoodMate.Infrastructure/Data/Seed/dishes.seed.more2.json`
- **验收标准**：C1、C7
- **边界**：只写这一个文件；**不要写甜点和饮品**（那是 R1/R2 的活）；
  不改加载器、不改其他种子文件、不改代码

### R4 菜谱作者

- **目标**：为**已存在的** 40 道菜补完整菜谱，让「自己做」模式点进去就有做法
- **输入**：`dishes.seed.json` 与 `dishes.seed.more.json`（读它们拿到现有菜名与食材）
- **交付物**：`src/FoodMate.Infrastructure/Data/Seed/dishes.seed.recipes.json`
- **验收标准**：C3
- **边界**：**只补菜谱**，不重新定义菜品属性（加载器只会用 `recipe` 补空缺）；
  只写这一个文件；不改加载器、不改其他种子文件、不改代码

---

## 五、验收者（1 个，独立）

**产品验收** —— 不是上述任何一个角色的产出者。
它必须自己读 JSON、自己起后端、自己跑命令，不接受任何人的自述。
默认对抗性：去找真缺陷。每条阻塞项必须带 `文件:行` 或命令输出作为证据。

---

## 六、规模控制说明

- **4 个产出 + 1 个验收 = 5 个代理**，落在 2–6 区间内。
- 没有单设「文档角色」：README 的菜品数量更新是编排者的收尾动作，不值得开一个角色。
- 没有单设「测试角色」：C5/C6 是验收者要跑的判定命令，不是独立工作流。
- 没有把 W1–W4 合并成一个角色：145 道菜的产出量对单次 `agent()` 调用太大，
  且合起来会失去并行收益。

---

## 七、平台期保护

连续两轮阻塞项数量没有下降 → 停止返工，向用户升级。循环上限 8 轮。
