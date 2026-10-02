# ─────────────────────────────────────────────────────────────
#  美食伴侣 · API 镜像
#
#  多阶段构建：
#    ① 先把工程文件单独复制进来 restore —— 只要依赖没变，这一层就能命中缓存
#    ② 再复制源码 publish
#    ③ 运行阶段只带运行时，镜像体积小很多
#
#  构建：docker build -t foodmate-api .
#  运行：见 docker-compose.yml / deploy/deploy.sh
#
#  ⚠️ 本文件尚未经过实际 docker build 验证（开发机未安装 Docker）。
#     首次部署时请留意构建输出，有报错欢迎反馈。
# ─────────────────────────────────────────────────────────────

# ── ① 构建阶段 ────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# 先只复制工程文件，让 restore 层可被缓存。
# 若把 COPY src/ 写在 restore 之前，改任何一行源码都会重装依赖。
COPY Directory.Build.props ./
COPY src/FoodMate.Core/FoodMate.Core.csproj                     src/FoodMate.Core/
COPY src/FoodMate.Contracts/FoodMate.Contracts.csproj           src/FoodMate.Contracts/
COPY src/FoodMate.Infrastructure/FoodMate.Infrastructure.csproj src/FoodMate.Infrastructure/
COPY src/FoodMate.Api/FoodMate.Api.csproj                       src/FoodMate.Api/

RUN dotnet restore src/FoodMate.Api/FoodMate.Api.csproj

# 再复制源码并发布
COPY src/ src/

RUN dotnet publish src/FoodMate.Api/FoodMate.Api.csproj \
        -c Release \
        -o /app/publish \
        --no-restore \
        /p:UseAppHost=false

# ── ② 运行阶段 ────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# curl 用于容器健康检查；标准 aspnet 镜像不自带。
# -y + 非交互 + 清缓存，避免构建挂住或镜像变大。
ENV DEBIAN_FRONTEND=noninteractive
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# 以非 root 运行。
# 固定 UID/GID 便于和宿主机对齐；--create-home 是必须的 ——
# 用户没有 HOME 时，工具写 ~/.cache 会以很难排查的方式失败。
ARG UID=10001
ARG GID=10001
RUN groupadd --system --gid "${GID}" foodmate \
    && useradd --system --uid "${UID}" --gid foodmate \
               --create-home --shell /usr/sbin/nologin foodmate

WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    HOME=/home/foodmate

# --chown 让运行期用户直接拥有产物。
# 不用 RUN chown -R：那会多生成一整个数据层，镜像更大、构建更慢。
COPY --from=build --chown=foodmate:foodmate /app/publish .

# 上传目录：本地存储实现会往这里写图片，必须可写。
# 单独用 install 建目录并定属主 —— COPY --chown 只管拷进来的内容。
# 这一步也是 docker-compose 里 uploads 命名卷属主能对齐的前提：
# 命名卷首次创建时会连同镜像中该路径的属主一起复制。
RUN install -d -o foodmate -g foodmate /app/wwwroot/uploads

# USER 只出现一次，且在所有需要 root 的步骤之后
USER foodmate

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
    CMD curl -fsS http://localhost:8080/health || exit 1

# exec 形式：PID 1 直接是 dotnet，docker stop 的 SIGTERM 才能送达
ENTRYPOINT ["dotnet", "FoodMate.Api.dll"]
