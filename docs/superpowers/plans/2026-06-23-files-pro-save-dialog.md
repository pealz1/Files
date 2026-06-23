# Files Pro Save Dialog Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give Files Pro a working, full-parity Save dialog — a docked "Save bar" with a File name box, Save-as-type dropdown, New Folder, and Save/Cancel — so apps like Discord can save new files through Files Pro.

**Architecture:** The native COM `CFilesSaveDialog` already launches the Files Pro window via a `files-dev:` protocol URI and blocks on a `FILEDIALOG` event, then reads the chosen path from a temp file. We extend the native side to forward the suggested name + file-type filters and to read back the chosen type index, and we build the missing app-side save UI + commit logic. Open-dialog behavior is untouched.

**Tech Stack:** C++/ATL COM server (`Files.App.SaveDialog`), C# WinUI 3 (.NET 10) app (`Files.App`), CommunityToolkit.Mvvm, MSTest for pure-logic unit tests, MSIX packaging via existing PowerShell scripts.

**Reference spec:** `docs/superpowers/specs/2026-06-23-files-pro-save-dialog-design.md`

---

## File Structure

**New files:**
- `src/Files.App/Utils/SaveDialog/FileTypeChoice.cs` — pure record describing one "Save as type" entry.
- `src/Files.App/Utils/SaveDialog/SaveDialogPathHelper.cs` — pure logic: parse filters, resolve target path, validate name. No WinUI deps.
- `src/Files.App/Data/Models/SaveDialogRequest.cs` — immutable request parsed from launch args.
- `src/Files.App/ViewModels/SaveDialogViewModel.cs` — bar state + Save/Cancel/NewFolder commands.
- `src/Files.App/UserControls/SaveDialogBar.xaml` (+ `.xaml.cs`) — the docked bar.
- `tests/Files.App.SaveDialog.UnitTests/Files.App.SaveDialog.UnitTests.csproj` — MSTest project that link-compiles the two pure helper files.
- `tests/Files.App.SaveDialog.UnitTests/SaveDialogPathHelperTests.cs`
- `tests/Files.App.SaveDialog.UnitTests/CommandLineSaveParsingTests.cs`

**Modified files:**
- `src/Files.App/Data/Enums/ParsedCommandType.cs` — add `SaveDialog`, `SaveAs`, `FileTypes`, `FileTypeIndex`.
- `src/Files.App/Utils/CommandLine/CommandLineParser.cs` — map the four new arg keys.
- `src/Files.App/App.xaml.cs` — `IsSaveDialog`, `SaveDialogRequest`, commit guard, `Window_Closed` gate.
- `src/Files.App/MainWindow.xaml.cs` — build the request from parsed commands.
- `src/Files.App/ViewModels/MainPageViewModel.cs` — expose `SaveDialogViewModel`.
- `src/Files.App/Views/MainPage.xaml` (+ `.xaml.cs`) — host the bar in a new bottom row.
- `src/Files.App.SaveDialog/FilesSaveDialog.h` / `FilesSaveDialog.cpp` — store filters, forward args, parse index.
- `src/Files.App/Package.appxmanifest` — version `4.1.4.9` → `4.1.4.10`.

---

## Task 1: Pure save-path logic + unit test project (TDD)

**Files:**
- Create: `src/Files.App/Utils/SaveDialog/FileTypeChoice.cs`
- Create: `src/Files.App/Utils/SaveDialog/SaveDialogPathHelper.cs`
- Create: `tests/Files.App.SaveDialog.UnitTests/Files.App.SaveDialog.UnitTests.csproj`
- Create: `tests/Files.App.SaveDialog.UnitTests/SaveDialogPathHelperTests.cs`

- [ ] **Step 1: Create the unit test project**

`tests/Files.App.SaveDialog.UnitTests/Files.App.SaveDialog.UnitTests.csproj`:

```xml
<!--  Copyright (c) Files Community. Licensed under the MIT License.  -->
<Project Sdk="Microsoft.NET.Sdk">

	<PropertyGroup>
		<TargetFramework>net10.0-windows10.0.26100.0</TargetFramework>
		<Platforms>x64</Platforms>
		<RuntimeIdentifiers>win-x64</RuntimeIdentifiers>
		<IsPackable>false</IsPackable>
		<Nullable>enable</Nullable>
		<LangVersion>latest</LangVersion>
		<EnableMSTestRunner>true</EnableMSTestRunner>
		<OutputType>Exe</OutputType>
	</PropertyGroup>

	<ItemGroup>
		<PackageReference Include="MSTest.TestAdapter" />
		<PackageReference Include="MSTest.TestFramework" />
		<PackageReference Include="Microsoft.NET.Test.Sdk" />
	</ItemGroup>

	<!-- Link-compile the pure helpers under test (no WinUI dependency) -->
	<ItemGroup>
		<Compile Include="..\..\src\Files.App\Utils\SaveDialog\FileTypeChoice.cs" Link="FileTypeChoice.cs" />
		<Compile Include="..\..\src\Files.App\Utils\SaveDialog\SaveDialogPathHelper.cs" Link="SaveDialogPathHelper.cs" />
	</ItemGroup>

</Project>
```

> Note: `Microsoft.NET.Test.Sdk` and the MSTest packages already have versions pinned in `Directory.Packages.props` (used by `Files.InteractionTests`). If `Microsoft.NET.Test.Sdk` is missing there, add `<PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />` to `Directory.Packages.props`.

- [ ] **Step 2: Write the failing tests**

`tests/Files.App.SaveDialog.UnitTests/SaveDialogPathHelperTests.cs`:

