# ─────────────────────────────────────────────────────────────
#  美食伴侣 · 一键升级（Windows / PowerShell）
#
#  Linux 服务器请用 deploy/upgrade.sh。
#
#  用法：
#    .\deploy\upgrade.ps1              # 用本地代码升级
#    .\deploy\upgrade.ps1 -Pull        # 先 git pull 再升级
#    .\deploy\upgrade.ps1 -Rollback    # 回滚到最近一次备份镜像
#    .\deploy\upgrade.ps1 -NoBackup    # 跳过数据库备份（不推荐）
# ─────────────────────────────────────────────────────────────
[CmdletBinding()]
param(
    [switch]$Pull,
    [switch]$NoBackup,
    [switch]$Rollback
)

$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = Split-Path -Parent $ScriptDir
Set-Location $RootDir

function Write-Info($msg) { Write-Host "[信息] $msg" -ForegroundColor Cyan }
function Write-Ok($msg)   { Write-Host "[完成] $msg" -ForegroundColor Green }
function Write-Warn($msg) { Write-Host "[注意] $msg" -ForegroundColor Yellow }
function Fail($msg)       { Write-Host "[失败] $msg" -ForegroundColor Red; exit 1 }

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { Fail '未找到 docker。' }
docker info *> $null
if ($LASTEXITCODE -ne 0) { Fail 'docker 守护进程未运行。' }

if (-not (Test-Path '.env')) { Fail '未找到 .env。请先运行 .\deploy\deploy.ps1。' }

$apiPort = 8080
$portLine = Select-String -Path '.env' -Pattern '^API_PORT=(.+)$' -ErrorAction SilentlyContinue
if ($portLine) { $apiPort = $portLine.Matches[0].Groups[1].Value.Trim() }

$healthUrl = "http://localhost:$apiPort/health"
$imageName = 'foodmate-api'
$backupDir = Join-Path $RootDir 'backups'

if (-not (Test-Path $backupDir)) { New-Item -ItemType Directory -Path $backupDir | Out-Null }

function Test-Healthy {
    for ($i = 0; $i -lt 60; $i++) {
        try {
            $r = Invoke-WebRequest $healthUrl -UseBasicParsing -TimeoutSec 5
            if ($r.StatusCode -eq 200) { return $true }
        }
        catch { Start-Sleep -Seconds 3 }
    }
    return $false
}

# ── 回滚模式 ────────────────────────────────────────────────
if ($Rollback) {
    $backupTags = docker images --format '{{.Tag}}' $imageName |
        Where-Object { $_ -like 'backup-*' } | Sort-Object -Descending

    if (-not $backupTags) { Fail "没有找到任何备份镜像（$imageName`:backup-*）。" }

    $target = $backupTags[0]
    Write-Info "回滚到 $imageName`:$target …"

    docker tag "$imageName`:$target" "$imageName`:latest"
    docker compose up -d --no-deps api

    if (Test-Healthy) { Write-Ok '回滚完成，服务已恢复'; exit 0 }

    docker compose logs --tail=50 api
    Fail '回滚后服务仍不健康，请人工介入。'
}

Write-Host ''
Write-Info '开始升级…'
Write-Host ''

$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backupFile = Join-Path $backupDir "foodmate-$timestamp.sql.gz"

# ── ① 数据库备份 ────────────────────────────────────────────
if (-not $NoBackup) {
    Write-Info "备份数据库 → backups\foodmate-$timestamp.sql.gz"

    $running = docker compose ps --status running --services 2>$null
    if ($running -contains 'db') {
        # pg_dump 输出重定向到文件，再压缩
        $rawFile = Join-Path $backupDir "foodmate-$timestamp.sql"
        docker compose exec -T db pg_dump -U foodmate -d foodmate --clean --if-exists |
            Out-File -FilePath $rawFile -Encoding utf8

        if ($LASTEXITCODE -ne 0 -or -not (Test-Path $rawFile)) {
            Fail '数据库备份失败，已中止升级。确认无误后可加 -NoBackup 跳过。'
        }

        # PowerShell 5.1 没有内置 gzip，用 .NET
        $input = [System.IO.File]::OpenRead($rawFile)
        $output = [System.IO.File]::Create($backupFile)
        $gzip = New-Object System.IO.Compression.GZipStream($output, [System.IO.Compression.CompressionMode]::Compress)
        $input.CopyTo($gzip)
        $gzip.Dispose(); $output.Dispose(); $input.Dispose()
        Remove-Item $rawFile -Force

        Write-Ok '备份完成'
    }
    else {
        Write-Warn '数据库容器未运行，跳过备份。'
    }

    # 只保留最近 10 份
    Get-ChildItem $backupDir -Filter 'foodmate-*.sql.gz' |
        Sort-Object LastWriteTime -Descending |
        Select-Object -Skip 10 |
        Remove-Item -Force -ErrorAction SilentlyContinue
}
else {
    Write-Warn '已跳过数据库备份（-NoBackup）'
}

# ── ② 标记当前镜像 ──────────────────────────────────────────
$backupTag = "backup-$timestamp"

docker image inspect "$imageName`:latest" *> $null
if ($LASTEXITCODE -eq 0) {
    docker tag "$imageName`:latest" "$imageName`:$backupTag"
    Write-Ok "已标记当前镜像为 $imageName`:$backupTag（用于回滚）"
}
else {
    Write-Warn '当前镜像不存在，跳过标记。'
    $backupTag = ''
}

# ── ③ 拉取代码 ──────────────────────────────────────────────
if ($Pull) {
    if (Test-Path '.git') {
        Write-Info '拉取最新代码…'
        git pull --ff-only
        if ($LASTEXITCODE -ne 0) { Fail 'git pull 失败，请手动处理后重试。' }
        Write-Ok '代码已更新'
    }
    else {
        Write-Warn '当前目录不是 git 仓库，跳过 -Pull。'
    }
}

# ── ④ 构建并替换 ────────────────────────────────────────────
Write-Info '构建新镜像…'
docker compose build api
if ($LASTEXITCODE -ne 0) { Fail '镜像构建失败，当前服务未受影响。' }
Write-Ok '新镜像构建完成'

Write-Info '替换 api 容器…'
docker compose up -d --no-deps api

# ── ⑤ 健康检查与回滚 ────────────────────────────────────────
Write-Info '等待服务就绪…'

if (Test-Healthy) {
    Write-Host ''
    Write-Ok '升级完成'
    Write-Host ''
    Write-Host "  接口地址    http://localhost:$apiPort"
    Write-Host "  本次备份    backups\foodmate-$timestamp.sql.gz"
    if ($backupTag) { Write-Host '  回滚命令    .\deploy\upgrade.ps1 -Rollback' }
    Write-Host ''
    exit 0
}

Write-Host ''
Write-Warn '新版本未通过健康检查，准备回滚…'
docker compose logs --tail=80 api
Write-Host ''

if (-not $backupTag) { Fail '没有可回滚的备份镜像，请人工介入。' }

Write-Info "回滚到 $imageName`:$backupTag …"
docker tag "$imageName`:$backupTag" "$imageName`:latest"
docker compose up -d --no-deps api

if (Test-Healthy) {
    Write-Warn '已回滚到上一个可用版本。请排查新版本的日志后再试。'
    Write-Host ''
    Write-Host "  数据库备份  backups\foodmate-$timestamp.sql.gz"
    Write-Host '  （数据库未自动恢复 —— 若新版本执行过迁移，需人工确认后再决定是否还原）'
    Write-Host ''
    exit 1
}

Fail "回滚后服务仍不健康，请人工介入。数据库备份在 $backupDir"
