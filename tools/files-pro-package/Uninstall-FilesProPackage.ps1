param(
	[string]$ManifestPath,
	[switch]$RollbackShell,
	[switch]$Apply
)

$ErrorActionPreference = "Stop"

function Write-Plan($Message) {
	Write-Host "[Files Pro uninstall] $Message"
}

function Get-LatestManifest {
	$root = Join-Path $env:LOCALAPPDATA "FilesPro\InstallBackups"
	if (-not (Test-Path -LiteralPath $root)) {
		return $null
	}

	Get-ChildItem -LiteralPath $root -Recurse -Filter "install-manifest.json" |
		Sort-Object LastWriteTime -Descending |
		Select-Object -First 1
}

if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
	$latest = Get-LatestManifest
	if ($null -eq $latest) {
		throw "No install manifest was found under $env:LOCALAPPDATA\FilesPro\InstallBackups. Pass -ManifestPath explicitly."
	}

	$ManifestPath = $latest.FullName
}

$manifestFile = Resolve-Path $ManifestPath
$manifest = Get-Content -LiteralPath $manifestFile -Raw | ConvertFrom-Json
$packageFullName = [string]$manifest.InstalledPackageFullName

Write-Plan "Manifest: $manifestFile"
Write-Plan "Package full name: $packageFullName"
Write-Plan "Rollback shell: $RollbackShell"
if ($manifest.ShellManifestPath) {
	Write-Plan "Shell manifest: $($manifest.ShellManifestPath)"
}

if (-not $Apply) {
	Write-Plan "Dry run only. Re-run with -Apply to uninstall the package and optional shell integration."
	exit 0
}

if ($RollbackShell -or $manifest.ShellApplied) {
	$rollbackArgs = @("-Apply")
	if ([string]$manifest.ShellRollbackMode -ne "RemoveOverrides") {
		$rollbackArgs += "-RestoreBackup"
		if ($manifest.ShellBackupDirectory) {
			$rollbackArgs += "-BackupDirectory"
			$rollbackArgs += [string]$manifest.ShellBackupDirectory
		}
	}

	& (Join-Path (Resolve-Path (Join-Path $PSScriptRoot "..\..")) "tools\files-pro-shell\Unregister-FilesProShell.ps1") @rollbackArgs
}

if (-not [string]::IsNullOrWhiteSpace($packageFullName)) {
	$package = Get-AppxPackage -PackageFullName $packageFullName -ErrorAction SilentlyContinue
	if ($package) {
		Remove-AppxPackage -Package $packageFullName
		Write-Plan "Removed $packageFullName"
	}
	else {
		Write-Plan "Package is not currently installed."
	}
}
else {
	Write-Plan "Manifest does not contain an installed package full name."
}

Write-Plan "Uninstall flow complete."
