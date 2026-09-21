[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackageRoot
)

$ErrorActionPreference = 'Stop'
$packageRootFull = [IO.Path]::GetFullPath($PackageRoot).TrimEnd('\', '/')
$corePath = Join-Path $packageRootFull 'CodexWingman.Core.dll'
$helperPath = Join-Path $packageRootFull 'Helpers\Obsidian links\apply.js'

foreach ($requiredPath in @($corePath, $helperPath)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Obsidian link package contract is missing $requiredPath"
    }
}

$helperSource = Get-Content -LiteralPath $helperPath -Raw
$coreStrings = [Text.Encoding]::Unicode.GetString([IO.File]::ReadAllBytes($corePath))
if ($helperSource.IndexOf('obsidian://wait-for-note', [StringComparison]::Ordinal) -ge 0 -and
    -not ($coreStrings.IndexOf('wait-for-note', [StringComparison]::Ordinal) -ge 0)) {
    throw "Obsidian link package protocol mismatch: Helper emits wait-for-note but CodexWingman.Core.dll does not accept it"
}

if (-not ($coreStrings.IndexOf('system.openObsidianUri', [StringComparison]::Ordinal) -ge 0)) {
    throw "Obsidian link package protocol mismatch: CodexWingman.Core.dll does not expose system.openObsidianUri"
}

Write-Output 'Obsidian link package protocol is coherent.'
