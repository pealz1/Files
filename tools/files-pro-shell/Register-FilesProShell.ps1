param(
	[switch]$Apply,
	[switch]$CreateShortcuts,
	[switch]$ReplaceExplorerShell,
	[switch]$StartShellHost,
	[switch]$StopExplorer,
	[string]$LauncherPath,
	[string]$ShellHostPath
)

$ErrorActionPreference = "Stop"

function Write-Plan($Message) {
	Write-Host "[Files Pro shell] $Message"
}

function Backup-Key($Key, $BackupDirectory) {
	$leaf = ($Key -replace '[\\/:*?"<>|{}]', '_')
	$path = Join-Path $BackupDirectory "$leaf.reg"
	& reg.exe export $Key $path /y *> $null
	if ($LASTEXITCODE -eq 0) {
		Write-Plan "Backed up $Key to $path"
		return [pscustomobject]@{
			Key = $Key
			Path = $path
			Existed = $true
		}
	}

	return [pscustomobject]@{
		Key = $Key
		Path = $path
		Existed = $false
	}
}

function Set-Command($Key, $Command) {
	& reg.exe add $Key /ve /t REG_EXPAND_SZ /d $Command /f | Out-Null
	if ($LASTEXITCODE -ne 0) {
		throw "Failed to set default value for $Key"
	}

	& reg.exe add $Key /v DelegateExecute /t REG_SZ /d "" /f | Out-Null
	if ($LASTEXITCODE -ne 0) {
		throw "Failed to set DelegateExecute for $Key"
	}
}

function Set-RegistryValue($Key, $Name, $Type, $Data) {
	& reg.exe add $Key /v $Name /t $Type /d $Data /f | Out-Null
	if ($LASTEXITCODE -ne 0) {
		throw "Failed to set $Name for $Key"
	}
}

function New-Shortcut($ShortcutPath, $TargetPath) {
	$shell = New-Object -ComObject WScript.Shell
	$shortcut = $shell.CreateShortcut($ShortcutPath)
	$shortcut.TargetPath = $TargetPath
	$shortcut.WorkingDirectory = Split-Path -Parent $TargetPath
	$shortcut.IconLocation = $TargetPath
	$shortcut.Save()
}

function Resolve-LauncherPath($ExplicitLauncherPath) {
	if (-not [string]::IsNullOrWhiteSpace($ExplicitLauncherPath)) {
		return $ExplicitLauncherPath
	}

	$alias = Get-Command "files-dev.exe" -ErrorAction SilentlyContinue
	if ($alias -and (Test-Path -LiteralPath $alias.Source)) {
		return $alias.Source
	}

	$legacyLauncher = Join-Path $env:LOCALAPPDATA "Files\Files.App.Launcher.exe"
	if (Test-Path -LiteralPath $legacyLauncher) {
		return $legacyLauncher
	}

	throw "Could not resolve a Files Pro launcher path. Install Files Pro first or pass -LauncherPath explicitly."
}

function Install-ShellHost($ExplicitShellHostPath) {
	$destination = Resolve-ShellHostPath $ExplicitShellHostPath

	$source = Join-Path $PSScriptRoot "FilesProShellHost.ps1"
	if (-not (Test-Path -LiteralPath $source)) {
		throw "Files Pro shell host script was not found: $source"
	}

	$destinationRoot = Split-Path -Parent $destination
	New-Item -ItemType Directory -Path $destinationRoot -Force | Out-Null
	Copy-Item -LiteralPath $source -Destination $destination -Force
	return $destination
}

function Resolve-ShellHostPath($ExplicitShellHostPath) {
	if (-not [string]::IsNullOrWhiteSpace($ExplicitShellHostPath)) {
		return $ExplicitShellHostPath
	}

	return (Join-Path $env:LOCALAPPDATA "FilesPro\ShellHost\FilesProShellHost.ps1")
}