```csharp
// Copyright (c) Files Community. Licensed under the MIT License.

using Files.App.Utils.SaveDialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.App.SaveDialog.UnitTests
{
	[TestClass]
	public class SaveDialogPathHelperTests
	{
		[TestMethod]
		public void ParseFileTypes_ParsesDisplayAndPatternPairs()
		{
			var result = SaveDialogPathHelper.ParseFileTypes("PNG Image|*.png|All files|*.*");

			Assert.AreEqual(2, result.Count);
			Assert.AreEqual("PNG Image", result[0].Display);
			Assert.AreEqual("*.png", result[0].Pattern);
			Assert.AreEqual(".png", result[0].PrimaryExtension);
			Assert.AreEqual("All files", result[1].Display);
			Assert.AreEqual("", result[1].PrimaryExtension);
		}

		[TestMethod]
		public void ParseFileTypes_EmptyOrNull_ReturnsAllFilesFallback()
		{
			var result = SaveDialogPathHelper.ParseFileTypes("");

			Assert.AreEqual(1, result.Count);
			Assert.AreEqual("*.*", result[0].Pattern);
		}

		[TestMethod]
		public void ParseFileTypes_MultiExtensionPattern_UsesFirstAsPrimary()
		{
			var result = SaveDialogPathHelper.ParseFileTypes("JPEG|*.jpg;*.jpeg");

			Assert.AreEqual(".jpg", result[0].PrimaryExtension);
		}

		[TestMethod]
		public void ResolveTargetPath_AppendsExtensionWhenMissing()
		{
			var type = new FileTypeChoice("PNG Image", "*.png", ".png");
			var path = SaveDialogPathHelper.ResolveTargetPath(@"C:\Users\me\Downloads", "photo", type);

			Assert.AreEqual(@"C:\Users\me\Downloads\photo.png", path);
		}

		[TestMethod]
		public void ResolveTargetPath_RespectsTypedExtension()
		{
			var type = new FileTypeChoice("PNG Image", "*.png", ".png");
			var path = SaveDialogPathHelper.ResolveTargetPath(@"C:\Users\me\Downloads", "photo.jpg", type);

			Assert.AreEqual(@"C:\Users\me\Downloads\photo.jpg", path);
		}

		[TestMethod]
		public void ResolveTargetPath_AllFilesType_DoesNotAppend()
		{
			var type = new FileTypeChoice("All files", "*.*", "");
			var path = SaveDialogPathHelper.ResolveTargetPath(@"C:\Users\me\Downloads", "notes", type);

			Assert.AreEqual(@"C:\Users\me\Downloads\notes", path);
		}

		[TestMethod]
		public void ResolveTargetPath_TypedAbsolutePath_OverridesWorkingDirectory()
		{
			var type = new FileTypeChoice("PNG Image", "*.png", ".png");
			var path = SaveDialogPathHelper.ResolveTargetPath(@"C:\Users\me\Downloads", @"D:\stuff\a", type);

			Assert.AreEqual(@"D:\stuff\a.png", path);
		}

		[TestMethod]
		public void IsValidFileName_RejectsInvalidChars()
		{
			Assert.IsFalse(SaveDialogPathHelper.IsValidFileName("bad:name?.png"));
			Assert.IsFalse(SaveDialogPathHelper.IsValidFileName("   "));
			Assert.IsFalse(SaveDialogPathHelper.IsValidFileName(""));
			Assert.IsFalse(SaveDialogPathHelper.IsValidFileName("CON"));
		}

		[TestMethod]
		public void IsValidFileName_AcceptsNormalNames()
		{
			Assert.IsTrue(SaveDialogPathHelper.IsValidFileName("5Julygamer.png"));
			Assert.IsTrue(SaveDialogPathHelper.IsValidFileName(@"D:\stuff\a.png"));
		}
	}
}
```

- [ ] **Step 3: Run tests, verify they FAIL to compile/run**

Run: `dotnet test tests/Files.App.SaveDialog.UnitTests/Files.App.SaveDialog.UnitTests.csproj -c Debug -p:Platform=x64`
Expected: FAIL — `FileTypeChoice` / `SaveDialogPathHelper` do not exist.

- [ ] **Step 4: Implement `FileTypeChoice`**

`src/Files.App/Utils/SaveDialog/FileTypeChoice.cs`:

```csharp
// Copyright (c) Files Community. Licensed under the MIT License.

namespace Files.App.Utils.SaveDialog
{
	/// <summary>
	/// One entry in the Save dialog "Save as type" dropdown.
	/// </summary>
	/// <param name="Display">Friendly name shown to the user, e.g. "PNG Image".</param>
	/// <param name="Pattern">Raw filter pattern, e.g. "*.png" or "*.jpg;*.jpeg" or "*.*".</param>
	/// <param name="PrimaryExtension">First concrete extension incl. dot (".png"), or "" for *.* .</param>
	public sealed record FileTypeChoice(string Display, string Pattern, string PrimaryExtension)
	{
		public override string ToString() => Display;
	}
}
```

- [ ] **Step 5: Implement `SaveDialogPathHelper`**

`src/Files.App/Utils/SaveDialog/SaveDialogPathHelper.cs`:

```csharp
// Copyright (c) Files Community. Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Files.App.Utils.SaveDialog
{
	/// <summary>
	/// Pure (UI-free) helpers for the Save dialog: filter parsing, target-path resolution,
	/// and filename validation. Kept dependency-free so it can be unit tested.
	/// </summary>
	public static class SaveDialogPathHelper
	{
		private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
		{
			"CON", "PRN", "AUX", "NUL",
			"COM1","COM2","COM3","COM4","COM5","COM6","COM7","COM8","COM9",
			"LPT1","LPT2","LPT3","LPT4","LPT5","LPT6","LPT7","LPT8","LPT9",
		};

		/// <summary>
		/// Parse the pipe-delimited "Display|Pattern|Display|Pattern" string forwarded by the
		/// native dialog. Empty input yields a single "All files (*.*)" fallback.
		/// </summary>
		public static IReadOnlyList<FileTypeChoice> ParseFileTypes(string? serialized)
		{
			if (string.IsNullOrWhiteSpace(serialized))
				return new[] { new FileTypeChoice("All files", "*.*", "") };

			var parts = serialized.Split('|');
			var list = new List<FileTypeChoice>();

			for (int i = 0; i + 1 < parts.Length; i += 2)
			{
				var display = parts[i];
				var pattern = parts[i + 1];
				list.Add(new FileTypeChoice(display, pattern, PrimaryExtensionOf(pattern)));
			}

			return list.Count > 0
				? list
				: new List<FileTypeChoice> { new("All files", "*.*", "") };
		}

		/// <summary>First concrete extension of a pattern (".png"), or "" for wildcard "*.*".</summary>
		private static string PrimaryExtensionOf(string pattern)
		{
			var first = pattern.Split(';').FirstOrDefault()?.Trim() ?? "";
			// "*.png" -> ".png"; "*.*" / "*" -> ""
			var star = first.TrimStart('*');
			if (star is "" or "." or ".*")
				return "";
			return star.StartsWith('.') ? star : "." + star;
		}

		/// <summary>
		/// Compose the full target path from the current folder, the typed name, and the selected
		/// file type. A typed absolute/rooted path overrides the working directory. Extension is
		/// appended from the selected type only when the typed name has none.
		/// </summary>
		public static string ResolveTargetPath(string workingDirectory, string typedName, FileTypeChoice selectedType)
		{
			var name = typedName.Trim();

			string directory;
			string fileName;

			if (Path.IsPathRooted(name))
			{
				directory = Path.GetDirectoryName(name) ?? workingDirectory;
				fileName = Path.GetFileName(name);
			}
			else
			{
				directory = workingDirectory;
				fileName = name;
			}

			if (!string.IsNullOrEmpty(selectedType.PrimaryExtension) &&
				string.IsNullOrEmpty(Path.GetExtension(fileName)))
			{
				fileName += selectedType.PrimaryExtension;
			}

			return Path.Combine(directory, fileName);
		}

		/// <summary>
		/// Validate the typed name. For rooted input only the filename portion is checked.
		/// Rejects empty/whitespace, invalid path chars, and reserved device names.
		/// </summary>
		public static bool IsValidFileName(string typedName)
		{
			if (string.IsNullOrWhiteSpace(typedName))
				return false;

			var name = typedName.Trim();
			var fileName = Path.IsPathRooted(name) ? Path.GetFileName(name) : name;

			if (string.IsNullOrWhiteSpace(fileName))
				return false;

			if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
				return false;

			var stem = Path.GetFileNameWithoutExtension(fileName);
			if (ReservedNames.Contains(stem))
				return false;

			if (fileName.EndsWith('.') || fileName.EndsWith(' '))
				return false;

			return true;
		}
	}
}
```

