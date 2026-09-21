[CmdletBinding()]
param(
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$ProfileRoot = [Environment]::GetFolderPath('UserProfile'),
    [string]$ProgramsFolder = [Environment]::GetFolderPath('Programs'),
    [string]$PackageDirectory = '',
    [switch]$ForceExplorerLaunch
)

$ErrorActionPreference = 'Stop'

$defaultVerifiedDirectory = Join-Path $ProjectRoot 'dist\CodexWingman-verified'
$canonicalDirectory = [IO.Path]::GetFullPath($defaultVerifiedDirectory).TrimEnd('\', '/')
$executableDirectory = if (-not [string]::IsNullOrWhiteSpace($PackageDirectory)) {
    $requestedDirectory = [IO.Path]::GetFullPath((Join-Path $ProjectRoot $PackageDirectory)).TrimEnd('\', '/')
    if (-not $requestedDirectory.Equals($canonicalDirectory, [StringComparison]::OrdinalIgnoreCase)) {
        throw "PackageDirectory must be dist\CodexWingman-verified: $requestedDirectory"
    }
    $requestedDirectory
} else {
    $canonicalDirectory
}
$executable = Join-Path $executableDirectory 'CodexWingman.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "CodexWingman executable not found: $executable"
}
$helpersDirectory = Join-Path $executableDirectory 'Helpers'
if (-not (Test-Path -LiteralPath $helpersDirectory -PathType Container)) {
    throw "Canonical executable-relative Helpers directory not found: $helpersDirectory"
}
if ((Get-Item -LiteralPath $helpersDirectory).Attributes -band [IO.FileAttributes]::ReparsePoint) {
    throw "Canonical Helpers directory must not be a symbolic link or junction: $helpersDirectory"
}

if ([string]::IsNullOrWhiteSpace($programsFolder)) {
    throw 'Windows did not provide a per-user Start Menu Programs folder.'
}

$shortcutPath = Join-Path $programsFolder 'Codex Wingman.lnk'
$shortcutTarget = $executable
$shortcutArguments = ''
$executingProfileRoot = [Environment]::GetFolderPath('UserProfile')
if ($ForceExplorerLaunch -or (-not [string]::IsNullOrWhiteSpace($ProfileRoot) -and
    -not [IO.Path]::GetFullPath($ProfileRoot).TrimEnd('\').Equals(
        [IO.Path]::GetFullPath($executingProfileRoot).TrimEnd('\'),
        [StringComparison]::OrdinalIgnoreCase))) {
    $shortcutTarget = Join-Path $env:WINDIR 'explorer.exe'
    $shortcutArguments = '"' + $executable + '"'
}
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $shortcutTarget
$shortcut.Arguments = $shortcutArguments
$shortcut.WorkingDirectory = $executableDirectory
$shortcut.IconLocation = "$executable,0"
$shortcut.Description = 'Codex Wingman - Codex desktop companion'
$shortcut.Save()

Write-Output $shortcutPath
