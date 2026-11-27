$ErrorActionPreference = "Stop"
$path = Join-Path $env:SystemRoot "System32\drivers\etc\hosts"
$raw = [System.IO.File]::ReadAllText($path)
if ($raw -notmatch '(?m)^\s*127\.0\.0\.1\s+onlinetest\.local(\s|$)') {
    if (-not ($raw.EndsWith("`n") -or $raw.EndsWith("`r"))) {
        [System.IO.File]::AppendAllText($path, "`r`n")
    }
    [System.IO.File]::AppendAllText($path, "127.0.0.1 onlinetest.local`r`n")
}
ipconfig /flushdns | Out-Null
