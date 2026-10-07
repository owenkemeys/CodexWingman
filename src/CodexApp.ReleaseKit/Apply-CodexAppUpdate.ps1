[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Package,
    [Parameter(Mandatory)][string]$Installed,
    [Parameter(Mandatory)][int]$WaitingPid,
    [Parameter(Mandatory)][string]$ExpectedVersion,
    [Parameter(Mandatory)][string]$ExpectedRepository,
    [Parameter(Mandatory)][string]$ExpectedSchema,
    [Parameter(Mandatory)][string]$ExecutableName,
    [Parameter(Mandatory)][string]$StableDirectoryName,
    [string]$ExtensionsDirectoryName = '',
    [string]$RelaunchArguments = '',
    [switch]$PreflightOnly
)
$ErrorActionPreference = 'Stop'
if ($RelaunchArguments.IndexOfAny([char[]]@([char]0, [char]10, [char]13)) -ge 0) {
    throw 'Invalid app relaunch arguments'
}
foreach ($name in @($ExecutableName, $StableDirectoryName, $ExtensionsDirectoryName)) {
    if ($name -and $name -notmatch '^[A-Za-z0-9_.-]+$') { throw 'Invalid release path component' }
}
if ($ExpectedRepository -notmatch '^https://github\.com/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$' -or
    $ExpectedSchema -notmatch '^[a-z0-9.-]+$') {
    throw 'Invalid release identity parameter'
}

function Get-ReleaseFileHash([string]$path) {
    $stream = [IO.File]::OpenRead($path)
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try {
        return [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '')
    }
    finally {
        $algorithm.Dispose()
        $stream.Dispose()
    }
}

function Assert-SealedPackage([string]$directory, [string]$version) {
    $recordPath = Join-Path $directory 'release.json'
    $record = Get-Content -LiteralPath $recordPath -Raw | ConvertFrom-Json
    if ($record.schema -ne $ExpectedSchema -or
        $record.repository -ne $ExpectedRepository -or
        $record.version -ne $version) {
        throw 'Downloaded release identity does not match the selected update'
    }
    $expected = @{}
    foreach ($entry in $record.files.PSObject.Properties) {
        $expected[$entry.Name] = [string]$entry.Value
    }
    if (-not $expected.ContainsKey($ExecutableName) -or $expected.Count -lt 1) {
        throw 'Downloaded release is incomplete'
    }
    $actual = @{}
    foreach ($item in Get-ChildItem -LiteralPath $directory -Recurse -Force) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw 'Downloaded release contains a link'
        }
        if ($item.PSIsContainer) { continue }
        $name = $item.FullName.Substring($directory.Length + 1).Replace('\', '/')
        if ($name -eq 'release.json') { continue }
        if (-not $expected.ContainsKey($name)) { throw "Unexpected release file: $name" }
        $hash = Get-ReleaseFileHash $item.FullName
        if ($hash -ine $expected[$name]) { throw "Release file did not verify: $name" }
        $actual[$name] = $true
    }
    if ($actual.Count -ne $expected.Count) { throw 'Downloaded release is missing a file' }
}

function Start-UpdatedApp([string]$executable) {
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = $executable
    $start.Arguments = $RelaunchArguments
    $start.WorkingDirectory = $installedFull
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    return [Diagnostics.Process]::Start($start)
}

$packageFull = [IO.Path]::GetFullPath($Package).TrimEnd('\', '/')
$installedFull = [IO.Path]::GetFullPath($Installed).TrimEnd('\', '/')
$parent = Split-Path -Parent $installedFull
$leaf = Split-Path -Leaf $installedFull
if ($leaf -ine $StableDirectoryName -or
    -not (Test-Path -LiteralPath (Join-Path $installedFull $ExecutableName) -PathType Leaf) -or
    -not (Test-Path -LiteralPath (Join-Path $packageFull $ExecutableName) -PathType Leaf) -or
    $packageFull -eq $installedFull -or
    $packageFull.StartsWith($installedFull + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
    $installedFull.StartsWith($packageFull + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Unsafe app update path'
}
Assert-SealedPackage $packageFull $ExpectedVersion

if ($PreflightOnly) {
    $probe = Join-Path $parent ($leaf + '.preflight-' + [Guid]::NewGuid().ToString('N'))
    $moved = $probe + '.moved'
    try {
        New-Item -ItemType Directory -Path $probe | Out-Null
        Set-Content -LiteralPath (Join-Path $probe 'marker.txt') -Value 'codex-app-preflight' -Encoding ascii
        Move-Item -LiteralPath $probe -Destination $moved
        if ((Get-Content -LiteralPath (Join-Path $moved 'marker.txt') -Raw).Trim() -ne 'codex-app-preflight') {
            throw 'App update preflight readback failed'
        }
    }
    finally {
        if (Test-Path -LiteralPath $probe) { Remove-Item -LiteralPath $probe -Recurse -Force }
        if (Test-Path -LiteralPath $moved) { Remove-Item -LiteralPath $moved -Recurse -Force }
    }
    return
}

$nonce = [Guid]::NewGuid().ToString('N')
$staging = Join-Path $parent ($leaf + '.update-' + $nonce)
$rollback = Join-Path $parent ($leaf + '.rollback-' + $nonce)
$oldMoved = $false
$newMoved = $false
try {
    New-Item -ItemType Directory -Path $staging | Out-Null
    Get-ChildItem -LiteralPath $packageFull -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $staging -Recurse
    }
    Assert-SealedPackage $staging $ExpectedVersion

    # Preserve user-added extension folders. Bundled folder names in the new release win;
    # the full previous package remains in the rollback directory.
    if ($ExtensionsDirectoryName) {
      $oldHelpers = Join-Path $installedFull $ExtensionsDirectoryName
      $newHelpers = Join-Path $staging $ExtensionsDirectoryName
    }
    if ($ExtensionsDirectoryName -and (Test-Path -LiteralPath $oldHelpers -PathType Container)) {
        foreach ($helper in Get-ChildItem -LiteralPath $oldHelpers -Directory -Force) {
            $destination = Join-Path $newHelpers $helper.Name
            if (Test-Path -LiteralPath $destination) { continue }
            foreach ($entry in @($helper) + @(Get-ChildItem -LiteralPath $helper.FullName -Recurse -Force)) {
                if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) {
                    throw 'A custom extension contains a link; the update was not applied'
                }
            }
            Copy-Item -LiteralPath $helper.FullName -Destination $destination -Recurse
        }
    }

    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while (Get-Process -Id $WaitingPid -ErrorAction SilentlyContinue) {
        if ([DateTime]::UtcNow -ge $deadline) { throw 'The app did not exit for the update' }
        Start-Sleep -Milliseconds 250
    }

    Move-Item -LiteralPath $installedFull -Destination $rollback
    $oldMoved = $true
    Move-Item -LiteralPath $staging -Destination $installedFull
    $newMoved = $true
    $exe = Join-Path $installedFull $ExecutableName
    $started = Start-UpdatedApp $exe
    Start-Sleep -Seconds 3
    if ($started.HasExited) { throw 'Updated app exited immediately' }
    # Keep the rollback for a deliberate later cleanup after live acceptance.
}
catch {
    if ($newMoved) {
        $failed = Join-Path $parent ($leaf + '.failed-' + $nonce)
        Move-Item -LiteralPath $installedFull -Destination $failed
    }
    if ($oldMoved) {
        Move-Item -LiteralPath $rollback -Destination $installedFull
        $restored = Start-UpdatedApp (Join-Path $installedFull $ExecutableName)
        $restored.Dispose()
    }
    throw
}
