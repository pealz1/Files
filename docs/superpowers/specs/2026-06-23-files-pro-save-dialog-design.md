# Files Pro custom Save dialog — design

Date: 2026-06-23
Status: Approved (design) + edge cases added; pending implementation plan
Branch: `codex/files-pro-milestone1`

## Problem

Files Pro registers itself as the Windows **Save** dialog by overriding the per-user
CLSID `{C0B4E2F3-BA21-4773-8DBA-335EC946EB8B}` (`CLSID_FileSaveDialog`) in
`HKCU\Software\Classes\CLSID\...\InprocServer32`, pointing at
`Files.App.SaveDialog64.dll`. When an app (e.g. Discord "Save image as…") invokes the
save dialog, the native COM server launches the Files Pro window via the
`files-dev:?cmd=...` protocol with `-directory`, `-outputpath`, and `-select <name>`.

The app side has **no save UI**. The only commit path is `App.Window_Closed`, which writes
the paths of the **currently selected existing items** to the output file and signals the
`FILEDIALOG` event. That is an *Open*-dialog behavior. For saving a *new* file there is no
filename box and no Save button, the suggested name (`-select`, non-rooted) is ignored, and
closing the window returns an empty result, so the native side returns
`ERROR_CANCELLED` and nothing is saved.

Runtime evidence confirming this is a feature gap (not broken plumbing):
- `Desktop/save_dialog.txt` trace: `SetFileName: 5Julygamer.png`, `SetFileTypes: 2`,
  `SetFolder: C:\Users\pealz\Downloads`, `Show`, then
  `Invoking: files-dev.exe -directory "...\Downloads" -outputpath "...tmp" -select "5Julygamer.png"`.
- Packaged `debug.log`: protocol activation → `InitializeApplicationAsync completed. Root content: MainPage` (window renders fine).

The fork's dialog code is byte-identical to upstream `main` (the `custom-save-dialog` branch
merged); the save UI was simply never built upstream.

## Goal

Implement a full-parity, Files-styled Save dialog: a docked "Save bar" at the bottom of the
Files Pro window with a File name box, a "Save as type" dropdown, a New Folder button, and
Save / Cancel, with correct extension handling, overwrite confirmation, remembered last-used
folder, and file-type-index round-tripping. Open-dialog behavior is unchanged.

## Non-goals

- Changing the Open dialog UX.
- Replacing the `files-dev:` protocol / IPC mechanism.
- HKLM / machine-wide registration (stays per-user HKCU).

## Native side — `src/Files.App.SaveDialog/FilesSaveDialog.{cpp,h}`

Additive, minimal:

1. **Store filters.** `SetFileTypes(cFileTypes, rgFilterSpec)` currently discards the specs.
   Store them as `std::vector<std::pair<std::wstring,std::wstring>> _fileTypes`
   (friendly name, pattern). `SetFileTypeIndex(iFileType)` → store `_fileTypeIndex`
   (1-based, as Windows uses).
2. **Launch contract.** In `Show()`, build args:
   ```
   "<exe>" -directory "<folder>" -outputpath "<temp>" -savedialog \
           -saveas "<suggestedName>" \
           -filetypes "<Name1|pat1|Name2|pat2|...>" -filetypeindex <N>
   ```
   - `<suggestedName>` = existing `_initName`.
   - Filters serialized as a `|`-delimited list (name, pattern pairs). A literal `|` in a
     pattern is not expected; if present, replace with a space when serializing.
   - Enlarge the `args` buffer (`TCHAR args[8192]`) since filters lengthen the string. The
     whole args string is still hex-encoded by the existing `wstring_to_utf8_hex` path, so
     special characters remain safe inside the URI.
3. **Result parsing.** After the app returns, read `_outputPath`:
   - Line 1 = chosen full path (existing behavior → `_selectedItem`).
   - Optional line starting `index=` → parse into `_fileTypeIndex` so `GetFileTypeIndex`
     round-trips. Keep tolerant: missing line ⇒ leave index as-is.
   Existing post-processing (`CreateFile(... OPEN_ALWAYS ...)`, `OnFileOk`, `GetResult`
   via `SHCreateItemFromParsingName`) is unchanged.

The Open dialog (`FilesOpenDialog.cpp`) is untouched.

## App side — C#

### Command-line parsing
- `Data/Enums/ParsedCommandType.cs`: add `SaveDialog`, `SaveAs`, `FileTypes`, `FileTypeIndex`.
- `Utils/CommandLine/CommandLineParser.cs`: parse the four new tokens.
- `MainWindow.xaml.cs` `InitializeFromCmdLineArgsAsync`: collect them into a request and set
  app state. `-directory` still drives navigation; `-outputpath` still sets `App.OutputPath`.

### App state
- `App.IsSaveDialog : bool`.
- `App.SaveDialogRequest` record: `SuggestedName`, `IReadOnlyList<(string Display, string Pattern)> FileTypes`, `int TypeIndex`.

### View model + control
- `SaveDialogViewModel` (ObservableObject): `FileName`, `FileTypes`, `SelectedFileType`,
  `IsActive`, `SaveCommand`, `CancelCommand`, `NewFolderCommand`. Prefilled from
  `App.SaveDialogRequest`.
