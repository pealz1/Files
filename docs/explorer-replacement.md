# Files Pro Explorer Replacement Plan

Files Pro supports two per-user integration levels:

- folder `open` and `explore` verbs route to Files Pro,
- `Win+E` routes to Files Pro,
- desktop and Start Menu shortcuts point to Files Pro,
- optional current-user `Winlogon\Shell` replacement routes shell startup through Files Pro shell host,
- rollback restores the per-user registry overrides.

The scripts in `tools/files-pro-shell` are intentionally dry-run by default. They do not move personal files, delete files, or alter `HKLM`.

## Register

Build and install a signed or locally trusted package first. The package installer can apply shell integration and record package plus shell rollback metadata in one install manifest:

```powershell
pwsh .\tools\files-pro-package\Install-FilesProPackage.ps1 -PackagePath .\artifacts\files-pro-package\Files.App_4.1.4.0_x64_Test\Files.App_4.1.4.0_x64.msix -RegisterShell
pwsh .\tools\files-pro-package\Install-FilesProPackage.ps1 -PackagePath .\artifacts\files-pro-package\Files.App_4.1.4.0_x64_Test\Files.App_4.1.4.0_x64.msix -RegisterShell -Apply
```

For an already installed package, run shell registration directly against the packaged execution alias:

```powershell
$launcher = (Get-Command files-dev.exe).Source
pwsh .\tools\files-pro-shell\Register-FilesProShell.ps1 -Apply -CreateShortcuts -LauncherPath $launcher
```

To replace Explorer as the current user's shell on next sign-in and start the Files Pro shell host in the current session:

```powershell
$launcher = (Get-Command files-dev.exe).Source
pwsh .\tools\files-pro-shell\Register-FilesProShell.ps1 -Apply -CreateShortcuts -LauncherPath $launcher -ReplaceExplorerShell -StartShellHost
```

To also stop current `explorer.exe` processes after the shell host starts:

```powershell
pwsh .\tools\files-pro-shell\Register-FilesProShell.ps1 -Apply -CreateShortcuts -LauncherPath $launcher -ReplaceExplorerShell -StartShellHost -StopExplorer
```

Windows can respawn the already-running session's original `explorer.exe` shell even after this command. The reliable shell replacement boundary is the next sign-in, where the current user's `Winlogon\Shell` value points directly to Files Pro shell host.

The current local installation uses:

```text
%LOCALAPPDATA%\Microsoft\WindowsApps\files-dev.exe
```

Use `-LauncherPath` for a custom package/install layout. The package installer auto-detects the execution alias from the MSIX manifest when `-LauncherPath` is not supplied.

## Roll Back

```powershell
pwsh .\tools\files-pro-shell\Unregister-FilesProShell.ps1
pwsh .\tools\files-pro-shell\Unregister-FilesProShell.ps1 -Apply
pwsh .\tools\files-pro-shell\Unregister-FilesProShell.ps1 -Apply -RestoreBackup
pwsh .\tools\files-pro-shell\Unregister-FilesProShell.ps1 -Apply -RestoreBackup -StartExplorer
pwsh .\tools\files-pro-package\Uninstall-FilesProPackage.ps1
pwsh .\tools\files-pro-package\Uninstall-FilesProPackage.ps1 -RollbackShell
pwsh .\tools\files-pro-package\Uninstall-FilesProPackage.ps1 -RollbackShell -Apply
```

Backups are written under:

```text
%LOCALAPPDATA%\FilesPro\ShellBackups
%LOCALAPPDATA%\FilesPro\InstallBackups
%LOCALAPPDATA%\FilesPro\ShellHost
```

## Safety Rules

- Do not run the register script until the app launches successfully.
- Keep a rollback script available before applying shell integration.
- Use per-user registry keys by default.
- The shell host restores Explorer automatically if Files Pro cannot be resolved or repeatedly fails to stay running.
- To emergency-disable the shell host, create `%LOCALAPPDATA%\FilesPro\ShellHost\disable-files-pro-shell.flag` and start a new shell host or sign out/in.