- [ ] **Step 6: Run tests, verify PASS**

Run: `dotnet test tests/Files.App.SaveDialog.UnitTests/Files.App.SaveDialog.UnitTests.csproj -c Debug -p:Platform=x64`
Expected: PASS (9 tests).

- [ ] **Step 7: Add the test project to the solution**

Run: `dotnet sln Files.slnx add tests/Files.App.SaveDialog.UnitTests/Files.App.SaveDialog.UnitTests.csproj`
Expected: project added (or, if `.slnx` add is unsupported by the CLI version, manually add a `<Project Path="tests/Files.App.SaveDialog.UnitTests/Files.App.SaveDialog.UnitTests.csproj" />` line to `Files.slnx`).

- [ ] **Step 8: Commit**

```bash
git add src/Files.App/Utils/SaveDialog/ tests/Files.App.SaveDialog.UnitTests/ Files.slnx Directory.Packages.props
git commit -m "feat(save-dialog): add pure save-path logic with unit tests"
```

---

## Task 2: Parse the new launch args

**Files:**
- Modify: `src/Files.App/Data/Enums/ParsedCommandType.cs`
- Modify: `src/Files.App/Utils/CommandLine/CommandLineParser.cs`
- Create: `tests/Files.App.SaveDialog.UnitTests/CommandLineSaveParsingTests.cs`
- Modify: `tests/Files.App.SaveDialog.UnitTests/Files.App.SaveDialog.UnitTests.csproj`

- [ ] **Step 1: Write the failing parser test**

`tests/Files.App.SaveDialog.UnitTests/CommandLineSaveParsingTests.cs`:

```csharp
// Copyright (c) Files Community. Licensed under the MIT License.

using System.Linq;
using Files.App.Data.Enums;
using Files.App.Utils.CommandLine;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.App.SaveDialog.UnitTests
{
	[TestClass]
	public class CommandLineSaveParsingTests
	{
		[TestMethod]
		public void Parses_SaveDialog_Args()
		{
			var cmd = "-directory \"C:\\Users\\me\\Downloads\" -outputpath \"C:\\Temp\\out.tmp\" " +
					  "-savedialog -saveas \"photo.png\" -filetypes \"PNG|*.png|All files|*.*\" -filetypeindex 1";

			var parsed = CommandLineParser.ParseUntrustedCommands(cmd);

			Assert.IsTrue(parsed.Any(c => c.Type == ParsedCommandType.SaveDialog));
			Assert.AreEqual("photo.png", parsed.First(c => c.Type == ParsedCommandType.SaveAs).Payload);
			Assert.AreEqual("PNG|*.png|All files|*.*", parsed.First(c => c.Type == ParsedCommandType.FileTypes).Payload);
			Assert.AreEqual("1", parsed.First(c => c.Type == ParsedCommandType.FileTypeIndex).Payload);
			Assert.AreEqual(ParsedCommandType.OutputPath, parsed.First(c => c.Type == ParsedCommandType.OutputPath).Type);
		}
	}
}
```

Add the needed source files to the test project (`Files.App.SaveDialog.UnitTests.csproj` `<ItemGroup>` of linked compiles):

```xml
		<Compile Include="..\..\src\Files.App\Utils\CommandLine\CommandLineParser.cs" Link="CommandLineParser.cs" />
		<Compile Include="..\..\src\Files.App\Utils\CommandLine\ParsedCommand.cs" Link="ParsedCommand.cs" />
		<Compile Include="..\..\src\Files.App\Utils\CommandLine\ParsedCommands.cs" Link="ParsedCommands.cs" />
		<Compile Include="..\..\src\Files.App\Data\Enums\ParsedCommandType.cs" Link="ParsedCommandType.cs" />
```

> If `CommandLineParser.cs` pulls in `Debug`/global usings that don't resolve in the test project, add `<Using Include="System.Diagnostics" />` and `<Using Include="System.Collections.Generic" />` to the test csproj `<ItemGroup>`. Verify the exact filenames of `ParsedCommand`/`ParsedCommands` under `src/Files.App/Utils/CommandLine/` before linking.

- [ ] **Step 2: Run, verify FAIL**

Run: `dotnet test tests/Files.App.SaveDialog.UnitTests/Files.App.SaveDialog.UnitTests.csproj -c Debug -p:Platform=x64`
Expected: FAIL — enum values `SaveDialog`/`SaveAs`/`FileTypes`/`FileTypeIndex` missing.

- [ ] **Step 3: Add enum values**

In `src/Files.App/Data/Enums/ParsedCommandType.cs`, add before the closing brace of the enum (after `TagFiles`, adding a comma to `TagFiles`):

```csharp
		TagFiles,

		/// <summary>Enter Save dialog mode.</summary>
		SaveDialog,

		/// <summary>Suggested file name for the Save dialog.</summary>
		SaveAs,

		/// <summary>Pipe-delimited Save dialog file-type filters.</summary>
		FileTypes,

		/// <summary>1-based default file-type index for the Save dialog.</summary>
		FileTypeIndex
```

- [ ] **Step 4: Map the arg keys**

In `src/Files.App/Utils/CommandLine/CommandLineParser.cs`, inside the `switch (kvp.Key)` (after the `Tag` case at lines 66-68), add:

```csharp
					case string s when "SaveDialog".Equals(s, StringComparison.OrdinalIgnoreCase):
						command.Type = ParsedCommandType.SaveDialog;
						break;

					case string s when "SaveAs".Equals(s, StringComparison.OrdinalIgnoreCase):
						command.Type = ParsedCommandType.SaveAs;
						break;

					case string s when "FileTypes".Equals(s, StringComparison.OrdinalIgnoreCase):
						command.Type = ParsedCommandType.FileTypes;
						break;

					case string s when "FileTypeIndex".Equals(s, StringComparison.OrdinalIgnoreCase):
						command.Type = ParsedCommandType.FileTypeIndex;
						break;
```

