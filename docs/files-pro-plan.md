# Files Pro Fork Plan

## Current Upstream State

- Repository: `files-community/Files`
- Local fork path: `C:\Users\pealz\OneDrive\Documents\New project\Files-Pro`
- Working branch: `codex/files-pro-milestone1`
- Upstream commit inspected: `47ab4f5fcd4b62da9457944e3a9d170e97cb2b99`
- Latest GitHub release inspected: `v4.1.3`, published June 10, 2026

## Build Requirements

- Solution: `Files.slnx`
- Main app project: `src\Files.App\Files.App.csproj`
- Target framework: `.NET 10` via `global.json` SDK `10.0.102` with `rollForward=latestMajor`
- Windows target: `net10.0-windows10.0.26100.0`
- Minimum Windows version: `10.0.19041.0`
- Windows SDK build tools package: `Microsoft.Windows.SDK.BuildTools` `10.0.28000.1839`
- Windows App SDK: `2.2.0`
- UI stack: WinUI 3, packaged desktop app with MSIX tooling enabled
- CI source of truth: `.github/workflows/ci.yml`
- Local verification command:

```powershell
dotnet restore src\Files.App\Files.App.csproj -p:Platform=x64 -p:Configuration=Debug
dotnet build src\Files.App\Files.App.csproj -c Debug -p:Platform=x64 -p:AppxBundle=Never -p:GenerateAppxPackageOnBuild=false --no-restore
```

Local note: the Visual Studio BuildTools `MSBuild.exe` path on this machine did not resolve `Microsoft.NET.Sdk`; `dotnet build` reached XAML Pass2. A clean upstream worktree failed with the same `Microsoft.WindowsAppSDK.WinUI 2.2.1` `WMC9999` XAML compiler resource error, so the remaining full-build blocker is local/upstream tooling rather than the milestone code.

## Architecture Map

- App shell: `MainPage`, `ShellPanesPage`, `ModernShellPage`, and `ColumnShellPage` manage tabs, panes, virtual pages, path routing, and folder layouts.
- UI pages: `HomePage`, layout pages, settings pages, release notes, dialogs, preview panes, and reusable WinUI controls live under `src\Files.App\Views`, `UserControls`, and `ViewModels`.
- Storage abstraction: app storage is split between `Files.App.Storage`, `Files.Core.Storage`, OwlCore storage interfaces, native storage helpers, archive storage, FTP storage, and shell-backed filesystem operations.
- Shell integration: `NavigationHelpers`, `Win32Helper`, context menus, app launcher projects, default-file-manager registry assets, and server/background projects support shell launch and Windows integration.
- Background work: startup loads quick access, libraries, cloud drives, WSL, tags, jump list, add-item service, and context menu warmup via DI services in `AppLifecycleHelper`.
- Search: folder search is routed through `ShellViewModel.SearchAsync` and currently needs stronger cancellation and indexing isolation before large search features are added.
- Tags: file tag manager, file tag service, settings, sidebar section, and tag search virtual paths are already present.
- Archive support: archive storage service, SevenZipSharp/SharpZipLib/DiscUtils packages, archive preview, and archive actions are already in the app.
- Git actions: `GitHelpers`, `ShellViewModel` git directory detection, branch display, checkout, fetch, and LibGit2Sharp support are already present.
- Settings: user settings are layered through per-area settings services and view models registered in `AppLifecycleHelper`.
- Performance-sensitive views: `DetailsLayoutPage`, `GridLayoutPage`, `ColumnsLayoutPage`, thumbnails, previews, shell context menu loading, folder watchers, and search result pages are the high-risk areas for CPU, memory, cancellation, and virtualization work.

## Issue Overlap

High-signal open issues reviewed on June 21, 2026:

- `#18619` Files randomly uses 20-30% CPU when fully closed.
- `#18618`, `#18592`, `#18543` crash reports, including grid-view folder crashes.
- `#18588` default file manager configuration confusion.
- `#18578`, `#18381`, `#18344`, `#18303`, `#18268`, `#18202` network drive/FTP/network crash and naming problems.
- `#18533`, `#18396` archive and large ZIP crash gaps.
- `#18522`, `#18300`, `#18294` copy/move/file operation correctness problems.
- `#18503` thumbnail rendering problems.
- `#18393` search keeps querying after search/tab exit.
- `#18337`, `#18289` grid/selection high CPU and responsiveness problems.
- `#18395` command-list/omnibar duplicate entries.
- `#18556`, `#18586`, `#18353` sidebar expansion/grouping gaps that overlap smart sections.

## Product Direction

The fork should keep Files as a daily-driver file manager while adding power-user features behind focused, cancellable modules:

- `Files.App.ProjectDiscovery`
- `Files.App.Cleanup`
- `Files.App.StorageAnalysis`
- `Files.App.Indexing`
- `Files.App.CommandPalette`
- `Files.App.Roblox`
- `Files.App.Diagnostics`

Every long operation must expose progress, support cancellation, and stay read-only until the user reviews an explicit plan.

## Milestone 1

This milestone adds a usable read-only vertical slice:

- A `Files Pro` virtual page reachable from the sidebar and tabs.
- Project discovery for selected roots.
- Roblox/Lua file detection.
- A versioned SQLite filename/content index with basic power-user query syntax.
- Downloads cleanup dry-run classification.
- Downloads cleanup dry-run move plans with exact source/destination paths.
- Large file scanning for a chosen folder.
- Staged duplicate detection using same-size grouping, partial hash, then full hash.
- Wasted-space review hints for generated folders, old installers, game archives, and extracted archive pairs.
- A command palette service and dashboard list for power-user actions.
- Progress and cancellation for scans.
- Open path and copy path actions.

Explicitly out of scope for milestone 1:

- NTFS MFT/USN scanning.
- Treemap rendering.
- Actual cleanup move/delete execution.
- Executing command-palette actions beyond listing/searching them.
- Applying Explorer replacement registry changes automatically.

## Safety Position

No personal files are moved, deleted, renamed, or reorganized in this milestone. Explorer replacement is represented by dry-run-first scripts in `tools\files-pro-shell`; they must not be applied until the package/launcher path is verified and rollback is available.

## Next Milestone

- Add smart saved searches and command palette execution backed by the index.
- Add storage analyzer CSV/JSON export.
- Add Downloads cleanup review plans with dry-run diffs and guarded move execution.
- Add project pinning, project categories, dirty-repo grouping, duplicate project copy detection, and IDE/GitHub action buttons.
- Add treemap visualization and an NTFS MFT/USN fast path behind explicit capability checks.
- Profile startup, large folders, thumbnails, search cancellation, and app-close CPU.
