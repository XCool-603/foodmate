#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────
#  美食伴侣 · 一键部署
#
#  做四件事：
#    ① 检查环境（docker / compose）
#    ② 首次运行时生成 .env 并自动填入随机密钥
#    ③ 构建镜像
#    ④ 启动并等到健康检查通过
#
#  用法：
#    ./deploy/deploy.sh              # 完整部署
#    ./deploy/deploy.sh --no-build   # 只重启，不重新构建
#    ./deploy/deploy.sh --logs       # 部署完自动跟随日志
# ─────────────────────────────────────────────────────────────
set -Eeuo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
cd "${ROOT_DIR}"

# ── 输出 ────────────────────────────────────────────────────
if [ -t 1 ]; then
  C_RESET='\033[0m'; C_INFO='\033[36m'; C_OK='\033[32m'; C_WARN='\033[33m'; C_ERR='\033[31m'
else
  C_RESET=''; C_INFO=''; C_OK=''; C_WARN=''; C_ERR=''
fi

info()  { printf "${C_INFO}[信息]${C_RESET} %s\n" "$*"; }
ok()    { printf "${C_OK}[完成]${C_RESET} %s\n" "$*"; }
warn()  { printf "${C_WARN}[注意]${C_RESET} %s\n" "$*"; }
fail()  { printf "${C_ERR}[失败]${C_RESET} %s\n" "$*" >&2; exit 1; }

# ── 参数 ────────────────────────────────────────────────────
DO_BUILD=1
FOLLOW_LOGS=0
for arg in "$@"; do
  case "${arg}" in
    --no-build) DO_BUILD=0 ;;
    --logs)     FOLLOW_LOGS=1 ;;
    -h|--help)  sed -n '2,20p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *)          fail "未知参数：${arg}（用 --help 查看用法）" ;;
  esac
done

# ── ① 环境检查 ──────────────────────────────────────────────
info "检查环境…"

command -v docker >/dev/null 2>&1 \
  || fail "未找到 docker。请先安装：https://docs.docker.com/engine/install/"

docker info >/dev/null 2>&1 \
  || fail "docker 守护进程未运行，或当前用户没有权限（试试 sudo，或把用户加入 docker 组）。"

# 兼容 docker compose（v2 插件）与 docker-compose（v1 独立命令）
if docker compose version >/dev/null 2>&1; then
  COMPOSE="docker compose"
elif command -v docker-compose >/dev/null 2>&1; then
  COMPOSE="docker-compose"
  warn "使用的是旧版 docker-compose，建议升级到 docker compose v2。"
else
  fail "未找到 docker compose。请安装 Docker Compose v2。"
fi

ok "docker 环境正常（${COMPOSE}）"

# ── ② 生成配置 ──────────────────────────────────────────────
# 优先用 openssl，没有就退回 /dev/urandom —— 两者都足够随机
random_secret() {
  local bytes="${1:-48}"
  if command -v openssl >/dev/null 2>&1; then
    openssl rand -base64 "${bytes}" | tr -d '\n'
  else
    head -c "${bytes}" /dev/urandom | base64 | tr -d '\n'
  fi
}

if [ ! -f .env ]; then
  info "未找到 .env，正在从模板生成并填入随机密钥…"

  [ -f .env.example ] || fail "缺少 .env.example，无法生成配置。"

  cp .env.example .env

  local_db_password="$(random_secret 24)"
  local_jwt_key="$(random_secret 48)"

  # 用 | 作分隔符，避免密钥里的 / 破坏 sed 表达式
  sed -i.bak "s|^POSTGRES_PASSWORD=.*|POSTGRES_PASSWORD=${local_db_password}|" .env
  sed -i.bak "s|^JWT_SIGNING_KEY=.*|JWT_SIGNING_KEY=${local_jwt_key}|" .env
  rm -f .env.bak

  chmod 600 .env

  ok "已生成 .env（数据库密码与 JWT 密钥均为随机生成，权限 600）"
  warn "请检查 .env 里的三端凭据与 AI 配置 —— 至少配置一个平台才能登录。"
else
  info "已存在 .env，沿用现有配置。"
fi

# 校验必填项（compose 里用了 :? 语法，这里提前给出更友好的提示）
# shellcheck disable=SC1091
set -a; . ./.env; set +a

[ -n "${POSTGRES_PASSWORD:-}" ] || fail ".env 中 POSTGRES_PASSWORD 为空。"
[ -n "${JWT_SIGNING_KEY:-}" ]  || fail ".env 中 JWT_SIGNING_KEY 为空。"

if [ "${#JWT_SIGNING_KEY}" -lt 32 ]; then
  fail "JWT_SIGNING_KEY 至少需要 32 个字符，当前只有 ${#JWT_SIGNING_KEY} 个。"
fi

# 生产环境必须至少有一个真实登录方式
if [ "${ALLOW_GUEST_LOGIN:-false}" != "true" ] \
   && [ -z "${WECHAT_APPID:-}" ] && [ -z "${ALIPAY_APPID:-}" ] && [ -z "${DOUYIN_APPID:-}" ]; then
  fail "未配置任何平台凭据，且游客登录已关闭 —— 将没有任何登录方式可用。
       请在 .env 中至少填写 WECHAT_APPID / ALIPAY_APPID / DOUYIN_APPID 之一，
       或临时设置 ALLOW_GUEST_LOGIN=true（不建议用于生产）。"
fi

# ── ③ 构建 ──────────────────────────────────────────────────
if [ "${DO_BUILD}" -eq 1 ]; then
  info "构建镜像（首次构建需要几分钟）…"
  ${COMPOSE} build api
  ok "镜像构建完成"
else
  info "跳过构建（--no-build）"
fi

# ── ④ 启动并等待健康 ────────────────────────────────────────
info "启动服务…"
${COMPOSE} up -d --remove-orphans

info "等待服务就绪…"

API_PORT_VALUE="${API_PORT:-8080}"
HEALTH_URL="http://localhost:${API_PORT_VALUE}/health"
MAX_WAIT=180
elapsed=0

while [ "${elapsed}" -lt "${MAX_WAIT}" ]; do
  if curl -fsS "${HEALTH_URL}" >/dev/null 2>&1; then
    ok "服务已就绪"
    break
  fi

  # 容器已经挂了就不必再等
  if ! ${COMPOSE} ps --status running --services 2>/dev/null | grep -q '^api$'; then
    echo
    ${COMPOSE} logs --tail=50 api || true
    fail "api 容器未能保持运行，请查看上面的日志。"
  fi

  sleep 3
  elapsed=$((elapsed + 3))
  printf '.'
done
echo

if [ "${elapsed}" -ge "${MAX_WAIT}" ]; then
  ${COMPOSE} logs --tail=50 api || true
  fail "等待 ${MAX_WAIT} 秒后服务仍未就绪。"
fi

# ── 完成 ────────────────────────────────────────────────────
echo
ok "部署完成 🎉"
echo
echo "  接口地址    http://localhost:${API_PORT_VALUE}"
echo "  健康检查    ${HEALTH_URL}"
echo "  就绪检查    http://localhost:${API_PORT_VALUE}/health/ready"
echo
echo "  查看日志    ${COMPOSE} logs -f api"
echo "  重启服务    ${COMPOSE} restart api"
echo "  停止服务    ${COMPOSE} down"
echo "  一键升级    ./deploy/upgrade.sh"
echo

if [ "${FOLLOW_LOGS}" -eq 1 ]; then
  info "跟随日志（Ctrl+C 退出，不会停止服务）…"
  ${COMPOSE} logs -f api
fi
