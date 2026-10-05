# Baseline measurements of WinNotch at rest: starts publish\WinNotch.exe, waits 60 s, then for 10 minutes samples every
# 5 s its Working Set, Private Bytes and CPU% (of the whole machine). Writes the averages and maximums to
# docs\perf\baseline-<version>.md. Close WinNotch before running it (only one copy can run).
#   powershell -ExecutionPolicy Bypass -File tools\measure-perf.ps1 [-Exe publish\WinNotch.exe] [-Minutes 10]
param(
    [string]$Exe = (Join-Path $PSScriptRoot '..\publish\WinNotch.exe'),
    [int]$WarmupSeconds = 60,
    [int]$Minutes = 10,
    [int]$IntervalSeconds = 5
)
$ErrorActionPreference = 'Stop'
$Exe = (Resolve-Path $Exe).Path
if (Get-Process -Name WinNotch -ErrorAction SilentlyContinue) { throw "WinNotch rulează deja: închide-l (Ieșire din meniul iconiței) și pornește din nou scriptul." }

$info = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($Exe)
$version = ($info.ProductVersion -split '\+')[0]
if (-not $version) { $version = $info.FileVersion }

Write-Host "Pornesc $Exe (versiunea $version)…"
$p = Start-Process -FilePath $Exe -PassThru
Write-Host "Aștept $WarmupSeconds s să se liniștească…"
Start-Sleep -Seconds $WarmupSeconds
if ($p.HasExited) { throw "WinNotch s-a închis în timpul pornirii (cod $($p.ExitCode))." }

$cores = [Environment]::ProcessorCount
$samples = @()
$count = [int]($Minutes * 60 / $IntervalSeconds)
$p.Refresh()
$lastCpu = $p.TotalProcessorTime
$lastAt = Get-Date
for ($i = 1; $i -le $count; $i++) {
    Start-Sleep -Seconds $IntervalSeconds
    if ($p.HasExited) { throw "WinNotch s-a închis în timpul măsurării (cod $($p.ExitCode))." }
    $p.Refresh()
    $now = Get-Date
    $cpu = $p.TotalProcessorTime
    $pct = 100 * ($cpu - $lastCpu).TotalMilliseconds / (($now - $lastAt).TotalMilliseconds * $cores)
    $lastCpu = $cpu; $lastAt = $now
    $samples += [pscustomobject]@{ WorkingSetMB = $p.WorkingSet64 / 1MB; PrivateMB = $p.PrivateMemorySize64 / 1MB; Cpu = $pct }
    Write-Progress -Activity "Măsor WinNotch $version" -Status "$i / $count" -PercentComplete (100 * $i / $count)
}
Write-Progress -Activity "Măsor WinNotch $version" -Completed

function Stat($name) {
    $m = $samples | Measure-Object -Property $name -Average -Maximum
    [pscustomobject]@{ Avg = [math]::Round($m.Average, 1); Max = [math]::Round($m.Maximum, 1) }
}
$ws = Stat 'WorkingSetMB'; $pb = Stat 'PrivateMB'; $c = Stat 'Cpu'

$os = (Get-CimInstance Win32_OperatingSystem)
$cpuName = (Get-CimInstance Win32_Processor | Select-Object -First 1).Name
$out = Join-Path $PSScriptRoot "..\docs\perf\baseline-$version.md"
New-Item -ItemType Directory -Force -Path (Split-Path $out) | Out-Null
@"
# Măsurători de bază — WinNotch $version

- Data: $(Get-Date -Format 'yyyy-MM-dd HH:mm')
- Sistem: $($os.Caption) $($os.BuildNumber) · $cpuName · $cores nuclee logice
- Metodă: pornire, $WarmupSeconds s de așteptare, apoi $Minutes minute cu o măsurătoare la $IntervalSeconds s ($($samples.Count) măsurători), notch-ul închis, fără interacțiune. CPU% e din tot procesorul.

| Măsură | Medie | Maxim |
|---|---|---|
| Working Set (MB) | $($ws.Avg) | $($ws.Max) |
| Private Bytes (MB) | $($pb.Avg) | $($pb.Max) |
| CPU (%) | $($c.Avg) | $($c.Max) |
"@ | Set-Content -Path $out -Encoding UTF8

Write-Host "Gata: $out"
Write-Host "WinNotch rămâne pornit."
