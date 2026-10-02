# ─────────────────────────────────────────────────────────────
#  美食伴侣 · 一键部署（Windows / PowerShell）
#
#  Linux 服务器请用 deploy/deploy.sh —— 那个功能更完整。
#  本脚本用于在 Windows + Docker Desktop 上本地跑起整套服务。
#
#  用法：
#    .\deploy\deploy.ps1
#    .\deploy\deploy.ps1 -NoBuild
#    .\deploy\deploy.ps1 -Logs
# ─────────────────────────────────────────────────────────────
[CmdletBinding()]
param(
    [switch]$NoBuild,
    [switch]$Logs
)

$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = Split-Path -Parent $ScriptDir
Set-Location $RootDir

function Write-Info($msg) { Write-Host "[信息] $msg" -ForegroundColor Cyan }
function Write-Ok($msg)   { Write-Host "[完成] $msg" -ForegroundColor Green }
function Write-Warn($msg) { Write-Host "[注意] $msg" -ForegroundColor Yellow }
function Fail($msg)       { Write-Host "[失败] $msg" -ForegroundColor Red; exit 1 }

# ── 环境检查 ────────────────────────────────────────────────
Write-Info '检查环境…'

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    Fail '未找到 docker。请先安装 Docker Desktop：https://www.docker.com/products/docker-desktop/'
}

docker info *> $null
if ($LASTEXITCODE -ne 0) {
    Fail 'docker 守护进程未运行。请先启动 Docker Desktop。'
}

docker compose version *> $null
if ($LASTEXITCODE -ne 0) {
    Fail '未找到 docker compose v2。请升级 Docker Desktop。'
}

Write-Ok 'docker 环境正常'

# ── 生成配置 ────────────────────────────────────────────────
function New-Secret([int]$bytes = 48) {
    $buffer = New-Object byte[] $bytes
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($buffer)
    return [Convert]::ToBase64String($buffer)
}

if (-not (Test-Path '.env')) {
    Write-Info '未找到 .env，正在从模板生成并填入随机密钥…'

    if (-not (Test-Path '.env.example')) { Fail '缺少 .env.example，无法生成配置。' }

    $content = Get-Content '.env.example' -Raw
    $content = $content -replace '(?m)^POSTGRES_PASSWORD=.*$', "POSTGRES_PASSWORD=$(New-Secret 24)"
    $content = $content -replace '(?m)^JWT_SIGNING_KEY=.*$',   "JWT_SIGNING_KEY=$(New-Secret 48)"

    # 用 UTF-8 无 BOM 写入：带 BOM 会让 docker compose 解析 .env 时出错
    [System.IO.File]::WriteAllText(
        (Join-Path $RootDir '.env'),
        $content,
        (New-Object System.Text.UTF8Encoding($false)))

    Write-Ok '已生成 .env（数据库密码与 JWT 密钥均为随机生成）'
    Write-Warn '请检查 .env 里的三端凭据与 AI 配置 —— 至少配置一个平台才能登录。'
}
else {
    Write-Info '已存在 .env，沿用现有配置。'
}

# ── 构建 ────────────────────────────────────────────────────
if (-not $NoBuild) {
    Write-Info '构建镜像（首次构建需要几分钟）…'
    docker compose build api
    if ($LASTEXITCODE -ne 0) { Fail '镜像构建失败。' }
    Write-Ok '镜像构建完成'
}
else {
    Write-Info '跳过构建（-NoBuild）'
}

# ── 启动并等待健康 ──────────────────────────────────────────
Write-Info '启动服务…'
docker compose up -d --remove-orphans
if ($LASTEXITCODE -ne 0) { Fail '启动失败，请查看上面的输出。' }

# 从 .env 读端口
$apiPort = 8080
$portLine = Select-String -Path '.env' -Pattern '^API_PORT=(.+)$' -ErrorAction SilentlyContinue
if ($portLine) { $apiPort = $portLine.Matches[0].Groups[1].Value.Trim() }

$healthUrl = "http://localhost:$apiPort/health"

Write-Info '等待服务就绪…'

$deadline = (Get-Date).AddSeconds(180)
$ready = $false

while ((Get-Date) -lt $deadline) {
    try {
        $response = Invoke-WebRequest $healthUrl -UseBasicParsing -TimeoutSec 5
        if ($response.StatusCode -eq 200) { $ready = $true; break }
    }
    catch {
        Start-Sleep -Seconds 3
        Write-Host '.' -NoNewline
    }
}
Write-Host ''

if (-not $ready) {
    docker compose logs --tail=50 api
    Fail "等待 180 秒后服务仍未就绪。"
}

Write-Ok '部署完成'
Write-Host ''
Write-Host "  接口地址    http://localhost:$apiPort"
Write-Host "  健康检查    $healthUrl"
Write-Host "  就绪检查    http://localhost:$apiPort/health/ready"
Write-Host ''
Write-Host '  查看日志    docker compose logs -f api'
Write-Host '  停止服务    docker compose down'
Write-Host '  一键升级    .\deploy\upgrade.ps1'
Write-Host ''

if ($Logs) {
    Write-Info '跟随日志（Ctrl+C 退出，不会停止服务）…'
    docker compose logs -f api
}
