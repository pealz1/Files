param(
	[string]$BackupDirectory,
	[switch]$RestoreBackup,
	[switch]$Apply,
	[switch]$StartExplorer
)

$ErrorActionPreference = "Stop"

function Write-Plan($Message) {
	Write-Host "[Files Pro shell rollback] $Message"
}

$keysToRemove = @(
	"Registry::HKEY_CURRENT_USER\SOFTWARE\Classes\Folder\shell\open\command",
	"Registry::HKEY_CURRENT_USER\SOFTWARE\Classes\Folder\shell\explore\command",
	"Registry::HKEY_CURRENT_USER\SOFTWARE\Classes\CLSID\{52205fd8-5dfb-447d-801a-d0b52f2e83e1}"
)
$winlogonKey = "Registry::HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon"

Write-Plan "Will remove the per-user Folder open/explore command overrides and Win+E CLSID override."
Write-Plan "Will remove the per-user Winlogon Shell override when present."
if ($RestoreBackup) {
	Write-Plan "Will restore previous per-user shell registry keys from the latest shell backup manifest when available."
}
Write-Plan "Will not edit HKLM/HKCR."

if (-not $Apply) {
	Write-Plan "Dry run only. Re-run with -Apply to remove the overrides."
	exit 0
}

foreach ($key in $keysToRemove) {
	if (Test-Path -LiteralPath $key) {
		Remove-Item -LiteralPath $key -Recurse -Force
		Write-Plan "Removed $key"
	}
}

if (Test-Path -LiteralPath $winlogonKey) {
	Remove-ItemProperty -LiteralPath $winlogonKey -Name "Shell" -Force -ErrorAction SilentlyContinue
	Remove-ItemProperty -LiteralPath $winlogonKey -Name "AutoRestartShell" -Force -ErrorAction SilentlyContinue
	Write-Plan "Removed per-user Winlogon shell values."
}

$shellHostProcesses = Get-CimInstance Win32_Process |
	Where-Object { $_.Name -eq "powershell.exe" -and $_.CommandLine -like "*-File*FilesProShellHost.ps1*" }
foreach ($process in $shellHostProcesses) {
	Invoke-CimMethod -InputObject $process -MethodName Terminate | Out-Null
	Write-Plan "Stopped Files Pro shell host process $($process.ProcessId)."
}

if ($RestoreBackup) {
	if ([string]::IsNullOrWhiteSpace($BackupDirectory)) {
		$backupRoot = Join-Path $env:LOCALAPPDATA "FilesPro\ShellBackups"
		if (Test-Path -LiteralPath $backupRoot) {
			$latestManifest = Get-ChildItem -LiteralPath $backupRoot -Recurse -Filter "shell-manifest.json" |
				Sort-Object LastWriteTime -Descending |
				Select-Object -First 1
			if ($latestManifest) {
				$BackupDirectory = Split-Path -Parent $latestManifest.FullName
			}
		}
	}

	if (-not [string]::IsNullOrWhiteSpace($BackupDirectory)) {
		$manifestPath = Join-Path $BackupDirectory "shell-manifest.json"
		if (Test-Path -LiteralPath $manifestPath) {
			$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
			foreach ($backup in $manifest.Backups) {
				if ($backup.Existed -and (Test-Path -LiteralPath $backup.Path)) {
					& reg.exe import $backup.Path | Out-Null
					if ($LASTEXITCODE -ne 0) {
						throw "Failed to import $($backup.Path)"
					}
					Write-Plan "Restored $($backup.Key)"
				}
			}
		}
		else {
			Write-Plan "Backup manifest was not found: $manifestPath"
		}
	}
	else {
		Write-Plan "No shell backup directory was found."
	}
}

if ($StartExplorer) {
	Start-Process explorer.exe
	Write-Plan "Started explorer.exe."
}

Write-Plan "Rollback complete. Sign out/in if Windows keeps an old shell association cached."