> `-savedialog` has no value, so `kvp.Value` is empty. `command.Payload` (first arg) may be null/empty for the flag — that is fine; we only check `.Type`.

- [ ] **Step 5: Run, verify PASS**

Run: `dotnet test tests/Files.App.SaveDialog.UnitTests/Files.App.SaveDialog.UnitTests.csproj -c Debug -p:Platform=x64`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Files.App/Data/Enums/ParsedCommandType.cs src/Files.App/Utils/CommandLine/CommandLineParser.cs tests/Files.App.SaveDialog.UnitTests/
git commit -m "feat(save-dialog): parse -savedialog/-saveas/-filetypes/-filetypeindex args"
```

---

## Task 3: SaveDialogRequest model + App state

**Files:**
- Create: `src/Files.App/Data/Models/SaveDialogRequest.cs`
- Modify: `src/Files.App/App.xaml.cs`

- [ ] **Step 1: Create the request model**

`src/Files.App/Data/Models/SaveDialogRequest.cs`:

```csharp
// Copyright (c) Files Community. Licensed under the MIT License.

using System.Collections.Generic;
using Files.App.Utils.SaveDialog;

namespace Files.App.Data.Models
{
	/// <summary>
	/// Immutable description of a Save dialog invocation, parsed from launch args.
	/// </summary>
	public sealed record SaveDialogRequest(
		string SuggestedName,
		IReadOnlyList<FileTypeChoice> FileTypes,
		int TypeIndex);
}
```

- [ ] **Step 2: Add App state + commit guard**

In `src/Files.App/App.xaml.cs`, next to `public static string? OutputPath { get; set; }` (line 25), add:

```csharp
		public static string? OutputPath { get; set; }
		public static bool IsSaveDialog { get; set; }
		public static Files.App.Data.Models.SaveDialogRequest? SaveDialogRequest { get; set; }
		public static bool SaveDialogCommitted { get; set; }
```

- [ ] **Step 3: Commit**

```bash
git add src/Files.App/Data/Models/SaveDialogRequest.cs src/Files.App/App.xaml.cs
git commit -m "feat(save-dialog): add SaveDialogRequest model and App save-mode state"
```

> No unit test here — this is plain state wiring exercised end-to-end in Task 9 verification.

---

## Task 4: Build the request in MainWindow

**Files:**
- Modify: `src/Files.App/MainWindow.xaml.cs`

- [ ] **Step 1: Handle the new commands**

In `src/Files.App/MainWindow.xaml.cs`, in `InitializeFromCmdLineArgsAsync`'s `foreach (var command in parsedCommands)` switch (the block ending with the `OutputPath` case around lines 386-388), replace the `OutputPath` case and add handling. First, before the `foreach`, compute save-mode fields:

```csharp
			// Save dialog detection (collect before the navigation switch)
			var saveDialogCmd = parsedCommands.FirstOrDefault(x => x.Type == ParsedCommandType.SaveDialog);
			if (saveDialogCmd is not null)
			{
				var suggested = parsedCommands.FirstOrDefault(x => x.Type == ParsedCommandType.SaveAs)?.Payload ?? string.Empty;
				var filtersRaw = parsedCommands.FirstOrDefault(x => x.Type == ParsedCommandType.FileTypes)?.Payload ?? string.Empty;
				var indexRaw = parsedCommands.FirstOrDefault(x => x.Type == ParsedCommandType.FileTypeIndex)?.Payload;
				var types = Files.App.Utils.SaveDialog.SaveDialogPathHelper.ParseFileTypes(filtersRaw);
				var index = int.TryParse(indexRaw, out var n) ? n : 1;

				App.IsSaveDialog = true;
				App.SaveDialogCommitted = false;
				App.SaveDialogRequest = new Files.App.Data.Models.SaveDialogRequest(suggested, types, index);
			}
```

Keep the existing `case ParsedCommandType.OutputPath: App.OutputPath = command.Payload; break;`. The new `SaveDialog`/`SaveAs`/`FileTypes`/`FileTypeIndex` command types need a no-op `case` so they don't hit the `Unknown` navigation path. Add to the switch:

```csharp
					case ParsedCommandType.SaveDialog:
					case ParsedCommandType.SaveAs:
					case ParsedCommandType.FileTypes:
					case ParsedCommandType.FileTypeIndex:
						// Consumed above into App.SaveDialogRequest; no navigation.
						break;
```

- [ ] **Step 2: Build the app to verify it compiles**

Run: `dotnet build src/Files.App/Files.App.csproj -c Debug -p:Platform=x64 -p:AppxBundle=Never -p:GenerateAppxPackageOnBuild=false`
Expected: no `error CS*`. (XAML `MSB3073`/`WMC` failures, if any, are unrelated — see spec note.)

- [ ] **Step 3: Commit**

```bash
git add src/Files.App/MainWindow.xaml.cs
git commit -m "feat(save-dialog): build SaveDialogRequest from launch args"
```

---

## Task 5: SaveDialogViewModel

**Files:**
- Create: `src/Files.App/ViewModels/SaveDialogViewModel.cs`

- [ ] **Step 1: Implement the view model**

`src/Files.App/ViewModels/SaveDialogViewModel.cs`:

```csharp
// Copyright (c) Files Community. Licensed under the MIT License.

using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Files.App.Utils.SaveDialog;

namespace Files.App.ViewModels
{
	/// <summary>
	/// Backing state for the docked Save bar. Pure to the extent possible; the actual file
	/// commit (path resolution, overwrite prompt, writing the result) is performed by the
	/// owner (MainPage) via the CommitRequested / CancelRequested / NewFolderRequested events
	/// so it can reach the active pane and dialog services.
	/// </summary>
	public sealed partial class SaveDialogViewModel : ObservableObject
	{
		[ObservableProperty]
		private bool isActive;

		[ObservableProperty]
		private string fileName = string.Empty;

		[ObservableProperty]
		private IReadOnlyList<FileTypeChoice> fileTypes = Array.Empty<FileTypeChoice>();

		[ObservableProperty]
		private FileTypeChoice? selectedFileType;

		public event EventHandler? CommitRequested;
		public event EventHandler? CancelRequested;
		public event EventHandler? NewFolderRequested;

		public bool CanSave => SaveDialogPathHelper.IsValidFileName(FileName);

		partial void OnFileNameChanged(string value) => SaveCommand.NotifyCanExecuteChanged();

		[RelayCommand(CanExecute = nameof(CanSave))]
		private void Save() => CommitRequested?.Invoke(this, EventArgs.Empty);

		[RelayCommand]
		private void Cancel() => CancelRequested?.Invoke(this, EventArgs.Empty);

