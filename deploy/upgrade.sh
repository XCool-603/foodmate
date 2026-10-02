#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────
#  美食伴侣 · 一键升级
#
#  做五件事：
#    ① 备份数据库（pg_dump 压缩存档）
#    ② 把当前镜像打上备份标签（用于回滚）
#    ③ 拉取最新代码（--pull 时）
#    ④ 重新构建并滚动替换 api 容器
#    ⑤ 健康检查；失败则自动回滚到上一个镜像
#
#  用法：
#    ./deploy/upgrade.sh              # 用本地代码升级
#    ./deploy/upgrade.sh --pull       # 先 git pull 再升级
#    ./deploy/upgrade.sh --rollback   # 回滚到最近一次备份镜像
#    ./deploy/upgrade.sh --no-backup  # 跳过数据库备份（不推荐）
# ─────────────────────────────────────────────────────────────
set -Eeuo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
cd "${ROOT_DIR}"

if [ -t 1 ]; then
  C_RESET='\033[0m'; C_INFO='\033[36m'; C_OK='\033[32m'; C_WARN='\033[33m'; C_ERR='\033[31m'
else
  C_RESET=''; C_INFO=''; C_OK=''; C_WARN=''; C_ERR=''
fi

info() { printf "${C_INFO}[信息]${C_RESET} %s\n" "$*"; }
ok()   { printf "${C_OK}[完成]${C_RESET} %s\n" "$*"; }
warn() { printf "${C_WARN}[注意]${C_RESET} %s\n" "$*"; }
fail() { printf "${C_ERR}[失败]${C_RESET} %s\n" "$*" >&2; exit 1; }

# ── 参数 ────────────────────────────────────────────────────
DO_PULL=0
DO_BACKUP=1
DO_ROLLBACK=0

for arg in "$@"; do
  case "${arg}" in
    --pull)      DO_PULL=1 ;;
    --no-backup) DO_BACKUP=0 ;;
    --rollback)  DO_ROLLBACK=1 ;;
    -h|--help)   sed -n '2,22p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *)           fail "未知参数：${arg}（用 --help 查看用法）" ;;
  esac
done

# ── 环境检查 ────────────────────────────────────────────────
command -v docker >/dev/null 2>&1 || fail "未找到 docker。"
docker info >/dev/null 2>&1 || fail "docker 守护进程未运行，或当前用户没有权限。"

if docker compose version >/dev/null 2>&1; then
  COMPOSE="docker compose"
elif command -v docker-compose >/dev/null 2>&1; then
  COMPOSE="docker-compose"
else
  fail "未找到 docker compose。"
fi

[ -f .env ] || fail "未找到 .env。请先运行 ./deploy/deploy.sh 完成首次部署。"

# shellcheck disable=SC1091
set -a; . ./.env; set +a

API_PORT_VALUE="${API_PORT:-8080}"
HEALTH_URL="http://localhost:${API_PORT_VALUE}/health"
IMAGE_NAME="foodmate-api"
BACKUP_DIR="${ROOT_DIR}/backups"

mkdir -p "${BACKUP_DIR}"

# ── 回滚模式 ────────────────────────────────────────────────
if [ "${DO_ROLLBACK}" -eq 1 ]; then
  latest_backup="$(docker images --format '{{.Tag}}' "${IMAGE_NAME}" \
    | grep '^backup-' | sort -r | head -n1 || true)"

  [ -n "${latest_backup}" ] || fail "没有找到任何备份镜像（${IMAGE_NAME}:backup-*）。"

  info "回滚到 ${IMAGE_NAME}:${latest_backup}…"

  docker tag "${IMAGE_NAME}:${latest_backup}" "${IMAGE_NAME}:${IMAGE_TAG:-latest}"
  ${COMPOSE} up -d --no-deps api

  sleep 5
  curl -fsS "${HEALTH_URL}" >/dev/null 2>&1 \
    && ok "回滚完成，服务已恢复" \
    || fail "回滚后服务仍不健康，请查看日志：${COMPOSE} logs api"

  exit 0
fi

echo
info "开始升级…"
echo

# ── ① 数据库备份 ────────────────────────────────────────────
TIMESTAMP="$(date +%Y%m%d-%H%M%S)"
BACKUP_FILE="${BACKUP_DIR}/foodmate-${TIMESTAMP}.sql.gz"

