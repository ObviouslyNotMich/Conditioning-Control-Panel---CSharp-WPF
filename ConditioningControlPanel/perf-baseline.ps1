# Performance baseline capture script for Avalonia vs WPF
# Measures: startup readiness (time to WorkingSet > threshold), working set after 10s, peak working set.
# Uses built executables directly to avoid dotnet-run overhead.

param(
    [string]$AvaloniaExe = "$PSScriptRoot/CCP.Avalonia.Desktop.Windows/bin/Debug/net8.0-windows10.0.19041.0/CCP.Desktop.Windows.exe",
    [string]$WpfExe = "$PSScriptRoot/bin/Debug/net8.0-windows10.0.19041.0/win-x64/ConditioningControlPanel.exe",
    [int]$MemoryThresholdMB = 50,
    [int]$SampleSeconds = 10,
    [int]$TimeoutSeconds = 90
)

function Measure-App($Name, $ExePath, $Arguments) {
    $result = @{ Name = $Name; Exe = $ExePath; Args = ($Arguments -join ' ') }
    if (-not (Test-Path $ExePath)) {
        $result.Error = "Executable not found: $ExePath"
        return $result
    }

    $start = Get-Date
    $proc = Start-Process $ExePath -ArgumentList $Arguments -PassThru -WorkingDirectory $PSScriptRoot
    $result.PID = $proc.Id
    $result.ProcessStartAt = $start

    $ready = $null
    $samples = @()
    $deadline = $start.AddSeconds($TimeoutSeconds)
    $sampleDeadline = $start.AddSeconds($SampleSeconds)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 200
        $p = Get-Process -Id $proc.Id -ErrorAction SilentlyContinue
        if (-not $p) { break }
        $wsMB = [math]::Round($p.WorkingSet64 / 1MB, 2)
        $samples += [PSCustomObject]@{ Time = (Get-Date); WorkingSetMB = $wsMB }
        if (-not $ready -and $wsMB -gt $MemoryThresholdMB) {
            $ready = Get-Date
            $result.StartupSeconds = [math]::Round(($ready - $start).TotalSeconds, 2)
        }
        if ((Get-Date) -gt $sampleDeadline) {
            $p.Refresh()
            $result.WorkingSetAtSampleMB = [math]::Round($p.WorkingSet64 / 1MB, 2)
            $result.PeakWorkingSetMB = [math]::Round($p.PeakWorkingSet64 / 1MB, 2)
            $result.PrivateMemoryMB = [math]::Round($p.PrivateMemorySize64 / 1MB, 2)
            $result.PagedMemoryMB = [math]::Round($p.PagedMemorySize64 / 1MB, 2)
            break
        }
    }

    # Ensure process is terminated unless it already exited
    try { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } catch {}
    $proc.WaitForExit(5000) | Out-Null
    $result.ExitCode = $proc.ExitCode
    $result.Samples = $samples | Select-Object -First 200
    return $result
}

$avalonia = Measure-App 'Avalonia' $AvaloniaExe @('--smoke-test')
$avalonia | ConvertTo-Json -Depth 5 | Set-Content "$PSScriptRoot/perf-avalonia.json"

$wpf = Measure-App 'WPF' $WpfExe @()
$wpf | ConvertTo-Json -Depth 5 | Set-Content "$PSScriptRoot/perf-wpf.json"

Write-Host "=== Avalonia ==="
$avalonia | Select-Object Name, StartupSeconds, WorkingSetAtSampleMB, PeakWorkingSetMB, PrivateMemoryMB, PagedMemoryMB, ExitCode | Format-List

Write-Host "=== WPF ==="
$wpf | Select-Object Name, StartupSeconds, WorkingSetAtSampleMB, PeakWorkingSetMB, PrivateMemoryMB, PagedMemoryMB, ExitCode | Format-List
