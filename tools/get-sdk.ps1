# Downloads the .NET 8 SDK from Microsoft into the given folder (build.bat: %LOCALAPPDATA%\WinNotch\dotnet), with visible progress.
# Used by build.bat when no SDK is installed. Nothing is installed in Windows; no admin rights needed.
param([Parameter(Mandatory = $true)][string]$Dest)

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Add-Type -AssemblyName System.Net.Http
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$feed = 'https://builds.dotnet.microsoft.com/dotnet'
$zip = Join-Path $env:TEMP 'winnotch-dotnet-sdk.zip'

function Bar([double]$frac, [int]$width = 28) {
    $n = [math]::Floor([math]::Max(0, [math]::Min(1, $frac)) * $width)
    return '[' + ('#' * $n) + ('.' * ($width - $n)) + ']'
}

try {
    Write-Host ''
    Write-Host ' [1/3] Caut ultima versiune .NET 8 SDK la Microsoft...' -ForegroundColor Cyan
    $http = New-Object System.Net.Http.HttpClient
    $http.Timeout = [TimeSpan]::FromMinutes(30)
    $version = ($http.GetStringAsync("$feed/Sdk/8.0/latest.version").Result).Trim().Split("`n")[-1].Trim()
    if ($version -notmatch '^\d+\.\d+\.\d+') { throw "versiune necunoscuta: $version" }
    Write-Host "       Gasit: .NET SDK $version"

    $url = "$feed/Sdk/$version/dotnet-sdk-$version-win-x64.zip"
    Write-Host ''
    Write-Host ' [2/3] Descarc SDK-ul (cam 250 MB)...' -ForegroundColor Cyan
    $resp = $http.GetAsync($url, [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead).Result
    if (-not $resp.IsSuccessStatusCode) { throw "descarcarea a raspuns $([int]$resp.StatusCode)" }
    $total = $resp.Content.Headers.ContentLength
    $in = $resp.Content.ReadAsStreamAsync().Result
    $out = [IO.File]::Create($zip)
    $buf = New-Object byte[] (1MB)
    $done = 0L
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $last = 0
    try {
        while (($read = $in.Read($buf, 0, $buf.Length)) -gt 0) {
            $out.Write($buf, 0, $read)
            $done += $read
            if ($sw.ElapsedMilliseconds - $last -ge 250) {
                $last = $sw.ElapsedMilliseconds
                $mb = $done / 1MB
                $speed = $mb / [math]::Max(0.1, $sw.Elapsed.TotalSeconds)
                if ($total) {
                    $frac = $done / $total
                    $left = [math]::Max(0, ($total - $done) / 1MB / [math]::Max(0.1, $speed))
                    $line = '       {0} {1,3:0}%  {2,5:0} / {3:0} MB   {4,5:0.0} MB/s   ~{5:0} s ramase   ' -f (Bar $frac), ($frac * 100), $mb, ($total / 1MB), $speed, $left
                } else {
                    $line = '       {0,5:0} MB descarcati   {1,5:0.0} MB/s   ' -f $mb, $speed
                }
                Write-Host -NoNewline ("`r" + $line)
            }
        }
    } finally { $out.Close(); $in.Close() }
    Write-Host ("`r       {0} 100%  {1:0} MB descarcati in {2:0} s                         " -f (Bar 1), ($done / 1MB), $sw.Elapsed.TotalSeconds)

    # Integrity: compare with the SHA-512 Microsoft publishes for this exact file.
    try {
        $meta = $http.GetStringAsync("$feed/release-metadata/8.0/releases.json").Result | ConvertFrom-Json
        $file = $meta.releases | ForEach-Object { @($_.sdks) + @($_.sdk) } | Where-Object { $_ -and $_.version -eq $version } |
                ForEach-Object { $_.files } | Where-Object { $_.rid -eq 'win-x64' -and $_.name -like '*.zip' } | Select-Object -First 1
        if ($file -and $file.hash) {
            $sha = New-Object System.Security.Cryptography.SHA512Managed
            $fs = [IO.File]::OpenRead($zip)
            try { $got = ([BitConverter]::ToString($sha.ComputeHash($fs)) -replace '-', '').ToLowerInvariant() } finally { $fs.Close() }
            if ($got -ne $file.hash.ToLowerInvariant()) { throw 'fisierul descarcat nu corespunde semnaturii Microsoft (SHA-512)' }
            Write-Host '       Verificat: semnatura SHA-512 corespunde.' -ForegroundColor Green
        } else { Write-Host '       (nu am gasit semnatura in lista Microsoft; continui, descarcarea a fost prin HTTPS)' }
    } catch {
        if ($_.Exception.Message -like '*SHA-512*') { throw }
        Write-Host '       (nu am putut verifica semnatura; continui, descarcarea a fost prin HTTPS)'
    }

    Write-Host ''
    Write-Host ' [3/3] Dezarhivez...' -ForegroundColor Cyan
    $full = [IO.Path]::GetFullPath($Dest).TrimEnd('\')
    $root = $full + '\'
    if (Test-Path $full) { Remove-Item $full -Recurse -Force }
    New-Item -ItemType Directory -Path $full | Out-Null
    $archive = [IO.Compression.ZipFile]::OpenRead($zip)
    try {
        $entries = $archive.Entries
        $count = $entries.Count
        $i = 0
        foreach ($e in $entries) {
            $i++
            $target = [IO.Path]::GetFullPath((Join-Path $full $e.FullName))
            if (-not $target.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { continue }   # never write outside .dotnet (trailing \ so ".dotnet-x" doesn't match)
            if ($e.FullName.EndsWith('/')) { New-Item -ItemType Directory -Force -Path $target | Out-Null; continue }
            New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($target)) | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($e, $target, $true)
            if ($i % 200 -eq 0 -or $i -eq $count) {
                Write-Host -NoNewline ("`r       {0} {1,3:0}%  {2} / {3} fisiere   " -f (Bar ($i / $count)), ($i * 100 / $count), $i, $count)
            }
        }
    } finally { $archive.Dispose() }
    Write-Host ''
    Remove-Item $zip -Force -ErrorAction SilentlyContinue

    if (-not (Test-Path (Join-Path $full 'dotnet.exe'))) { throw 'dotnet.exe lipseste dupa dezarhivare' }
    Write-Host ''
    Write-Host " Gata: .NET SDK $version este in $full" -ForegroundColor Green
    exit 0
}
catch {
    Write-Host ''
    Write-Host (' Nu a mers: ' + $_.Exception.GetBaseException().Message) -ForegroundColor Red
    Remove-Item $zip -Force -ErrorAction SilentlyContinue
    exit 1
}
