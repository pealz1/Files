param(
	[string]$LauncherPath = "$env:LOCALAPPDATA\FilesPro\FilesProLauncher.exe",
	[switch]$Apply,
	[switch]$ReplaceExplorerShell
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$register = Join-Path $repoRoot "tools\files-pro-shell\Register-FilesProShell.ps1"
$unregister = Join-Path $repoRoot "tools\files-pro-shell\Unregister-FilesProShell.ps1"

Write-Host "[Files Pro rollback test] This tests only per-user shell registry overrides."
if ($ReplaceExplorerShell) {
	Write-Host "[Files Pro rollback test] This will also test the per-user Winlogon shell override and restore it."
}
else {
	Write-Host "[Files Pro rollback test] It does not replace explorer.exe as the Windows shell."
}

if (-not $Apply) {
	& $register -LauncherPath $LauncherPath -ReplaceExplorerShell:$ReplaceExplorerShell
	& $unregister -RestoreBackup
	Write-Host "[Files Pro rollback test] Dry run complete. Re-run with -Apply only after validating the launcher."
	exit 0
}

if (-not (Test-Path -LiteralPath $LauncherPath)) {
	throw "LauncherPath does not exist: $LauncherPath"
}

& $register -Apply -LauncherPath $LauncherPath -ReplaceExplorerShell:$ReplaceExplorerShell
& $unregister -Apply -RestoreBackup -StartExplorer:$ReplaceExplorerShell
Write-Host "[Files Pro rollback test] Apply and rollback completed."
