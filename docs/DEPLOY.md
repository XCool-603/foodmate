# 美食伴侣 · 部署手册

> 配套文档：[总设计文档](./DESIGN.md) · [数据库设计](./DATABASE.md) · [API 契约](./API.md)

---

## 1. 前置条件

| 项目 | 要求 |
|---|---|
| Docker | 20.10+（含 Docker Compose v2） |
| 内存 | ≥ 1 GB（API 约 150 MB，PostgreSQL 约 300 MB） |
| 磁盘 | ≥ 5 GB（镜像约 400 MB，其余留给数据库与上传图片） |
| 网络 | 需要能拉取镜像与 NuGet 包；**生产环境需已备案的 HTTPS 域名** |

> 💡 不需要单独安装 .NET SDK —— 构建在容器内完成。

---

## 2. 一键部署

### 2.1 Linux / macOS

```bash
git clone <你的仓库地址> foodmate && cd foodmate
./deploy/deploy.sh
```

脚本会依次完成：

1. **检查环境** —— docker 是否安装、守护进程是否运行、compose 是否可用
2. **生成配置** —— 首次运行时从 `.env.example` 生成 `.env`，并**自动填入随机数据库密码与 JWT 密钥**（权限设为 600）
3. **构建镜像** —— 多阶段构建，只把运行时打进最终镜像
4. **启动并等待** —— 等到 `/health` 返回 200 才宣告成功；容器起不来会直接把日志打出来

### 2.2 Windows

```powershell
git clone <你的仓库地址> foodmate
cd foodmate
.\deploy\deploy.ps1
```

需要先启动 Docker Desktop。

### 2.3 部署完必做的一件事

**检查 `.env`，至少配置一个平台的登录凭据**，否则没有任何登录方式可用：

```bash
# 微信
WECHAT_APPID=wx1234567890abcdef
WECHAT_SECRET=...

# 支付宝（还需要应用私钥，用于 RSA2 签名）
ALIPAY_APPID=2021000000000000
ALIPAY_SECRET=...
ALIPAY_PRIVATE_KEY=<PKCS#8 Base64，单行>

# 抖音
DOUYIN_APPID=tt1234567890abcdef
DOUYIN_SECRET=...
```

改完重启：

```bash
docker compose up -d --force-recreate api
```

---

## 3. 配置说明

全部配置通过 `.env` 注入。常用项：

| 变量 | 默认 | 说明 |
|---|---|---|
| `API_PORT` | `8080` | 宿主机映射端口 |
| `POSTGRES_PASSWORD` | 自动生成 | **必填** |
| `JWT_SIGNING_KEY` | 自动生成 | **必填**，≥ 32 字节。改了会让所有已发令牌失效 |
| `ACCESS_TOKEN_MINUTES` | `120` | 访问令牌有效期 |
| `REFRESH_TOKEN_DAYS` | `30` | 刷新令牌有效期 |
| `ALLOW_GUEST_LOGIN` | `false` | 生产环境保持关闭：游客身份无法跨设备恢复 |
| `AI_PROVIDER` | `Stub` | `Stub`（本地桩）或 `OpenAiCompatible`（真实模型） |
| `AI_BASE_URL` | 空 | 如 `https://dashscope.aliyuncs.com/compatible-mode/v1` |
| `AI_API_KEY` | 空 | 大模型密钥 |

> ⚠️ **`.env` 绝不能提交到仓库**。它已被 `.gitignore` 与 `.dockerignore` 双重排除。

### 手动生成密钥

```bash
openssl rand -base64 48     # JWT_SIGNING_KEY
openssl rand -base64 24     # POSTGRES_PASSWORD
```

---

## 4. 一键升级

```bash
./deploy/upgrade.sh              # 用本地代码升级
./deploy/upgrade.sh --pull       # 先 git pull 再升级
./deploy/upgrade.sh --no-backup  # 跳过数据库备份（不推荐）
```