		[RelayCommand]
		private void NewFolder() => NewFolderRequested?.Invoke(this, EventArgs.Empty);

		/// <summary>Initialize from a parsed request.</summary>
		public void Initialize(Files.App.Data.Models.SaveDialogRequest request)
		{
			FileTypes = request.FileTypes;
			var idx = Math.Clamp(request.TypeIndex - 1, 0, Math.Max(0, request.FileTypes.Count - 1));
			SelectedFileType = request.FileTypes.Count > 0 ? request.FileTypes[idx] : null;
			FileName = request.SuggestedName;
			IsActive = true;
		}
	}
}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build src/Files.App/Files.App.csproj -c Debug -p:Platform=x64 -p:AppxBundle=Never -p:GenerateAppxPackageOnBuild=false`
Expected: no `error CS*`.

- [ ] **Step 3: Commit**

```bash
git add src/Files.App/ViewModels/SaveDialogViewModel.cs
git commit -m "feat(save-dialog): add SaveDialogViewModel"
```

---

## Task 6: SaveDialogBar control + MainPage host

**Files:**
- Create: `src/Files.App/UserControls/SaveDialogBar.xaml` (+ `.xaml.cs`)
- Modify: `src/Files.App/ViewModels/MainPageViewModel.cs`
- Modify: `src/Files.App/Views/MainPage.xaml`

- [ ] **Step 1: Expose the view model from MainPageViewModel**

In `src/Files.App/ViewModels/MainPageViewModel.cs`, add a property (near the other public properties):

```csharp
		public SaveDialogViewModel SaveDialogViewModel { get; } = new();
```

- [ ] **Step 2: Create the bar control**

`src/Files.App/UserControls/SaveDialogBar.xaml`:

```xml
<?xml version="1.0" encoding="utf-8" ?>
<!--  Copyright (c) Files Community. Licensed under the MIT License.  -->
<UserControl
	x:Class="Files.App.UserControls.SaveDialogBar"
	xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
	xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
	xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
	xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
	mc:Ignorable="d">

	<Border
		Padding="12,8"
		Background="{ThemeResource App.Theme.BackgroundBrush}"
		BorderBrush="{ThemeResource DividerStrokeColorDefaultBrush}"
		BorderThickness="0,1,0,0">
		<Grid ColumnSpacing="8">
			<Grid.ColumnDefinitions>
				<ColumnDefinition Width="Auto" />
				<ColumnDefinition Width="*" />
				<ColumnDefinition Width="Auto" />
				<ColumnDefinition Width="Auto" />
				<ColumnDefinition Width="Auto" />
				<ColumnDefinition Width="Auto" />
				<ColumnDefinition Width="Auto" />
			</Grid.ColumnDefinitions>

			<TextBlock
				Grid.Column="0"
				VerticalAlignment="Center"
				Text="File name:" />

			<TextBox
				x:Name="FileNameBox"
				Grid.Column="1"
				VerticalAlignment="Center"
				KeyDown="FileNameBox_KeyDown"
				Text="{x:Bind ViewModel.FileName, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" />

			<TextBlock
				Grid.Column="2"
				Margin="8,0,0,0"
				VerticalAlignment="Center"
				Text="Save as type:" />

			<ComboBox
				Grid.Column="3"
				MinWidth="160"
				VerticalAlignment="Center"
				ItemsSource="{x:Bind ViewModel.FileTypes, Mode=OneWay}"
				SelectedItem="{x:Bind ViewModel.SelectedFileType, Mode=TwoWay}" />

			<Button
				Grid.Column="4"
				VerticalAlignment="Center"
				Command="{x:Bind ViewModel.NewFolderCommand}"
				Content="New Folder" />

			<Button
				Grid.Column="5"
				VerticalAlignment="Center"
				Command="{x:Bind ViewModel.SaveCommand}"
				Content="Save"
				Style="{ThemeResource AccentButtonStyle}" />

			<Button
				Grid.Column="6"
				VerticalAlignment="Center"
				Command="{x:Bind ViewModel.CancelCommand}"
				Content="Cancel" />
		</Grid>
	</Border>
</UserControl>
```

`src/Files.App/UserControls/SaveDialogBar.xaml.cs`:

```csharp
// Copyright (c) Files Community. Licensed under the MIT License.

using Files.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Files.App.UserControls
{
	public sealed partial class SaveDialogBar : UserControl
	{
		public SaveDialogViewModel ViewModel
		{
			get => (SaveDialogViewModel)GetValue(ViewModelProperty);
			set => SetValue(ViewModelProperty, value);
		}

		public static readonly DependencyProperty ViewModelProperty =
			DependencyProperty.Register(nameof(ViewModel), typeof(SaveDialogViewModel), typeof(SaveDialogBar), new PropertyMetadata(null));

		public SaveDialogBar()
		{
			InitializeComponent();
		}

		private void FileNameBox_KeyDown(object sender, KeyRoutedEventArgs e)
		{
			if (e.Key == VirtualKey.Enter && ViewModel.SaveCommand.CanExecute(null))
			{
				ViewModel.SaveCommand.Execute(null);
				e.Handled = true;
			}
			else if (e.Key == VirtualKey.Escape)
			{
				ViewModel.CancelCommand.Execute(null);
				e.Handled = true;
			}
		}

		public void FocusFileName()
		{
			FileNameBox.Focus(FocusState.Programmatic);
			FileNameBox.SelectionStart = 0;
			var name = FileNameBox.Text;
			var dot = name.LastIndexOf('.');
			FileNameBox.SelectionLength = dot > 0 ? dot : name.Length;
		}
	}
}
```

- [ ] **Step 2: Host the bar in MainPage**

In `src/Files.App/Views/MainPage.xaml`, change the root `Grid.RowDefinitions` (lines 141-145) to add a fourth row:

```xml
		<Grid.RowDefinitions>
			<RowDefinition Height="Auto" />
			<RowDefinition Height="Auto" />
			<RowDefinition Height="*" />
			<RowDefinition Height="Auto" />
		</Grid.RowDefinitions>
```

Then, immediately before the closing `</Grid>` of the root grid (the `</Grid>` right before `</Page>` at the end of the file), add the bar host:

```xml
		<uc:SaveDialogBar
			x:Name="SaveDialogBarControl"
			Grid.Row="3"
			ViewModel="{x:Bind ViewModel.SaveDialogViewModel, Mode=OneWay}"
			Visibility="{x:Bind ViewModel.SaveDialogViewModel.IsActive, Mode=OneWay}" />
