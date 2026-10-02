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
# ─────────────────────────────────────────────────────────────

# ── ① 构建阶段 ────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# 先只复制工程文件，让 restore 层可被缓存
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

# curl 用于容器健康检查；标准 aspnet 镜像不自带
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# 以非 root 用户运行
RUN groupadd --system foodmate \
    && useradd --system --gid foodmate --create-home foodmate

WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    # 容器内不需要把异常栈暴露给客户端
    DOTNET_ENVIRONMENT=Production

COPY --from=build /app/publish .

# 上传目录：本地存储实现会往这里写图片，必须可写
RUN mkdir -p /app/wwwroot/uploads && chown -R foodmate:foodmate /app

USER foodmate

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
    CMD curl -fsS http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "FoodMate.Api.dll"]
