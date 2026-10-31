# Draws the painted world-art sheets listed in manifest.json with the image model (OpenAI Images API).
# Key: OPENAI_API_KEY env var, or the game's openai_key.txt. Existing sheets are skipped; pass -Only <file> to redraw one.
param([string]$Only = '', [string]$Quality = 'medium', [string]$Model = 'gpt-image-2.5-flare')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$out = Join-Path $root 'sheets'
New-Item -ItemType Directory -Force $out | Out-Null
$key = $env:OPENAI_API_KEY
if (-not $key) { $key = Get-Content -Raw "$env:USERPROFILE\AppData\LocalLow\DefaultCompany\Diverse\openai_key.txt" -ErrorAction SilentlyContinue }
if (-not $key -or $key.Trim().Length -lt 10) { Write-Output 'NO_KEY'; exit 2 }
$key = $key.Trim()
$m = Get-Content -Raw -Encoding UTF8 (Join-Path $root 'manifest.json') | ConvertFrom-Json

$jobs = foreach ($s in $m.sheets) {
  if ($Only -and $s.file -ne $Only) { continue }
  $path = Join-Path $out ($s.file + '.png')
  if (-not $Only -and (Test-Path $path)) { continue }
  if ($s.kind -eq 'ground') { $prompt = "$($m.groundStyle) $($s.prompt)"; $size = '1024x1024'; $bg = 'opaque' }
  else { $prompt = "$($m.style) $($m.sheetStyle.Replace('{cols}', $s.cols).Replace('{rows}', $s.rows)) $($s.prompt)"; $size = '1536x1024'; $bg = 'transparent' }
  @{ file = $s.file; path = $path; body = (@{ model = $Model; prompt = $prompt; size = $size; quality = $Quality; background = $bg; output_format = 'png'; n = 1 } | ConvertTo-Json -Compress) }
}

# A few requests at a time (each takes about a minute)
$running = @()
foreach ($j in $jobs) {
  while (($running | Where-Object { $_.State -eq 'Running' }).Count -ge 4) { Start-Sleep -Milliseconds 500 }
  $running += Start-Job -ArgumentList $j.file, $j.path, $j.body, $key -ScriptBlock {
    param($file, $path, $body, $key)
    try {
      $r = Invoke-RestMethod -Uri 'https://api.openai.com/v1/images/generations' -Method Post -Headers @{ Authorization = "Bearer $key" } -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body)) -TimeoutSec 400
      [IO.File]::WriteAllBytes($path, [Convert]::FromBase64String($r.data[0].b64_json))
      "OK $file"
    } catch { "FAIL ${file}: $($_.Exception.Message) $($_.ErrorDetails.Message)" }
  }
}
$running | Wait-Job | Receive-Job
$running | Remove-Job
