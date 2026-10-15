<#
  Stop all shards + the backend started by run-all.ps1.
  Run:  powershell -ExecutionPolicy Bypass -File scripts\stop-all.ps1
#>

$n = 0

Get-Process sbox-server -ErrorAction SilentlyContinue | ForEach-Object {
  try { $_ | Stop-Process -Force; $n++ } catch {}
}

Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" -ErrorAction SilentlyContinue |
  Where-Object { $_.CommandLine -match 'AnakinShardBackend' } |
  ForEach-Object {
    try { Stop-Process -Id $_.ProcessId -Force; $n++ } catch {}
  }

Write-Host "Stopped $n process(es) (shards + backend)." -ForegroundColor Green
