$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$sample = Join-Path $root 'sample-512-80.gif'
if (!(Test-Path -LiteralPath $sample)) {
    $sample = Join-Path $root 'source\bin\Release\sample-512-80.gif'
}
if (!(Test-Path -LiteralPath $sample)) {
    throw 'Place a GIF sample at sample-512-80.gif next to this project folder before running this smoke test.'
}

$exe = Join-Path $root 'source\bin\Release\GIF Viewer.exe'
$workDir = Split-Path -Parent $exe

$p = Start-Process -FilePath $exe `
    -ArgumentList ('"' + $sample + '"') `
    -WorkingDirectory $workDir `
    -PassThru

$sawHelper = $false
for ($i = 0; $i -lt 80; $i++) {
    if (Get-Process webp_extract_pillow -ErrorAction SilentlyContinue) {
        $sawHelper = $true
    }
    Start-Sleep -Milliseconds 250
}

$p.Refresh()
'SAMPLE=' + $sample
'RUNNING=' + ($p.HasExited -eq $false)
'SAW_HELPER=' + $sawHelper

if (-not $p.HasExited) {
    $p.CloseMainWindow() | Out-Null
    Start-Sleep -Seconds 2
    if (-not $p.HasExited) {
        $p.Kill()
    }
}

Get-Process 'GIF Viewer',webp_extract_pillow -ErrorAction SilentlyContinue |
    Select-Object ProcessName,Id,CPU,Path