- `SaveDialogBar` user control: `File name:` `TextBox`, `Save as type:` `ComboBox`,
  `New Folder` button, `Save` (accent) + `Cancel` buttons. Docked in a new bottom row of
  `MainPage.xaml`, visible only when `IsSaveDialog`.
- Keyboard: Enter in the box = Save; Esc = Cancel.

### Commit logic (the only commit path in save mode)
On **Save**:
1. Resolve the active pane's folder: `ShellPanesPage.ActivePane…ShellViewModel.WorkingDirectory`.
2. Apply extension: if the typed name has no extension and the selected type has a concrete
   pattern (e.g. `*.png`), append `.png`. `*.*` / "All files" ⇒ leave as typed. Respect an
   extension the user already typed.
3. Compose `fullPath = Combine(folder, name)`.
4. If `fullPath` exists **and** `_fos` has `FOS_OVERWRITEPROMPT` (the trace showed
   `fos = 2114` = `0x842`, which includes `FOS_OVERWRITEPROMPT 0x2`): show a "Replace
   existing file?" `ContentDialog`; on No, abort the commit (stay open).
5. Write `fullPath` (line 1) and `index=<selectedIndex>` (line 2) to `App.OutputPath`.
6. Signal the `FILEDIALOG` event (`CreateEvent`/`SetEvent`, same as today).
7. Set a `committed` guard, set `App.OutputPath = null`, close the window.

On **Cancel** or window-**X**: write nothing; signal `FILEDIALOG` (so the native side
unblocks) — native sees an empty result and returns `ERROR_CANCELLED`.

### `Window_Closed` guard
- If `IsSaveDialog`: do **not** run the existing "write `SelectedItems`" branch. (Otherwise
  closing the dialog could write a highlighted file's path and overwrite it.) If already
  committed, do nothing. If not committed, treat as cancel (empty result + signal event).
- If not a save dialog: existing open-dialog behavior is preserved exactly.

### Parity extras
- **New Folder**: create a folder in the current directory, navigate into it.
- **Remember last-used folder**: persist last successful save folder in `LocalSettings`;
  prefer it only when the caller did not provide an explicit `-directory`.
- **Type-index round-trip**: the selected `ComboBox` index (1-based) is written back via
  `index=N`.

## Edge cases

- **Invalid filename characters** (`< > : " / \ | ? *`) → block Save, show inline validation
  (red border / teaching tip), keep the bar open. Do not write the result.
- **Empty / whitespace-only name** → `Save` disabled.
- **User types an absolute or rooted path** in the box (e.g. `D:\stuff\a.png`) → treat it as the
  full target (use its directory + filename), bypassing the current-folder compose. Honor
  overwrite-prompt against that path.
- **User types a trailing-dot or reserved device name** (`CON`, `PRN`, `NUL`, …) → block with
  validation, same as invalid chars.
- **No filters supplied by caller** → single `All files (*.*)` entry; no forced extension.
- **Selected type pattern has multiple extensions** (e.g. `*.jpg;*.jpeg`) → use the first
  concrete extension for auto-append; respect any extension the user typed.
- **Current folder is virtual / non-filesystem** (e.g. a library, `Home`, search results) →
  `WorkingDirectory` has no real path; disable Save with a hint to navigate to a real folder.
- **Folder not writable** → Save attempt surfaces the native error path (empty result →
  caller sees cancel); ideally pre-checked with a validation message before writing.

## Build / deploy

1. Build SaveDialog native DLLs (x64 + win32) — `Files.App.SaveDialog.vcxproj` /
   `Files.App.SaveDialog.Win32.vcxproj`.
2. Bump app version → `4.1.4.10` (`Package.appxmanifest` / packaging props).
3. Build + package the WinUI MSIX (`tools/files-pro-package/Build-FilesProPackage.ps1`).
4. Sign + install (`tools/files-pro-package/Install-FilesProPackage.ps1`).
5. Re-register dialogs (`tools/files-pro-shell/Register-FilesProShell.ps1`) — install copies
   the rebuilt native DLLs into the package LocalState the HKCU CLSID override points at.

The build toolchain is known-good (artifacts `4.1.4.0`–`4.1.4.9` already built/installed); the
stale `WMC9999` note in `files-pro-plan.md` no longer blocks.

## Testing / verification

- Native unit-ish trace: confirm `save_dialog.txt` shows the new args on a real invocation.
- Manual: Discord "Save image as…" → Save bar appears, name prefilled, choose folder, Save →
  file written at chosen path; verify the caller receives the path (`GetResult`).
- Overwrite: save onto an existing name → "Replace?" appears; No keeps dialog open.
- Cancel / X: native returns `ERROR_CANCELLED`, no file created/overwritten.
- Open dialog regression: existing "select + close" open flow still returns selection.
- `debug.log`: protocol activation still reaches `Root content: MainPage`, no
  `NavigationFailed`.

## Risks

- **`Window_Closed` double-commit / accidental overwrite** — mitigated by the save-mode guard
  making Save the only commit path.
- **Caller had old native DLL loaded in-process** — requires the calling app to be restarted
  after reinstall (loads the new `Files.App.SaveDialog64.dll`).
- **URI length with many/long filters** — buffer enlarged to 8192; typical filter lists are
  short. If a pathological case appears, fall back to writing filters to a temp request file.
