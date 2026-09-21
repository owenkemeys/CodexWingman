[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Package,
    [string]$Destination
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$python = (Get-Command python -ErrorAction Stop).Source
$packageFull = (Resolve-Path -LiteralPath $Package).Path.TrimEnd('\', '/')
& $python (Join-Path $root 'tools\release_provenance.py') verify-package --package $packageFull
if ($LASTEXITCODE -ne 0) { throw 'Package verification failed; installation was not started' }
$processes = @(Get-CimInstance Win32_Process -Filter "name='CodexWingman.exe'")
if ([string]::IsNullOrWhiteSpace($Destination)) {
    if ($processes.Count -ne 1 -or [string]::IsNullOrWhiteSpace($processes[0].ExecutablePath)) {
        throw 'Cannot identify one installed Wingman; agent must supply the verified installation path'
    }
    $Destination = Split-Path -Parent $processes[0].ExecutablePath
}
$destinationFull = [IO.Path]::GetFullPath($Destination).TrimEnd('\', '/')
$parent = Split-Path -Parent $destinationFull
if ([string]::IsNullOrWhiteSpace($parent) -or
    $destinationFull -eq [IO.Path]::GetPathRoot($destinationFull).TrimEnd('\', '/') -or
    $packageFull -eq $destinationFull -or
    $packageFull.StartsWith($destinationFull + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
    $root.StartsWith($destinationFull + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
    -not (Test-Path -LiteralPath (Join-Path $destinationFull 'CodexWingman.exe') -PathType Leaf)) {
    throw 'Unsafe or unverified installation destination'
}
if ((Get-Item -LiteralPath $destinationFull -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
    throw 'Installation destination may not be a link or junction'
}
$nonce = [Guid]::NewGuid().ToString('N')
$staging = Join-Path $parent ((Split-Path -Leaf $destinationFull) + '.install-' + $nonce)
$rollback = Join-Path $parent ((Split-Path -Leaf $destinationFull) + '.rollback-' + $nonce)
foreach ($path in @($staging, $rollback)) {
    if ((Split-Path -Parent ([IO.Path]::GetFullPath($path))) -ne $parent -or (Test-Path -LiteralPath $path)) { throw 'Invalid transaction path' }
}
New-Item -ItemType Directory -Path $staging | Out-Null
Get-ChildItem -LiteralPath $packageFull -Force | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $staging -Recurse }
& $python (Join-Path $root 'tools\release_provenance.py') verify-package --package $staging
if ($LASTEXITCODE -ne 0) { throw 'Staged copy verification failed; installed Wingman is untouched' }
$exe = Join-Path $destinationFull 'CodexWingman.exe'
$owned = @($processes | Where-Object ExecutablePath -eq $exe)
$oldMoved = $false
$newMoved = $false
try {
    foreach ($process in $owned) { Stop-Process -Id $process.ProcessId -ErrorAction Stop }
    Move-Item -LiteralPath $destinationFull -Destination $rollback
    $oldMoved = $true
    Move-Item -LiteralPath $staging -Destination $destinationFull
    $newMoved = $true
    & $python (Join-Path $root 'tools\release_provenance.py') verify-package --package $destinationFull
    if ($LASTEXITCODE -ne 0) { throw 'Installed copy verification failed' }
    Start-Process -FilePath $exe -WorkingDirectory $destinationFull -WindowStyle Hidden
    [pscustomobject]@{Installed=$destinationFull;Rollback=$rollback;Commit=(Get-Content (Join-Path $destinationFull 'release.json') -Raw | ConvertFrom-Json).commit}
}
catch {
    if ($newMoved) { Move-Item -LiteralPath $destinationFull -Destination ($staging + '.failed') }
    if ($oldMoved) { Move-Item -LiteralPath $rollback -Destination $destinationFull }
    if ($owned.Count -gt 0 -and (Test-Path -LiteralPath $exe)) { Start-Process -FilePath $exe -WorkingDirectory $destinationFull -WindowStyle Hidden }
    throw
}