```

> `uc:` is already declared (`xmlns:uc="using:Files.App.UserControls"`). `IsActive` is a `bool`; WinUI binds `bool`→`Visibility` only via a converter. Add `xmlns:wctconverters` is already present — use the existing `BoolNegationConverter`? No. Instead bind visibility in code or add a `BoolToVisibilityConverter`. Simplest: bind `Visibility="{x:Bind ViewModel.SaveDialogViewModel.IsActive, Mode=OneWay, Converter={StaticResource BoolToVisibilityConverter}}"` and ensure that converter is registered in `Page.Resources` (the CommunityToolkit `BoolToVisibilityConverter` from `wctconverters` namespace). Add to `Page.Resources` (next to `BoolNegationConverter` at line ~57): `<wctconverters:BoolToVisibilityConverter x:Key="BoolToVisibilityConverter" />`.

- [ ] **Step 3: Build to verify XAML + bindings compile**

Run: `dotnet build src/Files.App/Files.App.csproj -c Debug -p:Platform=x64 -p:AppxBundle=Never -p:GenerateAppxPackageOnBuild=false`
Expected: build reaches XAML compile and produces no `error CS*`/`XLS*` for the new control. (If a pre-existing unrelated XAML `WMC` issue appears, confirm it also occurs on a clean checkout before treating it as ours.)

- [ ] **Step 4: Commit**

```bash
git add src/Files.App/UserControls/SaveDialogBar.xaml src/Files.App/UserControls/SaveDialogBar.xaml.cs src/Files.App/ViewModels/MainPageViewModel.cs src/Files.App/Views/MainPage.xaml
git commit -m "feat(save-dialog): add docked Save bar control and host it in MainPage"
```

---

## Task 7: Commit logic + Window_Closed guard

**Files:**
- Modify: `src/Files.App/Views/MainPage.xaml.cs`
- Modify: `src/Files.App/App.xaml.cs`

- [ ] **Step 1: Wire up the bar when entering save mode**

In `src/Files.App/Views/MainPage.xaml.cs` `Page_Loaded` (or the existing loaded handler), after the view model is available, add:

```csharp
			if (App.IsSaveDialog && App.SaveDialogRequest is not null)
			{
				ViewModel.SaveDialogViewModel.Initialize(App.SaveDialogRequest);
				ViewModel.SaveDialogViewModel.CommitRequested += SaveDialog_CommitRequested;
				ViewModel.SaveDialogViewModel.CancelRequested += SaveDialog_CancelRequested;
				ViewModel.SaveDialogViewModel.NewFolderRequested += SaveDialog_NewFolderRequested;
				DispatcherQueue.TryEnqueue(() => SaveDialogBarControl.FocusFileName());
			}
```

- [ ] **Step 2: Implement commit / cancel / new folder handlers**

Add these methods to `src/Files.App/Views/MainPage.xaml.cs`:

```csharp
		private string GetActiveWorkingDirectory()
		{
			var instance = ViewModels.MainPageViewModel.AppInstances.FirstOrDefault(x => x.TabItemContent.IsCurrentInstance);
			var shell = (instance?.TabItemContent as ShellPanesPage)?.ActivePane;
			return shell?.ShellViewModel?.WorkingDirectory ?? string.Empty;
		}

		private async void SaveDialog_CommitRequested(object? sender, EventArgs e)
		{
			var vm = ViewModel.SaveDialogViewModel;
			var folder = GetActiveWorkingDirectory();
			var type = vm.SelectedFileType ?? new Utils.SaveDialog.FileTypeChoice("All files", "*.*", "");

			if (!Utils.SaveDialog.SaveDialogPathHelper.IsValidFileName(vm.FileName))
				return;

			// Reject virtual/non-filesystem locations unless the user typed an absolute path.
			if (string.IsNullOrEmpty(folder) && !System.IO.Path.IsPathRooted(vm.FileName.Trim()))
				return;

			var fullPath = Utils.SaveDialog.SaveDialogPathHelper.ResolveTargetPath(folder, vm.FileName, type);

			if (System.IO.File.Exists(fullPath))
			{
				var confirm = new ContentDialog
				{
					Title = "Replace existing file?",
					Content = $"{System.IO.Path.GetFileName(fullPath)} already exists. Do you want to replace it?",
					PrimaryButtonText = "Replace",
					CloseButtonText = "Cancel",
					DefaultButton = ContentDialogButton.Close,
					XamlRoot = this.XamlRoot,
				};
				if (await confirm.ShowAsync() != ContentDialogResult.Primary)
					return;
			}

			var index = vm.SelectedFileType is null ? 1 : vm.FileTypes.ToList().IndexOf(vm.SelectedFileType) + 1;
			CommitSaveResult(fullPath, index);
		}

		private void SaveDialog_CancelRequested(object? sender, EventArgs e)
		{
			// Clean cancel: write nothing, just unblock the native side and close.
			App.SaveDialogCommitted = true; // prevents Window_Closed from re-handling
			SignalFileDialogEvent();
			MainWindow.Instance.Close();
		}

		private async void SaveDialog_NewFolderRequested(object? sender, EventArgs e)
		{
			var instance = ViewModels.MainPageViewModel.AppInstances.FirstOrDefault(x => x.TabItemContent.IsCurrentInstance);
			var shell = (instance?.TabItemContent as ShellPanesPage)?.ActivePane;
			if (shell?.SlimContentPage?.CommandsViewModel?.CreateNewFolderCommand is { } cmd && cmd.CanExecute(null))
				cmd.Execute(null);
			await Task.CompletedTask;
		}

		private void CommitSaveResult(string fullPath, int typeIndex)
		{
			if (App.OutputPath is not null)
			{
				System.IO.File.WriteAllLines(App.OutputPath, new[] { fullPath, $"index={typeIndex}" });
				App.SaveDialogCommitted = true;
			}
			SignalFileDialogEvent();
			App.OutputPath = null; // ensure Window_Closed does not re-handle
			MainWindow.Instance.Close();
		}

		private static void SignalFileDialogEvent()
		{
			using var eventHandle = Windows.Win32.PInvoke.CreateEvent(null, false, false, "FILEDIALOG");
			Windows.Win32.PInvoke.SetEvent(eventHandle);
		}
