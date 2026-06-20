# RiftReview M9 screenshot capture orchestration
# Captures: champions.png (Champions page, "Currently Practicing" card with Best Build panel)
# Reuses Capturer.exe from .m2shots/Capturer/out/ and the DisableHWAcceleration registry workaround.
# Adapted from .m8shots/run_capture.ps1 -- M9 targets --page champions with --seed-demo.
# No UIAutomation list-item selection needed -- Best Build panel is on the card at top of page.
# Extended async wait (15s) for ChampPoolViewModel.InitializeAsync (DDragon versions+champion+item JSON fetch).

$exe       = "C:\Agent Projects\RiftReview\src\RiftReview.App\bin\Debug\net10.0-windows\RiftReview.App.exe"
$capturer  = "C:\Agent Projects\RiftReview\.m2shots\Capturer\out\Capturer.exe"
$shots     = "C:\Agent Projects\RiftReview\.m9shots"
$procName  = "RiftReview.App"

# Ensure output directory exists
if (-not (Test-Path $shots)) { New-Item -Path $shots -ItemType Directory -Force | Out-Null }

# Registry plumbing (HKCU\Software\Microsoft\Avalon.Graphics DisableHWAcceleration)
$regPath  = "HKCU:\Software\Microsoft\Avalon.Graphics"
$regValue = "DisableHWAcceleration"

$keyExists   = Test-Path $regPath
$prevValue   = $null
$prevExisted = $false
if ($keyExists) {
    try {
        $prevValue   = (Get-ItemProperty -Path $regPath -Name $regValue -ErrorAction Stop).$regValue
        $prevExisted = $true
        Write-Host "Registry: found existing $regValue = $prevValue"
    } catch {
        Write-Host "Registry: key exists but $regValue absent"
    }
}

function Restore-Registry {
    if ($prevExisted) {
        Set-ItemProperty -Path $regPath -Name $regValue -Value $prevValue -Type DWord
        Write-Host "Registry: restored $regValue = $prevValue"
    } else {
        try {
            Remove-ItemProperty -Path $regPath -Name $regValue -ErrorAction Stop
            Write-Host "Registry: removed $regValue (was absent before)"
        } catch {
            Write-Host "Registry: $regValue already absent (clean)"
        }
    }
}

# Set HW accel off (required for WPF GPU-composited content to appear via PrintWindow)
if (-not $keyExists) { New-Item -Path $regPath -Force | Out-Null }
Set-ItemProperty -Path $regPath -Name $regValue -Value 1 -Type DWord
Write-Host "Registry: set $regValue = 1"

function Kill-App {
    Get-Process -Name $procName -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
}

function Wait-ForWindow([int]$timeoutSec = 25) {
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    while ((Get-Date) -lt $deadline) {
        $p = Get-Process -Name $procName -ErrorAction SilentlyContinue |
             Where-Object { $_.MainWindowHandle -ne 0 } |
             Select-Object -First 1
        if ($p) { return $p }
        Start-Sleep -Milliseconds 500
    }
    return $null
}

function Capture-Shot([string]$outFile) {
    $result = & $capturer $procName $outFile 2>&1
    Write-Host $result
    return $LASTEXITCODE -eq 0
}

try {
    # SCENARIO: champions.png -- Champions page with Best Build panel on Currently Practicing card
    Write-Host ""
    Write-Host "=== SCENARIO: champions.png (--seed-demo --page champions) ==="
    Kill-App

    $proc = Start-Process -FilePath $exe -ArgumentList "--seed-demo", "--page", "champions" -PassThru
    Write-Host "Launched PID=$($proc.Id)"

    $w = Wait-ForWindow -timeoutSec 25
    if (-not $w) {
        Write-Host "ERROR: window did not appear within 25s"
        exit 1
    }
    Write-Host "Window up PID=$($w.Id)"

    # Extended wait: ChampPoolViewModel.InitializeAsync fetches DDragon versions.json + champion.json + item.json
    # over the network, then computes Best Build on background thread from seeded timelines.
    # 15s is generous enough for typical internet speeds; if item.json is cached it may be faster.
    Write-Host "Waiting 15s for DDragon async load + Best Build computation..."
    Start-Sleep -Seconds 15

    Write-Host "--- Capturing champions.png ---"
    $ok = Capture-Shot "$shots\champions.png"
    if ($ok) {
        $sz = (Get-Item "$shots\champions.png").Length
        Write-Host "champions.png written, $sz bytes"
    } else {
        Write-Host "WARNING: Capturer returned non-zero for champions.png"
    }

    Kill-App

} finally {
    Kill-App
    Restore-Registry
    Write-Host ""
    Write-Host "Registry cleanup done."
}

Write-Host ""
Write-Host "=== M9 capture complete ==="
Write-Host "PNGs in ${shots}:"
Get-ChildItem $shots -Filter "*.png" -ErrorAction SilentlyContinue | Select-Object Name, Length