Windows：`.\deploy\upgrade.ps1 -Pull`

### 升级流程

```
① 备份数据库          pg_dump --clean --if-exists → backups/foodmate-<时间戳>.sql.gz
                      （只保留最近 10 份，自动清理旧备份）
② 标记当前镜像        foodmate-api:latest → foodmate-api:backup-<时间戳>
③ 拉取代码            git pull --ff-only（--pull 时）
④ 构建并替换          docker compose build api && up -d --no-deps api
⑤ 健康检查            轮询 /health，最多等 180 秒
    ├─ 通过 → 完成，打印回滚命令
    └─ 失败 → 自动回滚到 backup-<时间戳> 镜像，并打印日志
```

### 回滚

```bash
./deploy/upgrade.sh --rollback   # 回到最近一次备份镜像
```

> ⚠️ **数据库不会自动还原。** 如果新版本执行过数据库迁移，自动还原可能导致数据不一致。
> 备份文件保留在 `backups/`，确认后再人工决定是否恢复：
>
> ```bash
> gunzip -c backups/foodmate-20261002-120000.sql.gz | \
>   docker compose exec -T db psql -U foodmate -d foodmate
> ```

### 数据库迁移是怎么跑的

应用启动时自动执行（见 `DbInitializer`）：

- **PostgreSQL（生产）** → `Database.Migrate()`，按 `src/FoodMate.Infrastructure/Data/Migrations/` 里的迁移文件升级
- **SQLite（开发）** → `EnsureCreated()`，不维护迁移

新增迁移：

```bash
# 默认按 PostgreSQL 生成（生产用的就是它）
dotnet ef migrations add <迁移名> -p src/FoodMate.Infrastructure -s src/FoodMate.Api

# 确认将要执行的 SQL
dotnet ef migrations script -p src/FoodMate.Infrastructure -s src/FoodMate.Api -o schema.sql
```

---

## 5. 常用运维命令

```bash
docker compose ps                      # 查看状态
docker compose logs -f api             # 跟随 API 日志
docker compose logs --tail=100 db      # 查看数据库日志
docker compose restart api             # 重启 API
docker compose down                    # 停止（保留数据卷）
docker compose down -v                 # 停止并删除数据（⚠️ 会清空数据库）

# 进入数据库
docker compose exec db psql -U foodmate -d foodmate

# 查看健康状态
curl http://localhost:8080/health
curl http://localhost:8080/health/ready

# 磁盘占用
docker system df
```

---

## 6. 生产环境清单

上线前逐项确认：

- [ ] **`.env` 里的密钥全部换成随机值**（`deploy.sh` 已自动处理）
- [ ] **至少配置一个平台的登录凭据**
- [ ] **`ALLOW_GUEST_LOGIN=false`**
- [ ] **`JWT_SIGNING_KEY` ≥ 32 字节**，且已安全备份（丢失 = 所有用户被登出）
- [ ] **HTTPS 反向代理已配置**（Nginx / Caddy / 云厂商负载均衡）
- [ ] **域名已备案**，并在小程序后台配置为合法域名
- [ ] **数据库不对公网暴露**（compose 默认不映射 5432 端口）
- [ ] **`backups/` 目录已纳入定期备份**（或挂到对象存储）
- [ ] **图片存储换成对象存储** —— 当前用本地卷，多实例部署时各实例文件不共享
- [ ] **监控告警**：`/health` 探活、磁盘、容器重启次数

### HTTPS 反向代理示例（Nginx）

