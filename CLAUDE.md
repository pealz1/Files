# Files-Pro

Agent guidance lives in **[AGENTS.md](./AGENTS.md)** — read it first (codebase overview,
build/package commands, interop conventions).

Quick facts: fork of files-community/Files (WinUI3/.NET Windows file manager) whose
differentiator is a **custom Save dialog** (`Files.App.SaveDialog`). Now at `C:\dev\Files-Pro`
(moved off OneDrive 2026-07-02). Build: `dotnet build Files.slnx` or
`msbuild -restore src\Files.App\Files.App.csproj /p:Configuration=Debug /p:Platform=x64`
(regenerates `artifacts/`). Deep context: Claude memory `project_files_pro_save_dialog`.