```

> Verify the exact accessor chain for "create new folder" against `BaseShellPage`/`BaseLayoutPage` (`SlimContentPage.CommandsViewModel.CreateNewFolderCommand`). If the command name differs, use `Ioc.Default.GetRequiredService<ICommandManager>().CreateFolder` and `.ExecuteAsync()` instead. Confirm `ShellPanesPage.ActivePane.SlimContentPage` is the right path (it matches the existing `App.xaml.cs` `Window_Closed` access pattern).

- [ ] **Step 3: Gate Window_Closed for save mode**

In `src/Files.App/App.xaml.cs` `Window_Closed`, replace the `if (OutputPath is not null) { ... }` block (lines 227-242) with:

```csharp
			if (OutputPath is not null)
			{
				if (IsSaveDialog)
				{
					// Save mode commits ONLY via the explicit Save button (CommitSaveResult).
					// A window close here without a commit is a cancel: write nothing so the
					// native side returns ERROR_CANCELLED instead of overwriting a highlighted file.
					if (!SaveDialogCommitted)
					{
						using var cancelEvent = PInvoke.CreateEvent(null, false, false, "FILEDIALOG");
						PInvoke.SetEvent(cancelEvent);
					}
				}
				else
				{
					var instance = MainPageViewModel.AppInstances.FirstOrDefault(x => x.TabItemContent.IsCurrentInstance);
					if (instance is null)
						return;

					var items = (instance.TabItemContent as ShellPanesPage)?.ActivePane?.SlimContentPage?.SelectedItems;
					if (items is null)
						return;

					var results = items.Select(x => x.ItemPath).ToList();
					System.IO.File.WriteAllLines(OutputPath, results);

					using var eventHandle = PInvoke.CreateEvent(null, false, false, "FILEDIALOG");
					PInvoke.SetEvent(eventHandle);
				}
			}
```

- [ ] **Step 4: Build to verify compile**

Run: `dotnet build src/Files.App/Files.App.csproj -c Debug -p:Platform=x64 -p:AppxBundle=Never -p:GenerateAppxPackageOnBuild=false`
Expected: no `error CS*`.

- [ ] **Step 5: Commit**

```bash
git add src/Files.App/Views/MainPage.xaml.cs src/Files.App/App.xaml.cs
git commit -m "feat(save-dialog): commit logic, cancel, new folder, and Window_Closed guard"
```

---

## Task 8: Native — forward filters/name, parse type index

**Files:**
- Modify: `src/Files.App.SaveDialog/FilesSaveDialog.h`
- Modify: `src/Files.App.SaveDialog/FilesSaveDialog.cpp`

- [ ] **Step 1: Add member fields**

In `src/Files.App.SaveDialog/FilesSaveDialog.h`, add to the private members (near `_fos`, `_initName`, `_initFolder`):

```cpp
	std::vector<std::pair<std::wstring, std::wstring>> _fileTypes; // (display, pattern)
	UINT _fileTypeIndex = 1;
```

Ensure `<vector>`, `<string>`, and `<utility>` are included in `FilesSaveDialog.h` or `pch.h`.

- [ ] **Step 2: Store filters in SetFileTypes / SetFileTypeIndex**

In `src/Files.App.SaveDialog/FilesSaveDialog.cpp`, replace the body of `SetFileTypes` (currently returns S_OK at lines 537-544) with:

```cpp
HRESULT __stdcall CFilesSaveDialog::SetFileTypes(UINT cFileTypes, const COMDLG_FILTERSPEC* rgFilterSpec)
{
	cout << "SetFileTypes, cFileTypes: " << cFileTypes << endl;
#ifdef SYSTEMDIALOG
	return _systemDialog->SetFileTypes(cFileTypes, rgFilterSpec);
#endif
	_fileTypes.clear();
	for (UINT i = 0; i < cFileTypes; i++)
	{
		std::wstring name = rgFilterSpec[i].pszName ? rgFilterSpec[i].pszName : L"";
		std::wstring spec = rgFilterSpec[i].pszSpec ? rgFilterSpec[i].pszSpec : L"";
		// '|' is our delimiter; never expect it in a spec, but be safe.
		std::replace(name.begin(), name.end(), L'|', L' ');
		std::replace(spec.begin(), spec.end(), L'|', L' ');
		_fileTypes.push_back({ name, spec });
	}
	return S_OK;
}
```

Update `SetFileTypeIndex` (lines 546-553) to store the index:

```cpp
HRESULT __stdcall CFilesSaveDialog::SetFileTypeIndex(UINT iFileType)
{
	cout << "SetFileTypeIndex, iFileType: " << iFileType << endl;
#ifdef SYSTEMDIALOG
	return _systemDialog->SetFileTypeIndex(iFileType);
#endif
	_fileTypeIndex = iFileType;
	return S_OK;
}
```

Update `GetFileTypeIndex` (lines 555-563) to return the stored index:

```cpp
HRESULT __stdcall CFilesSaveDialog::GetFileTypeIndex(UINT* piFileType)
{
	cout << "GetFileTypeIndex" << endl;
#ifdef SYSTEMDIALOG
	return _systemDialog->GetFileTypeIndex(piFileType);
#endif
	*piFileType = _fileTypeIndex == 0 ? 1 : _fileTypeIndex;
	return S_OK;
}
```

Add `#include <algorithm>` near the top includes of `FilesSaveDialog.cpp` (for `std::replace`).

- [ ] **Step 3: Forward args in Show()**

In `src/Files.App.SaveDialog/FilesSaveDialog.cpp` `Show()` (lines 422-535): enlarge the args buffer and append the save-dialog args. Change `TCHAR args[1024]` (line 440) to `TCHAR args[8192]`, and replace the arg-building `if (_initFolder && ...) { ... } else { ... }` block (lines 445-461) with:

```cpp
	// Build the file-types payload: "Name1|*.png|Name2|*.*"
	std::wstring fileTypesArg;
	for (size_t i = 0; i < _fileTypes.size(); i++)
	{
		if (i > 0) fileTypesArg += L"|";
		fileTypesArg += _fileTypes[i].first + L"|" + _fileTypes[i].second;
	}

	if (_initFolder && SUCCEEDED(_initFolder->GetDisplayName(SIGDN_DESKTOPABSOLUTEPARSING, &pszPath)))
	{
		swprintf(args, _countof(args) - 1,
			L"\"%s\" -directory \"%s\" -outputpath \"%s\" -savedialog -saveas \"%s\" -filetypes \"%s\" -filetypeindex %u",
			szBuf, pszPath, _outputPath.c_str(), _initName.c_str(), fileTypesArg.c_str(), _fileTypeIndex == 0 ? 1 : _fileTypeIndex);
		wcout << L"Invoking: " << args << endl;
		CoTaskMemFree(pszPath);
	}
	else
	{
		swprintf(args, _countof(args) - 1,
			L"\"%s\" -outputpath \"%s\" -savedialog -saveas \"%s\" -filetypes \"%s\" -filetypeindex %u",
			szBuf, _outputPath.c_str(), _initName.c_str(), fileTypesArg.c_str(), _fileTypeIndex == 0 ? 1 : _fileTypeIndex);
	}
```

- [ ] **Step 4: Parse the result lines (path + optional index)**

In `Show()`, replace the result-reading loop (lines 500-510) with:

