<#
  Backend smoke test: exercises the transfer state machine over HTTP with real HMAC
  envelopes. Proves the backend side of the transfer matrix (happy path, exact-once toll,
  replay no-op, destination full, destination down, token expiry+revert, bad signature)
  without s&box. Starts the backend (if not already up), runs assertions, stops it.

  Run:  powershell -ExecutionPolicy Bypass -File backend\smoke-test.ps1
#>

$ErrorActionPreference = 'Stop'
$base   = 'http://localhost:8443'
$secret = 'fb1976e069994f08a3f0725eb5ba9051ecd5159ab74cce1f2cdf6cad56bfa45d'
$here   = Split-Path -Parent $MyInvocation.MyCommand.Path
$dll    = Join-Path $here 'bin\Debug\net10.0\AnakinShardBackend.dll'

$script:fails = 0
function Check($name, $cond) {
  if ($cond) { Write-Host "  PASS  $name" -ForegroundColor Green }
  else       { Write-Host "  FAIL  $name" -ForegroundColor Red; $script:fails++ }
}

function Sign($payload) {
  $h = [System.Security.Cryptography.HMACSHA256]::new([Text.Encoding]::UTF8.GetBytes($secret))
  ($h.ComputeHash([Text.Encoding]::UTF8.GetBytes($payload)) | ForEach-Object { $_.ToString('x2') }) -join ''
}
function Call($path, $obj, [string]$badSig) {
  $payload = ($obj | ConvertTo-Json -Compress)
  $sig = if ($badSig) { $badSig } else { Sign $payload }
  $env = @{ payload = $payload; sig = $sig } | ConvertTo-Json -Compress
  try {
    Invoke-RestMethod -Uri "$base$path" -Method Post -Body $env -ContentType 'application/json'
  } catch {
    # surface HTTP status for negative tests
    [pscustomobject]@{ __status = $_.Exception.Response.StatusCode.value__ }
  }
}

