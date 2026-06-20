# RiftReview M8 screenshot capture orchestration
# Captures: deepdive.png (Review page, first match selected via UIAutomation)
# Reuses Capturer.exe from .m2shots/Capturer/out/ and the DisableHWAcceleration registry workaround.
# Adapted from .m7shots/run_capture.ps1 -- M8 only needs the deep-dive capture.

$exe       = "C:\Agent Projects\RiftReview\src\RiftReview.App\bin\Debug\net10.0-windows\RiftReview.App.exe"
$capturer  = "C:\Agent Projects\RiftReview\.m2shots\Capturer\out\Capturer.exe"
$shots     = "C:\Agent Projects\RiftReview\.m8shots"
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
    # SCENARIO: deepdive.png -- Review page, first match in demo list selected
    # DeepDiveView is embedded inside ReviewView (right-panel Row=1)
    # Selecting a ListBoxItem fires OnSelectedMatchChanged -> DeepDive.Load()
    # Demo has 24 Ahri games each with stored match+timeline JSON
    # UIAutomation: find app window -> find first ListItem -> SelectionItemPattern.Select()
    Write-Host ""
    Write-Host "=== SCENARIO: deepdive.png (--seed-demo --page review + UIAutomation select) ==="
    Kill-App

    $p2 = Start-Process -FilePath $exe -ArgumentList "--seed-demo", "--page", "review" -PassThru
    Write-Host "Launched PID=$($p2.Id)"

    $w2 = Wait-ForWindow -timeoutSec 25
    if (-not $w2) {
        Write-Host "ERROR: window did not appear within 25s"
    } else {
        Write-Host "Window up PID=$($w2.Id)"
        Write-Host "Waiting 6s for Review page init + match list to populate..."
        Start-Sleep -Seconds 6

        # UIAutomation: select first match in list to trigger DeepDive.Load()
        $drillOk = $false
        try {
            [Reflection.Assembly]::LoadWithPartialName('UIAutomationClient') | Out-Null
            [Reflection.Assembly]::LoadWithPartialName('UIAutomationTypes') | Out-Null

            $root = [System.Windows.Automation.AutomationElement]::RootElement

            # Find the RiftReview app window by process ID (more reliable than name search)
            $pidCond = New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $p2.Id)
            $appWin = $root.FindFirst(
                [System.Windows.Automation.TreeScope]::Children, $pidCond)

            if ($null -eq $appWin) {
                # Fallback: find by window title
                Write-Host "DRILL: PID search failed, trying name search..."
                $condName = New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::NameProperty, "RiftReview")
                $appWin = $root.FindFirst(
                    [System.Windows.Automation.TreeScope]::Children, $condName)
            }

            if ($null -eq $appWin) {
                Write-Host "DRILL: Could not find app window via UIAutomation"
            } else {
                Write-Host "DRILL: Found app window"

                # Find all ListItem elements (WPF ListBoxItem maps to ControlType.ListItem)
                $condListItem = New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                    [System.Windows.Automation.ControlType]::ListItem)
                $items = $appWin.FindAll(
                    [System.Windows.Automation.TreeScope]::Descendants, $condListItem)

                Write-Host "DRILL: Found $($items.Count) ListItem elements"

                $firstItem = $null
                if ($items.Count -gt 0) { $firstItem = $items[0] }

                if ($null -ne $firstItem) {
                    $itemName = $firstItem.Current.Name
                    Write-Host "DRILL: First ListItem name='$itemName'"

                    # Try SelectionItemPattern (preferred for ListBoxItem selection)
                    try {
                        $selPat = $firstItem.GetCurrentPattern(
                            [System.Windows.Automation.SelectionItemPattern]::Pattern)
                        if ($null -ne $selPat) {
                            $selPat.Select()
                            Write-Host "DRILL: SelectionItemPattern.Select() invoked"
                            $drillOk = $true
                        }
                    } catch {
                        Write-Host "DRILL: SelectionItemPattern failed: $_"
                    }

                    # Fallback: InvokePattern
                    if (-not $drillOk) {
                        try {
                            $invPat = $firstItem.GetCurrentPattern(
                                [System.Windows.Automation.InvokePattern]::Pattern)
                            if ($null -ne $invPat) {
                                $invPat.Invoke()
                                Write-Host "DRILL: InvokePattern.Invoke() invoked (fallback)"
                                $drillOk = $true
                            }
                        } catch {
                            Write-Host "DRILL: InvokePattern also failed: $_"
                        }
                    }
                } else {
                    Write-Host "DRILL: No ListItem found -- match list may be empty or not yet loaded"
                }
            }
        } catch {
            Write-Host "DRILL: UIAutomation exception: $_"
        }

        if ($drillOk) {
            Write-Host "Waiting 4s for DeepDive.Load() to complete and chart to render..."
            Start-Sleep -Seconds 4
        } else {
            Write-Host "WARNING: UIAutomation drill failed -- will capture Review page without match selected"
            Write-Host "(DeepDiveView will show empty-state)"
        }

        Write-Host "--- Capturing deepdive.png ---"
        $ok2 = Capture-Shot "$shots\deepdive.png"
        if ($ok2) {
            $sz2 = (Get-Item "$shots\deepdive.png").Length
            Write-Host "deepdive.png written, $sz2 bytes"
        } else {
            Write-Host "WARNING: Capturer returned non-zero for deepdive.png"
        }

        if ($drillOk) {
            Write-Host "DRILL result: SELECTED_VIA_UIAUTOMATION"
        } else {
            Write-Host "DRILL result: NOT_AUTOMATABLE_empty_state_captured"
        }
    }

    Kill-App

} finally {
    Kill-App
    Restore-Registry
    Write-Host ""
    Write-Host "Registry cleanup done."
}

Write-Host ""
Write-Host "=== M8 capture complete ==="
Write-Host "PNGs in ${shots}:"
Get-ChildItem $shots -Filter "*.png" -ErrorAction SilentlyContinue | Select-Object Name, Length