```cpp
	std::ifstream file(_outputPath);
	if (file.good())
	{
		std::string str;
		std::wstring_convert<std::codecvt_utf8_utf16<wchar_t>> converter;
		while (std::getline(file, str))
		{
			std::wstring wide = converter.from_bytes(str);
			if (wide.rfind(L"index=", 0) == 0)
			{
				try { _fileTypeIndex = (UINT)std::stoul(wide.substr(6)); } catch (...) {}
			}
			else if (!wide.empty())
			{
				_selectedItem = wide;
			}
		}
	}
```

- [ ] **Step 5: Build the native DLLs (x64 + Win32)**

Run (from a Developer prompt / via the package build, see Task 9; or directly with MSBuild):
```
msbuild src/Files.App.SaveDialog/Files.App.SaveDialog.vcxproj /p:Configuration=Release /p:Platform=x64
msbuild src/Files.App.SaveDialog/Files.App.SaveDialog.Win32.vcxproj /p:Configuration=Release /p:Platform=Win32
```
Expected: both compile; produce `Files.App.SaveDialog64.dll` / `Files.App.SaveDialog32.dll`.

> If MSBuild paths are awkward locally, the packaging script in Task 9 builds the native projects as part of the solution build; this step is just an isolated compile check.

- [ ] **Step 6: Commit**

```bash
git add src/Files.App.SaveDialog/FilesSaveDialog.h src/Files.App.SaveDialog/FilesSaveDialog.cpp
git commit -m "feat(save-dialog): forward filters/name to app and round-trip file-type index"
```

---

## Task 9: Version bump, package, install, register

**Files:**
- Modify: `src/Files.App/Package.appxmanifest`

- [ ] **Step 1: Bump version**

In `src/Files.App/Package.appxmanifest`, change `Version="4.1.4.9"` (line 19) to `Version="4.1.4.10"`.

- [ ] **Step 2: Build + package the MSIX**

Run: `pwsh ./tools/files-pro-package/Build-FilesProPackage.ps1` (inspect the script's parameters first; pass the same options codex used for `4.1.4.9` — typically Release/x64). Capture output to `build-filespro-save-dialog-package.log`.
Expected: `artifacts/files-pro-package/Files.App_4.1.4.10_x64_Test/Files.App_4.1.4.10_x64.msix` produced, and the native `Files.App.SaveDialog64.dll` present in the package payload.

- [ ] **Step 3: Install the package**

Run: `pwsh ./tools/files-pro-package/Install-FilesProPackage.ps1 -PackagePath ./artifacts/files-pro-package/Files.App_4.1.4.10_x64_Test/Files.App_4.1.4.10_x64.msix`
Expected: package installed; `%LOCALAPPDATA%\Packages\FilesDev_*\LocalState\FilesOpenDialog\Files.App.SaveDialog64.dll` refreshed.

- [ ] **Step 4: Confirm the dialog registration still points at the new DLL**

Run:
```powershell
Get-ItemProperty 'HKCU:\Software\Classes\CLSID\{C0B4E2F3-BA21-4773-8DBA-335EC946EB8B}\InprocServer32'
```
Expected: `(default)` points at the package LocalState `Files.App.SaveDialog64.dll`. If missing, run `pwsh ./tools/files-pro-shell/Register-FilesProShell.ps1 -Apply -LauncherPath (Get-Command files-dev.exe).Source`.

- [ ] **Step 5: Commit**

```bash
git add src/Files.App/Package.appxmanifest
git commit -m "build(save-dialog): bump version to 4.1.4.10"
```

---

## Task 10: End-to-end verification

No code changes — verification only.

- [ ] **Step 1: Delete any stale native trace**

Run: `Remove-Item "$env:OneDrive\Desktop\save_dialog.txt" -ErrorAction SilentlyContinue` (only exists if DEBUGLOG builds were used).

- [ ] **Step 2: Real save**

Restart Discord (so it reloads the rebuilt in-proc DLL). Trigger "Save image as…" on an image.
Expected: the Files Pro window opens with the **Save bar** at the bottom, File name prefilled (e.g. `5Julygamer.png`), Save-as-type populated. Navigate to a folder, click **Save**.
Verify: the file exists at the chosen folder with the correct name/extension, and Discord reports the save succeeded.

- [ ] **Step 3: Overwrite prompt**

Save again to the same name. Expected: "Replace existing file?" dialog appears; **Cancel** keeps the bar open; **Replace** overwrites.

- [ ] **Step 4: Cancel path**

Trigger a save, then click **Cancel** / close the window with X. Expected: no file written, no accidental overwrite of a highlighted file; the caller sees a normal cancel.

- [ ] **Step 5: Open dialog regression**

Trigger an Open/upload dialog (e.g. Discord "upload file"), select an existing file, confirm it returns. Expected: unchanged behavior.

- [ ] **Step 6: Logs**

Check `%LOCALAPPDATA%\Packages\FilesDev_*\LocalState\debug.log`: protocol activation reaches `Root content: MainPage`, no `NavigationFailed`. Confirm no exceptions during commit.

- [ ] **Step 7: Final unit tests**

Run: `dotnet test tests/Files.App.SaveDialog.UnitTests/Files.App.SaveDialog.UnitTests.csproj -c Debug -p:Platform=x64`
Expected: all pass.

---

## Self-review notes

- **Spec coverage:** UI bar (T6), file name box + prefilled (T5/T6), Save-as-type (T1 parse, T5/T6 UI), New Folder (T6/T7), Save/Cancel (T7), extension handling (T1), overwrite prompt (T7), remembered last folder (deferred — see below), type-index round-trip (T1/T5/T7/T8), native forwarding (T8), Window_Closed guard (T7), edge cases (T1 validation + T7 virtual-folder guard), build/deploy (T9), verification (T10).
- **Deferred from "Full parity":** *Remember last-used save folder* is intentionally not in tasks above to keep the first build focused; the native side already supplies a folder via `SetFolder`/`SetDefaultFolder` (the trace showed `Downloads`), so caller-driven folders work today. Add last-folder persistence as a follow-up (store on successful `CommitSaveResult`, prefer when caller sends no `-directory`). Flagged here so it is not silently dropped.
- **Type consistency:** `FileTypeChoice(Display, Pattern, PrimaryExtension)`, `SaveDialogPathHelper.{ParseFileTypes, ResolveTargetPath, IsValidFileName}`, `SaveDialogRequest(SuggestedName, FileTypes, TypeIndex)`, `SaveDialogViewModel.{Initialize, CommitRequested, CancelRequested, NewFolderRequested}` used consistently across tasks.
- **Verify-before-trusting:** several access chains (`ShellPanesPage.ActivePane.ShellViewModel.WorkingDirectory`, create-folder command, `PInvoke.CreateEvent`/`SetEvent` generated signatures) are called out inline to confirm against the real source during implementation.