# --- start backend if needed ---
$started = $false
try { Invoke-RestMethod "$base/health" -TimeoutSec 2 | Out-Null; Write-Host "Using already-running backend." }
catch {
  if (-not (Test-Path $dll)) { throw "backend not built. Run: dotnet build backend\AnakinShardBackend.csproj" }
  Write-Host "Starting backend..."
  # fresh state for a clean run
  $dataFile = Join-Path $here 'data\state.json'
  if (Test-Path $dataFile) { Remove-Item $dataFile -Force }
  $proc = Start-Process dotnet -ArgumentList "`"$dll`"" -WorkingDirectory $here -PassThru -WindowStyle Hidden
  $started = $true
  $ok = $false
  foreach ($i in 1..30) {
    Start-Sleep -Milliseconds 400
    try { Invoke-RestMethod "$base/health" -TimeoutSec 2 | Out-Null; $ok = $true; break } catch {}
  }
  if (-not $ok) { throw "backend did not come up" }
}

Write-Host "`n=== Backend smoke test ===" -ForegroundColor Cyan
$P = '76561190000000001'   # fake steamId64

# heartbeats: A and B up with capacity 2
Call '/directory/heartbeat' @{ ShardId='A'; ConnectHandle='lobbyA'; Players=1; Capacity=2; Version='1' } | Out-Null
Call '/directory/heartbeat' @{ ShardId='B'; ConnectHandle='lobbyB'; Players=0; Capacity=2; Version='1' } | Out-Null

# join A
$j = Call '/player/join' @{ SteamId=$P; ShardId='A' }
Check "join A -> currentShard=A"      ($j.currentShard -eq 'A')
Check "new player money=500"          ($j.money -eq 500)
Check "new player carries briefcase"  ($j.carriedItem -eq 'briefcase')

# lookup
$lk = Call '/directory/lookup' @{ ShardId='B' }
Check "lookup B up + handle"          ($lk.up -eq $true -and $lk.connectHandle -eq 'lobbyB')

# --- happy path A->B (row 1) ---
$T1 = 'A:t1'
$pr = Call '/transfer/prepare' @{ SteamId=$P; SrcShard='A'; DstShard='B'; TransferId=$T1 }
Check "prepare A->B ok"               ($pr.ok -eq $true -and $pr.dstConnectHandle -eq 'lobbyB')
$ac = Call '/transfer/accept' @{ SteamId=$P; DstShard='B' }
Check "accept on B ok"                ($ac.ok -eq $true)
Check "arrived money=500 (row1/2)"    ($ac.snapshot.money -eq 500)
Check "arrived item carried"          ($ac.snapshot.carriedItem -eq 'briefcase')
Check "arrived currentShard=B"        ($ac.snapshot.currentShard -eq 'B')

# --- replay accept is a no-op (row 12) ---
$rp = Call '/transfer/accept' @{ SteamId=$P; DstShard='B' }
Check "replay accept no-op, money=500 (row12)" ($rp.ok -eq $true -and $rp.snapshot.money -eq 500)

# --- toll: exact-once deduction (rows 2/3) ---
$T2 = 'B:t2'
$l1 = Call '/player/apply-ledger' @{ SteamId=$P; TransferId=$T2; Delta=-100; Reason='toll' }
Check "toll deduct -> 400"            ($l1.ok -eq $true -and $l1.balance -eq 400)
$l2 = Call '/player/apply-ledger' @{ SteamId=$P; TransferId=$T2; Delta=-100; Reason='toll' }
Check "toll re-apply deduped -> 400"  ($l2.balance -eq 400)
$pr2 = Call '/transfer/prepare' @{ SteamId=$P; SrcShard='B'; DstShard='A'; TransferId=$T2 }
$ac2 = Call '/transfer/accept' @{ SteamId=$P; DstShard='A' }
Check "after toll+transfer money=400" ($ac2.snapshot.money -eq 400)

# --- toll blocked: insufficient funds (row 4) ---
$blk = Call '/player/apply-ledger' @{ SteamId=$P; TransferId='B:big'; Delta=-100000; Reason='toll' }
Check "insufficient toll denied, money intact" ($blk.ok -eq $false -and $blk.balance -eq 400)

# --- destination full (row 9) ---
Call '/directory/heartbeat' @{ ShardId='B'; ConnectHandle='lobbyB'; Players=2; Capacity=2; Version='1' } | Out-Null
$full = Call '/transfer/prepare' @{ SteamId=$P; SrcShard='A'; DstShard='B'; TransferId='A:full' }
Check "prepare to full B denied (row9)" ($full.ok -eq $false -and $full.reason -match 'full')

# --- destination down (row 10): C never heartbeats ---
$down = Call '/transfer/prepare' @{ SteamId=$P; SrcShard='A'; DstShard='C'; TransferId='A:down' }
Check "prepare to down C denied (row10)" ($down.ok -eq $false -and $down.reason -match 'down')

# --- token expiry + revert (row 11) ---
Call '/directory/heartbeat' @{ ShardId='B'; ConnectHandle='lobbyB'; Players=0; Capacity=2; Version='1' } | Out-Null
$T4 = 'A:t4'
Call '/transfer/prepare' @{ SteamId=$P; SrcShard='A'; DstShard='B'; TransferId=$T4 } | Out-Null
Call '/debug/force-expire' @{ SteamId=$P } | Out-Null
$exp = Call '/transfer/accept' @{ SteamId=$P; DstShard='B' }
Check "expired accept rejected (row11)"  ($exp.ok -eq $false -and $exp.reason -match 'expired')
$after = Call '/player/get' @{ SteamId=$P }
Check "after expiry stays on A"          ($after.currentShard -eq 'A')
Check "after expiry InTransit cleared"   ([string]::IsNullOrEmpty($after.inTransit))
Check "after expiry money intact=400"    ($after.money -eq 400)

# --- cancel reverts InTransit ---
Call '/transfer/prepare' @{ SteamId=$P; SrcShard='A'; DstShard='B'; TransferId='A:t5' } | Out-Null
$cn = Call '/transfer/cancel' @{ SteamId=$P; TransferId='A:t5' }
Check "cancel ok"                        ($cn.ok -eq $true)
$ag = Call '/player/get' @{ SteamId=$P }
Check "cancel cleared InTransit"         ([string]::IsNullOrEmpty($ag.inTransit))

# --- bad signature rejected (401) ---
$bad = Call '/player/get' @{ SteamId=$P } 'deadbeef'
Check "bad signature -> 401"             ($bad.__status -eq 401)

Write-Host "`n=== $(if ($script:fails -eq 0) {'ALL PASS'} else {"$script:fails FAILED"}) ===" -ForegroundColor $(if ($script:fails -eq 0){'Green'}else{'Red'})

if ($started) { Stop-Process -Id $proc.Id -Force; Write-Host "Backend stopped." }
exit $script:fails
