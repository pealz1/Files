param(
	[string]$LauncherPath,
	[int]$MonitorSeconds = 5,
	[int]$MaxRestarts = 5,
	[int]$RestartWindowSeconds = 300
)

$ErrorActionPreference = "Continue"

$stateRoot = Join-Path $env:LOCALAPPDATA "FilesPro\ShellHost"
$logPath = Join-Path $stateRoot "FilesProShellHost.log"
$disableFlagPath = Join-Path $stateRoot "disable-files-pro-shell.flag"
$winlogonKey = "HKCU:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon"

New-Item -ItemType Directory -Path $stateRoot -Force | Out-Null

function Write-ShellHostLog($Message) {
	$line = "{0} {1}" -f (Get-Date).ToString("O"), $Message
	Add-Content -LiteralPath $logPath -Value $line
}

function Resolve-FilesProLauncher($ExplicitLauncherPath) {
	if (-not [string]::IsNullOrWhiteSpace($ExplicitLauncherPath) -and (Test-Path -LiteralPath $ExplicitLauncherPath)) {
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

	return $null
}

function Restore-ExplorerShell($Reason) {
	Write-ShellHostLog "Restoring Explorer shell: $Reason"
	New-Item -Path $winlogonKey -Force | Out-Null
	Remove-ItemProperty -Path $winlogonKey -Name "Shell" -Force -ErrorAction SilentlyContinue
	Remove-ItemProperty -Path $winlogonKey -Name "AutoRestartShell" -Force -ErrorAction SilentlyContinue
	Start-Process explorer.exe
}

function Test-FilesProRunning {
	$filesProcesses = Get-Process -Name "Files" -ErrorAction SilentlyContinue
	foreach ($process in $filesProcesses) {
		try {
			if ($process.Path -like "*\FilesDev_*") {
				return $true
			}
		}
		catch {
			return $true
		}
	}

	return $false
}

Write-ShellHostLog "Files Pro shell host starting."
$resolvedLauncher = Resolve-FilesProLauncher $LauncherPath
if ([string]::IsNullOrWhiteSpace($resolvedLauncher)) {
	Restore-ExplorerShell "Files Pro launcher could not be resolved."
	exit 1
}

Write-ShellHostLog "Launcher: $resolvedLauncher"
$restartTimes = New-Object System.Collections.Generic.List[datetime]

while ($true) {
	if (Test-Path -LiteralPath $disableFlagPath) {
		Restore-ExplorerShell "Disable flag was found at $disableFlagPath."
		Remove-Item -LiteralPath $disableFlagPath -Force -ErrorAction SilentlyContinue
		exit 0
	}

	if (-not (Test-FilesProRunning)) {
		$now = Get-Date
		for ($i = $restartTimes.Count - 1; $i -ge 0; $i--) {
			if (($now - $restartTimes[$i]).TotalSeconds -gt $RestartWindowSeconds) {
				$restartTimes.RemoveAt($i)
			}
		}

		if ($restartTimes.Count -ge $MaxRestarts) {
			Restore-ExplorerShell "Files Pro exceeded $MaxRestarts restarts in $RestartWindowSeconds seconds."
			exit 2
		}

		Write-ShellHostLog "Starting Files Pro."
		try {
			Start-Process -FilePath $resolvedLauncher
			$restartTimes.Add($now)
		}
		catch {
			Write-ShellHostLog "Failed to start Files Pro: $($_.Exception.Message)"
			$restartTimes.Add($now)
		}
	}

	Start-Sleep -Seconds $MonitorSeconds
}
