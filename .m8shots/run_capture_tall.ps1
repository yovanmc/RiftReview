# RiftReview M8 tall screenshot capture
# Resizes the window to ~1120x1500 via UIAutomation TransformPattern before capture
# so the gold-diff chart (* row) comes into view.
# Writes: .m8shots/deepdive_tall.png
# Restores DisableHWAcceleration registry key to its prior state after capture.

$exe       = "C:\Agent Projects\RiftReview\src\RiftReview.App\bin\Debug\net10.0-windows\RiftReview.App.exe"
$capturer  = "C:\Agent Projects\RiftReview\.m2shots\Capturer\out\Capturer.exe"
$shots     = "C:\Agent Projects\RiftReview\.m8shots"
$procName  = "RiftReview.App"
$outFile   = "$shots\deepdive_tall.png"

if (-not (Test-Path $shots)) { New-Item -Path $shots -ItemType Directory -Force | Out-Null }

# Registry plumbing
$regPath  = "HKCU:\Software\Microsoft\Avalon.Graphics"
$regValue = "DisableHWAcceleration"
$prevExisted = $false
$prevValue   = $null
if (Test-Path $regPath) {
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

if (-not (Test-Path $regPath)) { New-Item -Path $regPath -Force | Out-Null }
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

try {
    Kill-App

    $proc = Start-Process -FilePath $exe -ArgumentList "--seed-demo", "--page", "review" -PassThru
    Write-Host "Launched PID=$($proc.Id)"

    $w = Wait-ForWindow -timeoutSec 25
    if (-not $w) {
        Write-Host "ERROR: window did not appear within 25s"
        exit 1
    }
    Write-Host "Window up PID=$($w.Id)"
    Write-Host "Waiting 6s for Review page init + match list to populate..."
    Start-Sleep -Seconds 6

    # Load UIAutomation assemblies
    [Reflection.Assembly]::LoadWithPartialName('UIAutomationClient') | Out-Null
    [Reflection.Assembly]::LoadWithPartialName('UIAutomationTypes') | Out-Null

    $root   = [System.Windows.Automation.AutomationElement]::RootElement
    $pidCond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $proc.Id)
    $appWin = $root.FindFirst(
        [System.Windows.Automation.TreeScope]::Children, $pidCond)

    if ($null -eq $appWin) {
        Write-Host "UIA: PID search failed, trying name search..."
        $condName = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, "RiftReview")
        $appWin = $root.FindFirst(
            [System.Windows.Automation.TreeScope]::Children, $condName)
    }

    if ($null -eq $appWin) {
        Write-Host "ERROR: could not find app window via UIAutomation"
        exit 1
    }
    Write-Host "UIA: found app window"

    # --- Step 1: select first match to load DeepDive ---
    $drillOk = $false
    $condListItem = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::ListItem)
    $items = $appWin.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants, $condListItem)
    Write-Host "UIA: found $($items.Count) ListItem elements"

    if ($items.Count -gt 0) {
        $firstItem = $items[0]
        Write-Host "UIA: First ListItem name='$($firstItem.Current.Name)'"
        try {
            $selPat = $firstItem.GetCurrentPattern(
                [System.Windows.Automation.SelectionItemPattern]::Pattern)
            $selPat.Select()
            Write-Host "UIA: SelectionItemPattern.Select() invoked"
            $drillOk = $true
        } catch {
            Write-Host "UIA: SelectionItemPattern failed: $_"
            try {
                $invPat = $firstItem.GetCurrentPattern(
                    [System.Windows.Automation.InvokePattern]::Pattern)
                $invPat.Invoke()
                Write-Host "UIA: InvokePattern.Invoke() invoked (fallback)"
                $drillOk = $true
            } catch {
                Write-Host "UIA: InvokePattern also failed: $_"
            }
        }
    }

    if ($drillOk) {
        Write-Host "Waiting 4s for DeepDive.Load() to complete and chart to render..."
        Start-Sleep -Seconds 4
    } else {
        Write-Host "WARNING: UIAutomation drill failed -- DeepDiveView will show empty-state"
    }

    # --- Step 2: resize window to 1120x1500 via TransformPattern ---
    $resizeOk = $false
    try {
        $transformPat = $appWin.GetCurrentPattern(
            [System.Windows.Automation.TransformPattern]::Pattern)
        if ($null -ne $transformPat) {
            if ($transformPat.Current.CanResize) {
                # Get current position so we only change the size, not the location
                $rect = $appWin.Current.BoundingRectangle
                Write-Host "UIA: current bounds = $($rect.Left),$($rect.Top) $($rect.Width)x$($rect.Height)"
                $transformPat.Resize(1120, 1500)
                Write-Host "UIA: TransformPattern.Resize(1120, 1500) invoked"
                $resizeOk = $true
            } else {
                Write-Host "UIA: TransformPattern present but CanResize=false"
            }
        } else {
            Write-Host "UIA: TransformPattern not available"
        }
    } catch {
        Write-Host "UIA: TransformPattern resize failed: $_"
    }

    # Fallback: try WindowPattern to set window state then use Win32 SetWindowPos via P/Invoke
    if (-not $resizeOk) {
        Write-Host "Fallback: attempting Win32 SetWindowPos via Add-Type..."
        try {
            Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public class Win32 {
    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
}
"@ -ErrorAction Stop
            $hwnd = [IntPtr]$proc.MainWindowHandle
            $flags = [Win32]::SWP_NOMOVE -bor [Win32]::SWP_NOZORDER -bor [Win32]::SWP_NOACTIVATE
            $result = [Win32]::SetWindowPos($hwnd, [IntPtr]::Zero, 0, 0, 1120, 1500, $flags)
            Write-Host "Win32 SetWindowPos result: $result"
            $resizeOk = $result
        } catch {
            Write-Host "Win32 SetWindowPos failed: $_"
        }
    }

    if ($resizeOk) {
        Write-Host "Waiting 2s for layout to reflow after resize..."
        Start-Sleep -Seconds 2
    } else {
        Write-Host "WARNING: could not resize window -- gold-diff chart may still be off-screen"
    }

    # --- Step 3: capture ---
    Write-Host "--- Capturing deepdive_tall.png ---"
    $captureResult = & $capturer $procName $outFile 2>&1
    Write-Host $captureResult
    if ($LASTEXITCODE -eq 0) {
        $sz = (Get-Item $outFile).Length
        Write-Host "deepdive_tall.png written, $sz bytes"
    } else {
        Write-Host "WARNING: Capturer returned non-zero exit code"
    }

    Kill-App

} finally {
    Kill-App
    Restore-Registry
    Write-Host ""
    Write-Host "Registry cleanup done."
}

Write-Host ""
Write-Host "=== M8 tall capture complete ==="
Get-ChildItem $shots -Filter "*.png" -ErrorAction SilentlyContinue | Select-Object Name, Length
