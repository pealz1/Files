param(
	[Parameter(Mandatory = $true)]
	[string]$PackagePath,
	[switch]$RegisterShell,
	[switch]$Apply,
	[string]$LauncherPath
)

$ErrorActionPreference = "Stop"

function Write-Plan($Message) {
	Write-Host "[Files Pro install] $Message"
}

function Get-MsixIdentity($Path) {
	Add-Type -AssemblyName System.IO.Compression.FileSystem
	$zip = [System.IO.Compression.ZipFile]::OpenRead($Path)
	try {
		$entry = $zip.GetEntry("AppxManifest.xml")
		if ($null -eq $entry) {
			throw "AppxManifest.xml was not found in $Path"
		}

		$reader = [System.IO.StreamReader]::new($entry.Open())
		try {
			[xml]$manifest = $reader.ReadToEnd()
			return [pscustomobject]@{
				Name = [string]$manifest.Package.Identity.Name
				Publisher = [string]$manifest.Package.Identity.Publisher
				Version = [string]$manifest.Package.Identity.Version
				ExecutionAlias = [string]$manifest.Package.Applications.Application.Extensions.Extension.AppExecutionAlias.ExecutionAlias.Alias
			}
		}
		finally {
			$reader.Dispose()
		}
	}
	finally {
		$zip.Dispose()
	}
}

function Resolve-LauncherPath($Identity, $InstalledPackage, $ExplicitLauncherPath) {
	if (-not [string]::IsNullOrWhiteSpace($ExplicitLauncherPath)) {
		if (-not (Test-Path -LiteralPath $ExplicitLauncherPath)) {
			throw "LauncherPath does not exist: $ExplicitLauncherPath"
		}

		return $ExplicitLauncherPath
	}

	if (-not [string]::IsNullOrWhiteSpace($Identity.ExecutionAlias)) {
		$aliasCommand = Get-Command $Identity.ExecutionAlias -ErrorAction SilentlyContinue
		if ($aliasCommand -and (Test-Path -LiteralPath $aliasCommand.Source)) {
			return $aliasCommand.Source
		}
	}

	if ($InstalledPackage -and -not [string]::IsNullOrWhiteSpace($InstalledPackage.InstallLocation)) {
		$packagedExe = Join-Path $InstalledPackage.InstallLocation "Files.exe"
		if (Test-Path -LiteralPath $packagedExe) {
			return $packagedExe
		}
	}

	throw "Could not resolve a Files Pro launcher path. Pass -LauncherPath explicitly."
}

$package = Resolve-Path $PackagePath
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$backupRoot = Join-Path $env:LOCALAPPDATA "FilesPro\InstallBackups\$(Get-Date -Format yyyyMMdd-HHmmss)"
$identity = Get-MsixIdentity $package

Write-Plan "Package: $package"
Write-Plan "Identity: $($identity.Name) $($identity.Version) $($identity.Publisher)"
if (-not [string]::IsNullOrWhiteSpace($identity.ExecutionAlias)) {
	Write-Plan "Execution alias: $($identity.ExecutionAlias)"
}
Write-Plan "Backups: $backupRoot"
Write-Plan "Shell registration: $RegisterShell"

if (-not $Apply) {
	Write-Plan "Dry run only. Re-run with -Apply to install the package."
	exit 0
}

New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
$existingPackages = Get-AppxPackage -Name $identity.Name
$existingPackages | Select-Object Name, PackageFullName, InstallLocation |
	ConvertTo-Json -Depth 4 |
	Set-Content (Join-Path $backupRoot "existing-files-packages.json")

$manifestPath = Join-Path $backupRoot "install-manifest.json"
$manifest = [ordered]@{
	CreatedAt = (Get-Date).ToString("O")
	PackagePath = [string]$package
	PackageIdentity = $identity
	ExistingPackageFullNames = @($existingPackages | ForEach-Object { $_.PackageFullName })
	RegisterShell = [bool]$RegisterShell
	LauncherPath = $LauncherPath
	ShellApplied = $false
	ShellBackupDirectory = $null
	ShellManifestPath = $null
	ShellRollbackMode = $null
	InstalledPackageFullName = $null
}
$manifest | ConvertTo-Json -Depth 8 | Set-Content $manifestPath
Write-Plan "Rollback manifest: $manifestPath"

Add-AppxPackage -Path $package
Write-Plan "Installed package."

$installedPackage = Get-AppxPackage -Name $identity.Name | Sort-Object PackageFullName -Descending | Select-Object -First 1
if ($installedPackage) {
	$manifest.InstalledPackageFullName = $installedPackage.PackageFullName
	$manifest | ConvertTo-Json -Depth 8 | Set-Content $manifestPath
	Write-Plan "Installed package full name: $($installedPackage.PackageFullName)"
}

if ($RegisterShell) {
	$resolvedLauncherPath = Resolve-LauncherPath $identity $installedPackage $LauncherPath
	$beforeShellRegistration = Get-Date
	& (Join-Path $repoRoot "tools\files-pro-shell\Register-FilesProShell.ps1") -Apply -CreateShortcuts -LauncherPath $resolvedLauncherPath
	$shellManifest = Get-ChildItem -LiteralPath (Join-Path $env:LOCALAPPDATA "FilesPro\ShellBackups") -Recurse -Filter "shell-manifest.json" -ErrorAction SilentlyContinue |
		Where-Object { $_.LastWriteTime -ge $beforeShellRegistration.AddSeconds(-2) } |
		Sort-Object LastWriteTime -Descending |
		Select-Object -First 1

	$manifest.LauncherPath = $resolvedLauncherPath
	$manifest.ShellApplied = $true
	$manifest.ShellRollbackMode = "RestoreBackup"
	if ($shellManifest) {
		$manifest.ShellBackupDirectory = Split-Path -Parent $shellManifest.FullName
		$manifest.ShellManifestPath = $shellManifest.FullName
	}
	$manifest | ConvertTo-Json -Depth 8 | Set-Content $manifestPath
}

Write-Plan "Rollback package: pwsh .\tools\files-pro-package\Uninstall-FilesProPackage.ps1 -ManifestPath `"$manifestPath`" -Apply"
Write-Plan "Rollback package and shell: pwsh .\tools\files-pro-package\Uninstall-FilesProPackage.ps1 -ManifestPath `"$manifestPath`" -RollbackShell -Apply"
