# ─────────────────────────────────────────────────────────────
#  美食伴侣 · 本地开发一键启动
#
#  为什么需要这个脚本：
#    在 AI 助手（DSH）里启动的后端进程会被它的进程管理回收 ——
#    命令一结束，后端就跟着没了，前端就会显示「连不上后端」。
#    所以开发服务器**必须在你自己的终端里启动**。
#
#  用法：
#    在项目根目录打开 PowerShell，执行：
#      .\start-dev.ps1
#
#    然后按 Ctrl+C 停止（会同时停掉前后端）。
# ─────────────────────────────────────────────────────────────

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

function Write-Step($msg) { Write-Host "`n▸ $msg" -ForegroundColor Cyan }
function Write-Ok($msg)   { Write-Host "  ✓ $msg" -ForegroundColor Green }
function Write-Warn2($msg) { Write-Host "  ! $msg" -ForegroundColor Yellow }

# ── 前置检查 ────────────────────────────────────────────────
Write-Step '检查环境'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host '  ✗ 未找到 dotnet SDK' -ForegroundColor Red; exit 1
}
if (-not (Test-Path 'web/node_modules')) {
    Write-Host '  ✗ web/node_modules 不存在，请先执行：cd web; npm install' -ForegroundColor Red; exit 1
}

Write-Ok "dotnet $(dotnet --version)"
Write-Ok 'web/node_modules 已就绪'

# ── 清掉旧进程 ──────────────────────────────────────────────
Write-Step '清理旧进程'

foreach ($port in 5199, 5173) {
    $conn = Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue |
        Select-Object -First 1

    if ($conn) {
        Write-Warn2 "端口 $port 被 PID $($conn.OwningProcess) 占用，正在结束"
        Stop-Process -Id $conn.OwningProcess -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 600
    }
}

Write-Ok '端口 5199 / 5173 已释放'

# ── 启动后端 ────────────────────────────────────────────────
Write-Step '启动后端 (http://localhost:5199)'

$env:ASPNETCORE_ENVIRONMENT = 'Development'

$api = Start-Process -FilePath 'dotnet' `
    -ArgumentList @('run', '--project', 'src/FoodMate.Api', '--urls', 'http://localhost:5199') `
    -WorkingDirectory $PSScriptRoot `
    -PassThru -NoNewWindow

# 等它就绪（最多 60 秒）
$ready = $false
for ($i = 0; $i -lt 30; $i++) {
    Start-Sleep -Seconds 2
    try {
        $r = Invoke-RestMethod 'http://localhost:5199/health/ready' -TimeoutSec 4
        Write-Ok "后端就绪 · 菜品 $($r.checks.dishCount) 道"
        $ready = $true
        break
    } catch {
        Write-Host '.' -NoNewline
    }
}

if (-not $ready) {
    Write-Host ''
    Write-Host '  ✗ 后端 60 秒内未就绪，请检查上面的输出' -ForegroundColor Red
    if (-not $api.HasExited) { Stop-Process -Id $api.Id -Force }
    exit 1
}

# ── 启动前端 ────────────────────────────────────────────────
Write-Step '启动前端 (http://localhost:5173)'

$web = Start-Process -FilePath 'npm.cmd' `
    -ArgumentList @('run', 'dev:h5') `
    -WorkingDirectory (Join-Path $PSScriptRoot 'web') `
    -PassThru -NoNewWindow

Write-Host ''
Write-Host '  ┌──────────────────────────────────────────────┐' -ForegroundColor Cyan
Write-Host '  │  前端  http://localhost:5173                 │' -ForegroundColor Cyan
Write-Host '  │  后端  http://localhost:5199                 │' -ForegroundColor Cyan
Write-Host '  │                                              │' -ForegroundColor Cyan
Write-Host '  │  按 Ctrl+C 停止（会同时停掉前后端）           │' -ForegroundColor Cyan
Write-Host '  └──────────────────────────────────────────────┘' -ForegroundColor Cyan
Write-Host ''

# ── 守着，Ctrl+C 时一起收掉 ─────────────────────────────────
try {
    while ($true) {
        Start-Sleep -Seconds 2

        if ($api.HasExited) {
            Write-Host '  ! 后端进程已退出' -ForegroundColor Yellow
            break
        }
        if ($web.HasExited) {
            Write-Host '  ! 前端进程已退出' -ForegroundColor Yellow
            break
        }
    }
} finally {
    Write-Host "`n正在停止服务…" -ForegroundColor Cyan
    foreach ($p in @($api, $web)) {
        if ($p -and -not $p.HasExited) {
            Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
        }
    }
    Write-Host '已停止。' -ForegroundColor Green
}
