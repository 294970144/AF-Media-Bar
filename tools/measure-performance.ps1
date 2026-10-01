# Samples one AFMediaBar process tree for local A/B performance comparisons.
# This development-only script owns its CSV files; it does not change app settings or send telemetry.
param(
    [Parameter(Mandatory = $true)][int]$ProcessId,
    [ValidateRange(1, 86400)][int]$DurationSeconds = 60,
    [ValidateRange(1, 3600)][int]$IntervalSeconds = 5,
    [Parameter(Mandatory = $true)][string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$root = Get-Process -Id $ProcessId -ErrorAction Stop
if ($root.ProcessName -ne 'AFMediaBar') {
    throw "PID $ProcessId is $($root.ProcessName), not AFMediaBar."
}

$rootStart = $root.StartTime
$outputFile = [IO.Path]::GetFullPath($OutputPath)
$outputDirectory = [IO.Path]::GetDirectoryName($outputFile)
[IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
$processFile = [IO.Path]::ChangeExtension($outputFile, '.processes.csv')
$summary = [Collections.Generic.List[object]]::new()
$details = [Collections.Generic.List[object]]::new()
$previousCpu = @{}
$deadline = [DateTimeOffset]::UtcNow.AddSeconds($DurationSeconds)
$sampleNumber = 0

do {
    $timestamp = [DateTimeOffset]::UtcNow
    $all = @(Get-CimInstance Win32_Process | Select-Object ProcessId, ParentProcessId, Name, CreationDate)
    $byPid = @{}
    foreach ($item in $all) { $byPid[[int]$item.ProcessId] = $item }
    if (-not $byPid.ContainsKey($ProcessId)) { break }

    $currentRoot = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    if ($null -eq $currentRoot -or $currentRoot.StartTime -ne $rootStart) { break }

    $owned = [Collections.Generic.HashSet[int]]::new()
    [void]$owned.Add($ProcessId)
    do {
        $added = $false
        foreach ($item in $all) {
            $pidValue = [int]$item.ProcessId
            $parentId = [int]$item.ParentProcessId
            if ($owned.Contains($pidValue) -or -not $owned.Contains($parentId)) { continue }
            if ($byPid.ContainsKey($parentId) -and $item.CreationDate -ge $byPid[$parentId].CreationDate) {
                [void]$owned.Add($pidValue)
                $added = $true
            }
        }
    } while ($added)

    $workingSet = [long]0
    $privateBytes = [long]0
    $cpuPercent = 0.0
    $webViewCount = 0
    $processCount = 0
    $handleCount = 0
    $threadCount = 0
    $sampleNumber++
    foreach ($ownedPid in $owned) {
        try {
            $process = Get-Process -Id $ownedPid -ErrorAction Stop
            $start = $process.StartTime
            $key = "$ownedPid|$($start.ToUniversalTime().Ticks)"
            $cpuSeconds = $process.TotalProcessorTime.TotalSeconds
            $oneCorePercent = 0.0
            if ($previousCpu.ContainsKey($key)) {
                $elapsed = ($timestamp - $previousCpu[$key].At).TotalSeconds
                if ($elapsed -gt 0) {
                    $oneCorePercent = 100.0 * ($cpuSeconds - $previousCpu[$key].Seconds) / $elapsed
                }
            }
            $previousCpu[$key] = @{ At = $timestamp; Seconds = $cpuSeconds }
            $workingSet += $process.WorkingSet64
            $processCount++
            $privateBytes += $process.PrivateMemorySize64
            $cpuPercent += $oneCorePercent
            $handleCount += $process.HandleCount
            $threadCount += $process.Threads.Count
            if ($process.ProcessName -eq 'msedgewebview2') { $webViewCount++ }
            $details.Add([pscustomobject]@{
                TimeUtc = $timestamp.ToString('O'); Sample = $sampleNumber
                ProcessName = $process.ProcessName; PID = $ownedPid; StartUtc = $start.ToUniversalTime().ToString('O')
                WorkingSetMiB = [math]::Round($process.WorkingSet64 / 1MB, 2)
                PrivateBytesMiB = [math]::Round($process.PrivateMemorySize64 / 1MB, 2)
                CpuPercentOneCore = [math]::Round($oneCorePercent, 2)
                Handles = $process.HandleCount; Threads = $process.Threads.Count
            })
        } catch [System.ArgumentException] {
            # A child may exit between process enumeration and sampling.
        } catch [System.InvalidOperationException] {
            # A child may exit between process enumeration and sampling.
        }
    }
    $gpuEnginePercentSum = $null
    $gpuDedicatedBytes = $null
    $gpuSharedBytes = $null
    try {
        # Windows exposes GPU counters by PID; keep only this process tree's instances.
        $gpuSamples = (Get-Counter -Counter '\GPU Engine(*)\Utilization Percentage',
            '\GPU Process Memory(*)\Dedicated Usage','\GPU Process Memory(*)\Shared Usage' -ErrorAction Stop).CounterSamples
        $engineSum = 0.0
        $dedicatedSum = [long]0
        $sharedSum = [long]0
        foreach ($gpuSample in $gpuSamples) {
            if ($gpuSample.Path -notmatch 'pid_(\d+)_') { continue }
            if (-not $owned.Contains([int]$Matches[1])) { continue }
            if ($gpuSample.Path -like '*\Utilization Percentage') {
                $engineSum += $gpuSample.CookedValue
            } elseif ($gpuSample.Path -like '*\Dedicated Usage') {
                $dedicatedSum += [long]$gpuSample.CookedValue
            } elseif ($gpuSample.Path -like '*\Shared Usage') {
                $sharedSum += [long]$gpuSample.CookedValue
            }
        }
        $gpuEnginePercentSum = [math]::Round($engineSum, 2)
        $gpuDedicatedBytes = [math]::Round($dedicatedSum / 1MB, 2)
        $gpuSharedBytes = [math]::Round($sharedSum / 1MB, 2)
    } catch {
        # GPU counters are optional on machines or sessions without a supported adapter.
    }
    $summary.Add([pscustomobject]@{
        TimeUtc = $timestamp.ToString('O'); Sample = $sampleNumber
        RootPID = $ProcessId; RootStartUtc = $rootStart.ToUniversalTime().ToString('O')
        ProcessCount = $processCount; WebView2Count = $webViewCount
        WorkingSetMiB = [math]::Round($workingSet / 1MB, 2)
        PrivateBytesMiB = [math]::Round($privateBytes / 1MB, 2)
        CpuPercentOneCore = [math]::Round($cpuPercent, 2)
        GpuEnginePercentSum = $gpuEnginePercentSum
        GpuDedicatedMiB = $gpuDedicatedBytes; GpuSharedMiB = $gpuSharedBytes
        Handles = $handleCount; Threads = $threadCount
    })

    if ([DateTimeOffset]::UtcNow -ge $deadline) { break }
    Start-Sleep -Seconds ([math]::Min($IntervalSeconds, [math]::Max(1, [math]::Ceiling(($deadline - [DateTimeOffset]::UtcNow).TotalSeconds))))
} while ($true)

$summary | Export-Csv -LiteralPath $outputFile -NoTypeInformation -Encoding UTF8
$details | Export-Csv -LiteralPath $processFile -NoTypeInformation -Encoding UTF8
Write-Output "Summary: $outputFile"
Write-Output "Processes: $processFile"
