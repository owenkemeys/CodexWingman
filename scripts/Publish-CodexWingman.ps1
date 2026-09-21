[CmdletBinding()]
param(
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$Destination,
    [string]$Configuration = 'Release',
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'

$projectRootFull = [IO.Path]::GetFullPath($ProjectRoot).TrimEnd('\', '/')
$appProject = Join-Path $projectRootFull 'src\CodexWingman\CodexWingman.csproj'
if (-not (Test-Path -LiteralPath $appProject -PathType Leaf)) {
    throw "CodexWingman project not found: $appProject"
}

if ([string]::IsNullOrWhiteSpace($Destination)) {
    $Destination = Join-Path $projectRootFull 'dist\CodexWingman-verified'
}
$destinationFull = [IO.Path]::GetFullPath($Destination).TrimEnd('\', '/')
$destinationRoot = [IO.Path]::GetPathRoot($destinationFull).TrimEnd('\', '/')
$destinationPrefix = $destinationFull + [IO.Path]::DirectorySeparatorChar
$destinationContainsProject = $projectRootFull.StartsWith(
    $destinationPrefix,
    [StringComparison]::OrdinalIgnoreCase)
if ([string]::IsNullOrWhiteSpace($destinationFull) -or
    $destinationFull.Equals($destinationRoot, [StringComparison]::OrdinalIgnoreCase) -or
    $destinationFull.Equals($projectRootFull, [StringComparison]::OrdinalIgnoreCase) -or
    $destinationContainsProject) {
    throw "Unsafe publish destination: $destinationFull"
}

$destinationParent = Split-Path -Parent $destinationFull
$destinationLeaf = Split-Path -Leaf $destinationFull
if ([string]::IsNullOrWhiteSpace($destinationParent) -or [string]::IsNullOrWhiteSpace($destinationLeaf)) {
    throw "Publish destination must be a named folder: $destinationFull"
}
if (-not $destinationLeaf.Equals('CodexWingman-verified', [StringComparison]::OrdinalIgnoreCase)) {
    throw "Publish destination must end in CodexWingman-verified: $destinationFull"
}
# This gate precedes build output or replacement of an installed package.
$python = (Get-Command python -ErrorAction Stop).Source
$provenance = Join-Path $projectRootFull 'tools\release_provenance.py'
$verificationJson = & $python $provenance check-source --root $projectRootFull
if ($LASTEXITCODE -ne 0) { throw 'Verified source gate rejected this publish' }
$verifiedCommit = ($verificationJson | ConvertFrom-Json).commit
New-Item -ItemType Directory -Path $destinationParent -Force | Out-Null

$nonce = [Guid]::NewGuid().ToString('N')
$stagingRoot = Join-Path $destinationParent "$destinationLeaf.staging-$nonce"
$previousRoot = Join-Path $destinationParent "$destinationLeaf.previous-$nonce"
$oldMoved = $false
$newMoved = $false

function Assert-SiblingPath([string]$path) {
    $resolvedParent = Split-Path -Parent ([IO.Path]::GetFullPath($path).TrimEnd('\', '/'))
    if (-not $resolvedParent.Equals($destinationParent, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Temporary publish path escaped the destination parent: $path"
    }
}

Assert-SiblingPath $stagingRoot
Assert-SiblingPath $previousRoot

try {
    $publishArguments = @('publish', $appProject, '-c', $Configuration, '-o', $stagingRoot, ('-p:SourceRevisionId=' + $verifiedCommit))
    if ($NoRestore) { $publishArguments += '--no-restore' }
    & dotnet @publishArguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

    foreach ($requiredPath in @('CodexWingman.exe', 'Helpers')) {
        if (-not (Test-Path -LiteralPath (Join-Path $stagingRoot $requiredPath))) {
            throw "Staged package is missing $requiredPath"
        }
    }
    $stagedHelpersRoot = Join-Path $stagingRoot 'Helpers'
    if ((Get-Item -LiteralPath $stagedHelpersRoot).Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw "Staged Helpers root must be a real executable-relative directory: $stagedHelpersRoot"
    }
    & (Join-Path $projectRootFull 'scripts\Test-ObsidianLinkPackageContract.ps1') -PackageRoot $stagingRoot
    & $python $provenance seal --root $projectRootFull --package $stagingRoot
    if ($LASTEXITCODE -ne 0) { throw 'Package provenance gate rejected this publish' }
    & $python $provenance verify-package --package $stagingRoot
    if ($LASTEXITCODE -ne 0) { throw 'Sealed package verification failed' }


    if (Test-Path -LiteralPath $destinationFull) {
        Move-Item -LiteralPath $destinationFull -Destination $previousRoot
        $oldMoved = $true
    }

    try {
        Move-Item -LiteralPath $stagingRoot -Destination $destinationFull
        $newMoved = $true
    }
    catch {
        if ($oldMoved -and -not (Test-Path -LiteralPath $destinationFull)) {
            Move-Item -LiteralPath $previousRoot -Destination $destinationFull
            $oldMoved = $false
        }
        throw
    }

    if ($oldMoved) {
        Write-Information ('Rollback retained at ' + $previousRoot) -InformationAction Continue
        $oldMoved = $false
    }

    Write-Output $destinationFull
}
finally {
    if (Test-Path -LiteralPath $stagingRoot) {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
    if ($oldMoved -and -not $newMoved -and -not (Test-Path -LiteralPath $destinationFull)) {
        Move-Item -LiteralPath $previousRoot -Destination $destinationFull
        $oldMoved = $false
    }
}