if [ "${DO_BACKUP}" -eq 1 ]; then
  info "备份数据库 → ${BACKUP_FILE#"${ROOT_DIR}/"}"

  if ${COMPOSE} ps --status running --services 2>/dev/null | grep -q '^db$'; then
    if ${COMPOSE} exec -T db pg_dump \
         -U "${POSTGRES_USER:-foodmate}" \
         -d "${POSTGRES_DB:-foodmate}" \
         --clean --if-exists \
         | gzip > "${BACKUP_FILE}"; then
      ok "备份完成（$(du -h "${BACKUP_FILE}" | cut -f1)）"
    else
      rm -f "${BACKUP_FILE}"
      fail "数据库备份失败，已中止升级。确认无误后可加 --no-backup 跳过。"
    fi
  else
    warn "数据库容器未运行，跳过备份（可能是首次升级）。"
    rm -f "${BACKUP_FILE}"
  fi

  # 只保留最近 10 份备份，避免磁盘被撑满
  ls -1t "${BACKUP_DIR}"/foodmate-*.sql.gz 2>/dev/null | tail -n +11 | xargs -r rm -f
else
  warn "已跳过数据库备份（--no-backup）"
fi

# ── ② 标记当前镜像 ──────────────────────────────────────────
BACKUP_TAG="backup-${TIMESTAMP}"

if docker image inspect "${IMAGE_NAME}:${IMAGE_TAG:-latest}" >/dev/null 2>&1; then
  docker tag "${IMAGE_NAME}:${IMAGE_TAG:-latest}" "${IMAGE_NAME}:${BACKUP_TAG}"
  ok "已标记当前镜像为 ${IMAGE_NAME}:${BACKUP_TAG}（用于回滚）"
else
  warn "当前镜像不存在，跳过标记（可能是首次升级）。"
  BACKUP_TAG=""
fi

# ── ③ 拉取代码 ──────────────────────────────────────────────
if [ "${DO_PULL}" -eq 1 ]; then
  if [ -d .git ]; then
    info "拉取最新代码…"
    git pull --ff-only || fail "git pull 失败，请手动处理后重试。"
    ok "代码已更新"
  else
    warn "当前目录不是 git 仓库，跳过 --pull。"
  fi
fi

# ── ④ 构建并替换 ────────────────────────────────────────────
info "构建新镜像…"
${COMPOSE} build api || fail "镜像构建失败，当前服务未受影响。"
ok "新镜像构建完成"

info "替换 api 容器…"
${COMPOSE} up -d --no-deps api

# ── ⑤ 健康检查与回滚 ────────────────────────────────────────
info "等待服务就绪…"

MAX_WAIT=180
elapsed=0
healthy=0

while [ "${elapsed}" -lt "${MAX_WAIT}" ]; do
  if curl -fsS "${HEALTH_URL}" >/dev/null 2>&1; then
    healthy=1
    break
  fi

  if ! ${COMPOSE} ps --status running --services 2>/dev/null | grep -q '^api$'; then
    break
  fi

  sleep 3
  elapsed=$((elapsed + 3))
  printf '.'
done
echo

if [ "${healthy}" -eq 1 ]; then
  echo
  ok "升级完成 🎉（耗时约 $((elapsed + 3)) 秒）"
  echo
  echo "  接口地址    http://localhost:${API_PORT_VALUE}"
  echo "  本次备份    ${BACKUP_FILE#"${ROOT_DIR}/"}"
  [ -n "${BACKUP_TAG}" ] && echo "  回滚命令    ./deploy/upgrade.sh --rollback"
  echo
  echo "  查看日志    ${COMPOSE} logs -f api"
  echo
  exit 0
fi

# ── 自动回滚 ────────────────────────────────────────────────
echo
warn "新版本未通过健康检查，准备回滚…"
${COMPOSE} logs --tail=80 api || true
echo

if [ -z "${BACKUP_TAG}" ]; then
  fail "没有可回滚的备份镜像，服务处于异常状态，请人工介入。"
fi

info "回滚到 ${IMAGE_NAME}:${BACKUP_TAG}…"
docker tag "${IMAGE_NAME}:${BACKUP_TAG}" "${IMAGE_NAME}:${IMAGE_TAG:-latest}"
${COMPOSE} up -d --no-deps api

sleep 5

if curl -fsS "${HEALTH_URL}" >/dev/null 2>&1; then
  warn "已回滚到上一个可用版本。请排查新版本的日志后再试。"
  echo
  echo "  数据库备份  ${BACKUP_FILE#"${ROOT_DIR}/"}"
  echo "  （数据库未自动恢复 —— 若新版本执行过迁移，需人工确认后再决定是否还原）"
  echo
  exit 1
fi

fail "回滚后服务仍不健康，请人工介入。数据库备份在 ${BACKUP_FILE}"