```nginx
server {
    listen 443 ssl http2;
    server_name api.your-domain.com;

    ssl_certificate     /etc/letsencrypt/live/api.your-domain.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/api.your-domain.com/privkey.pem;

    # 上传图片可能较大，放宽限制（应用侧上限 5 MB）
    client_max_body_size 10m;

    location / {
        proxy_pass         http://127.0.0.1:8080;
        proxy_http_version 1.1;
        proxy_set_header   Host              $host;
        proxy_set_header   X-Real-IP         $remote_addr;
        proxy_set_header   X-Forwarded-For   $proxy_add_x_forwarded_for;
        proxy_set_header   X-Forwarded-Proto $scheme;

        # AI 识别可能跑十几秒，别用默认的 60s
        proxy_read_timeout 120s;
    }
}
```

---

## 7. 不使用 Docker 的部署

如果目标机器已有 .NET 10 运行时：

```bash
# 构建
dotnet publish src/FoodMate.Api -c Release -o /opt/foodmate

# 配置（用环境变量，双下划线代表层级）
export ASPNETCORE_ENVIRONMENT=Production
export ConnectionStrings__Default="Host=...;Database=foodmate;Username=...;Password=..."
export Auth__Jwt__SigningKey="<≥32 字节>"
export Auth__AllowGuestLogin=false

# 运行
cd /opt/foodmate && dotnet FoodMate.Api.dll
```

配合 systemd：

```ini
[Unit]
Description=FoodMate API
After=network.target postgresql.service

[Service]
WorkingDirectory=/opt/foodmate
ExecStart=/usr/bin/dotnet /opt/foodmate/FoodMate.Api.dll
Restart=always
RestartSec=5
EnvironmentFile=/opt/foodmate/.env

[Install]
WantedBy=multi-user.target
```

---

## 8. 常见问题

**Q：启动失败，日志说 `Auth:Jwt:SigningKey` 未配置**

生产环境强制要求显式配置签名密钥，这是刻意设计的——避免带着开发用的临时密钥上线。
在 `.env` 里设置 `JWT_SIGNING_KEY`（≥ 32 字节）。

**Q：启动失败，日志说「没有可用的登录方式」**

`ALLOW_GUEST_LOGIN=false` 且没配任何平台凭据。至少配置一个平台的 AppId/AppSecret。

**Q：API 起来了但 `/health/ready` 报数据库不健康**

数据库还没就绪。compose 里已配置 `depends_on: condition: service_healthy`，
正常情况下不会出现。若仍出现，检查 `docker compose logs db`。

**Q：登录返回「抖音登录未配置：请设置 Auth:Douyin:AppId 与 AppSecret」**

对应平台的凭据没配。这是预期行为——错误信息会明确告诉缺什么。

**Q：支付宝登录报「应用私钥无法解析」**

`ALIPAY_PRIVATE_KEY` 需要是 **PKCS#8 或 PKCS#1 的 Base64 内容**（可带 PEM 头尾与换行，程序会自行清理）。
在支付宝开放平台「开发设置 → 接口加签方式」里生成。

**Q：升级后所有用户都被登出了**

`JWT_SIGNING_KEY` 变了。检查 `.env` 是否被改动或丢失。

**Q：上传的图片在重启后消失了**

本地卷没挂载或用了 `down -v`。检查 `docker volume ls` 里是否有 `foodmate_uploads`。
生产环境建议换成对象存储。

**Q：前端构建时提示找不到 API 地址**

`web/.env.production` 里的 `VITE_API_BASE_URL` 需要改成你的真实域名。

---

## 9. 架构一览

```
                    ┌──────────────────┐
   小程序客户端 ───► │  反向代理 (HTTPS) │
                    └────────┬─────────┘
                             ▼
                    ┌──────────────────┐
                    │  foodmate-api    │  .NET 10 / 非 root
                    │  :8080           │  健康检查 /health
                    └────────┬─────────┘
                             ▼
                    ┌──────────────────┐
                    │  foodmate-db     │  PostgreSQL 16
                    │  仅内网可达       │  数据卷 pgdata
                    └──────────────────┘
                             
    数据卷：pgdata（数据库） / uploads（用户图片）
    网络：foodmate（bridge，与宿主机隔离）
```