function Get-ShellHostCommand($ShellHostPath, $LauncherPath) {
	$powershellPath = Join-Path $env:WINDIR "System32\WindowsPowerShell\v1.0\powershell.exe"
	return "`"$powershellPath`" -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$ShellHostPath`" -LauncherPath `"$LauncherPath`""
}

function Stop-FilesProShellHosts {
	$shellHostProcesses = Get-CimInstance Win32_Process |
		Where-Object { $_.Name -eq "powershell.exe" -and $_.CommandLine -like "*-File*FilesProShellHost.ps1*" }
	foreach ($process in $shellHostProcesses) {
		Invoke-CimMethod -InputObject $process -MethodName Terminate | Out-Null
		Write-Plan "Stopped existing Files Pro shell host process $($process.ProcessId)."
	}
}

$LauncherPath = Resolve-LauncherPath $LauncherPath

$folderOpenKey = "HKCU\SOFTWARE\Classes\Folder\shell\open\command"
$folderExploreKey = "HKCU\SOFTWARE\Classes\Folder\shell\explore\command"
$winEKey = "HKCU\SOFTWARE\Classes\CLSID\{52205fd8-5dfb-447d-801a-d0b52f2e83e1}\shell\opennewwindow\command"
$winlogonKey = "HKCU\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon"
$backupDirectory = Join-Path $env:LOCALAPPDATA "FilesPro\ShellBackups\$(Get-Date -Format yyyyMMdd-HHmmss)"
$openCommand = "`"$LauncherPath`" `"%1`""
$winECommand = "`"$LauncherPath`""
$resolvedShellHostPath = $null
$shellHostCommand = $null
if ($ReplaceExplorerShell -or $StartShellHost) {
	$resolvedShellHostPath = if ($Apply) { Install-ShellHost $ShellHostPath } else { Resolve-ShellHostPath $ShellHostPath }
	$shellHostCommand = Get-ShellHostCommand $resolvedShellHostPath $LauncherPath
}

Write-Plan "Launcher path: $LauncherPath"
Write-Plan "Will set Folder open/explore verbs and Win+E to Files Pro."
if ($ReplaceExplorerShell) {
	Write-Plan "Will replace the current user's Winlogon shell with Files Pro shell host."
	Write-Plan "Shell host command: $shellHostCommand"
}
else {
	Write-Plan "Will not replace explorer.exe as the Windows shell."
}
if ($StopExplorer) {
	Write-Plan "Will stop current explorer.exe processes after Files Pro shell host starts."
}

if (-not $Apply) {
	Write-Plan "Dry run only. Re-run with -Apply to write per-user registry keys."
	exit 0
}

if (-not (Test-Path -LiteralPath $LauncherPath)) {
	throw "LauncherPath does not exist: $LauncherPath"
}

New-Item -ItemType Directory -Path $backupDirectory -Force | Out-Null
$backups = @(
	Backup-Key "HKCU\SOFTWARE\Classes\Folder\shell\open" $backupDirectory
	Backup-Key "HKCU\SOFTWARE\Classes\Folder\shell\explore" $backupDirectory
	Backup-Key "HKCU\SOFTWARE\Classes\CLSID\{52205fd8-5dfb-447d-801a-d0b52f2e83e1}" $backupDirectory
)
if ($ReplaceExplorerShell) {
	$backups += Backup-Key $winlogonKey $backupDirectory
}

$manifestPath = Join-Path $backupDirectory "shell-manifest.json"
[ordered]@{
	CreatedAt = (Get-Date).ToString("O")
	LauncherPath = $LauncherPath
	ReplaceExplorerShell = [bool]$ReplaceExplorerShell
	ShellHostPath = $resolvedShellHostPath
	ShellHostCommand = $shellHostCommand
	FolderOpenCommand = $openCommand
	FolderExploreCommand = $openCommand
	WinECommand = $winECommand
	Backups = $backups
} | ConvertTo-Json -Depth 8 | Set-Content $manifestPath
Write-Plan "Shell rollback manifest: $manifestPath"

Set-Command $folderOpenKey $openCommand
Set-Command $folderExploreKey $openCommand
Set-Command $winEKey $winECommand
if ($ReplaceExplorerShell) {
	Set-RegistryValue $winlogonKey "Shell" "REG_EXPAND_SZ" $shellHostCommand
	Set-RegistryValue $winlogonKey "AutoRestartShell" "REG_DWORD" "0"
}

if ($CreateShortcuts) {
	$desktop = [Environment]::GetFolderPath("Desktop")
	$startMenu = Join-Path ([Environment]::GetFolderPath("StartMenu")) "Programs"
	New-Shortcut (Join-Path $desktop "Files Pro.lnk") $LauncherPath
	New-Shortcut (Join-Path $startMenu "Files Pro.lnk") $LauncherPath
	Write-Plan "Created desktop and Start Menu shortcuts."
}

if ($StartShellHost -or $ReplaceExplorerShell) {
	$powershellPath = Join-Path $env:WINDIR "System32\WindowsPowerShell\v1.0\powershell.exe"
	Stop-FilesProShellHosts
	Start-Process -FilePath $powershellPath -WindowStyle Hidden -ArgumentList @(
		"-NoProfile",
		"-ExecutionPolicy",
		"Bypass",
		"-File",
		$resolvedShellHostPath,
		"-LauncherPath",
		$LauncherPath
	)
	Write-Plan "Started Files Pro shell host for the current session."
}

if ($StopExplorer) {
	Get-Process explorer -ErrorAction SilentlyContinue | Stop-Process -Force
	Write-Plan "Stopped current explorer.exe processes."
}

Write-Plan "Applied per-user Files Pro shell integration."
Write-Plan "Rollback: pwsh .\tools\files-pro-shell\Unregister-FilesProShell.ps1 -Apply"
