$ErrorActionPreference = 'Continue'
$repo = 'C:\Work\美食伴侣'
$qa   = Join-Path $repo '.dsh-team\qa'

function Stop-Api {
    $conns = Get-NetTCPConnection -State Listen -LocalPort 5199 -ErrorAction SilentlyContinue
    if ($conns) {
        foreach ($c in $conns) {
            Write-Output "  killing PID $($c.OwningProcess)"
            Stop-Process -Id $c.OwningProcess -Force -ErrorAction SilentlyContinue
        }
        Start-Sleep -Seconds 3
    } else {
        Write-Output "  no listener on 5199"
    }
}

function Start-Api($tag) {
    $log = Join-Path $qa "api-$tag.log"
    if (Test-Path $log) { Remove-Item $log -Force }
    $p = Start-Process -FilePath 'dotnet' `
        -ArgumentList 'run','--project','src/FoodMate.Api','--no-build','--urls','http://localhost:5199' `
        -WorkingDirectory $repo -RedirectStandardOutput $log -RedirectStandardError "$log.err" `
        -PassThru -WindowStyle Hidden
    Write-Output "  started dotnet run PID=$($p.Id), log=$log"
    return $p
}

function Wait-Ready($timeoutSec) {
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    while ((Get-Date) -lt $deadline) {
        try {
            $r = Invoke-RestMethod -Uri 'http://localhost:5199/health/ready' -TimeoutSec 5
            return $r
        } catch { Start-Sleep -Seconds 2 }
    }
    return $null
}

foreach ($round in 1, 2) {
    Write-Output "=========== ROUND $round ==========="
    Stop-Api
    $proc = Start-Api "round$round"
    $r = Wait-Ready 90
    if ($null -eq $r) {
        Write-Output "  *** /health/ready never became ready in 90s"
    } else {
        Write-Output "  status=$($r.status) database=$($r.checks.database) dishCount=$($r.checks.dishCount)"
        $api = Invoke-RestMethod -Uri 'http://localhost:5199/api/v1/dishes?page=1&pageSize=1' -Headers @{'X-Device-Id'='qa'} -TimeoutSec 30
        Write-Output "  api data.total=$($api.data.total)"
    }
    Write-Output "  --- seed log lines ---"
    $lg = Join-Path $qa "api-round$round.log"
    if (Test-Path $lg) {
        Select-String -Path $lg -Pattern '菜品|种子|导入|重建|Database' -Encoding UTF8 |
            Select-Object -First 12 | ForEach-Object { "    $($_.Line.Trim())" }
    }
}
Write-Output "DONE"
