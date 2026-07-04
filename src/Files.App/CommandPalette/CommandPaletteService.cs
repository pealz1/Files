// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.FileOperations;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.IO;

namespace Files.App.CommandPalette
{
	internal sealed class CommandPaletteService : ICommandPaletteService
	{
		private readonly IFilesProCopyQueueService copyQueueService;
		private readonly ILogger<CommandPaletteService> logger;

		public CommandPaletteService(
			IFilesProCopyQueueService copyQueueService,
			ILogger<CommandPaletteService> logger)
		{
			this.copyQueueService = copyQueueService;
			this.logger = logger;
		}

		private static readonly CommandPaletteItem[] Commands =
		[
			new()
			{
				Id = "files-pro.search",
				Title = "Search files",
				Subtitle = "Run an indexed filename or content search",
				Category = "Index",
				Glyph = "\uE721"
			},
			new()
			{
				Id = "files-pro.downloads-cleanup",
				Title = "Open Downloads Cleanup",
				Subtitle = "Review dry-run cleanup suggestions",
				Category = "Cleanup",
				Glyph = "\uE896"
			},
			new()
			{
				Id = "files-pro.large-files",
				Title = "Find Large Files",
				Subtitle = "Scan a selected folder for the largest files",
				Category = "Storage",
				Glyph = "\uE8B7"
			},
			new()
			{
				Id = "files-pro.roblox",
				Title = "Find Roblox Scripts",
				Subtitle = "Locate Lua, Luau, rbxl, rbxm, and Rojo files",
				Category = "Roblox",
				Glyph = "\uE8A5"
			},
			new()
			{
				Id = "files-pro.git-repos",
				Title = "Find Git Repos",
				Subtitle = "List detected repositories, branch names, and dirty state",
				Category = "Projects",
				Glyph = "\uE943"
			},
			new()
			{
				Id = "files-pro.duplicate-archives",
				Title = "Show Duplicate Archives",
				Subtitle = "Use staged size and hash checks before reporting duplicates",
				Category = "Storage",
				Glyph = "\uE8C8"
			},
			new()
			{
				Id = "files-pro.terminal-here",
				Title = "Open Terminal Here",
				Subtitle = "Open a terminal at the active folder",
				Category = "Action",
				Glyph = "\uE756"
			},
			new()
			{
				Id = "files-pro.open-vscode",
				Title = "Open in VS Code",
				Subtitle = "Open the active project or folder in VS Code",
				Category = "Developer",
				Glyph = "\uE943"
			},
			new()
			{
				Id = "files-pro.copy-path",
				Title = "Copy Full Path",
				Subtitle = "Copy the selected item path",
				Category = "Action",
				Glyph = "\uE8C8"
			},
			new()
			{
				Id = "files-pro.compare-folders",
				Title = "Compare Folders",
				Subtitle = "Prepare a folder comparison workflow",
				Category = "Dual pane",
				Glyph = "\uE8AB"
			},
			new()
			{
				Id = "files-pro.batch-rename",
				Title = "Batch Rename",
				Subtitle = "Open the multi-rename workflow",
				Category = "Batch",
				Glyph = "\uE8AC"
			},
			new()
			{
				Id = "files-pro.hash-files",
				Title = "Hash Selected Files",
				Subtitle = "Calculate file checksums for selected files",
				Category = "Tools",
				Glyph = "\uE950"
			},
			new()
			{
				Id = "files-pro.copy-queue-status",
				Title = "Show Copy Queue",
				Subtitle = "Show queued copy and move progress",
				Category = "File operations",
				Glyph = "\uE8CB"
			},
			new()
			{
				Id = "files-pro.shell-command",
				Title = "Run Shell Command",
				Subtitle = "Requires explicit confirmation before execution",
				Category = "Dangerous",
				Glyph = "\uE756",
				RequiresConfirmation = true
			},
		];

		public Task<IReadOnlyList<CommandPaletteItem>> GetCommandsAsync(CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			return Task.FromResult<IReadOnlyList<CommandPaletteItem>>(Commands);
		}

		public Task<IReadOnlyList<CommandPaletteItem>> SearchAsync(
			CommandPaletteQuery query,
			CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (string.IsNullOrWhiteSpace(query.Text))
				return Task.FromResult<IReadOnlyList<CommandPaletteItem>>(Commands.Take(query.MaxResults).ToArray());

			var terms = query.Text
				.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

			var results = Commands
				.Select(command => new
				{
					Command = command,
					Rank = terms.Sum(term => Rank(command, term))
				})
				.Where(x => x.Rank > 0)
				.OrderByDescending(x => x.Rank)
				.ThenBy(x => x.Command.Title)
				.Take(query.MaxResults)
				.Select(x => x.Command)
				.ToArray();

			return Task.FromResult<IReadOnlyList<CommandPaletteItem>>(results);
		}

		public Task<CommandPaletteExecutionResult> ExecuteAsync(
			CommandPaletteExecutionRequest request,
			CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var command = Commands.FirstOrDefault(x => string.Equals(x.Id, request.CommandId, StringComparison.OrdinalIgnoreCase));
			if (command is null)
				return Task.FromResult(Failed("Unknown command."));

			if (command.RequiresConfirmation && !request.Confirmed)
				return Task.FromResult(Failed("Command requires explicit confirmation."));

			try
			{
				return Task.FromResult(request.CommandId switch
				{
					"files-pro.downloads-cleanup" => View("Downloads Cleanup", "Downloads cleanup view selected."),
					"files-pro.large-files" => View("Large Files", "Large files view selected."),
					"files-pro.roblox" => View("Roblox", "Roblox view selected."),
					"files-pro.git-repos" => View("Projects", "Projects view selected."),
					"files-pro.duplicate-archives" => View("Duplicates", "Duplicate review view selected."),
					"files-pro.search" => View("Search Index", "Search index view selected."),
					"files-pro.copy-path" => Succeeded($"Active path: {NormalizeWorkingDirectory(request.WorkingDirectory)}"),
					"files-pro.copy-queue-status" => CopyQueueStatus(),
					"files-pro.terminal-here" => LaunchTerminal(request.WorkingDirectory),
					"files-pro.open-vscode" => LaunchProcess("code", Quote(NormalizeWorkingDirectory(request.WorkingDirectory)), request.WorkingDirectory, "Opened VS Code."),
					"files-pro.shell-command" => RunConfirmedShellCommand(request),
					_ => Succeeded($"Command registered: {command.Title}")
				});
			}
			catch (Exception ex)
			{
				logger.LogWarning(ex, "Files Pro command palette execution failed for {CommandId}", request.CommandId);
				return Task.FromResult(Failed(ex.Message));
			}
		}

		private static int Rank(CommandPaletteItem command, string term)
		{
			if (command.Title.Equals(term, StringComparison.OrdinalIgnoreCase))
				return 1000;
			if (command.Title.StartsWith(term, StringComparison.OrdinalIgnoreCase))
				return 600;
			if (command.Title.Contains(term, StringComparison.OrdinalIgnoreCase))
				return 350;
			if (command.Category.Contains(term, StringComparison.OrdinalIgnoreCase))
				return 250;
			if (command.Subtitle.Contains(term, StringComparison.OrdinalIgnoreCase))
				return 100;

			return 0;
		}

		private static CommandPaletteExecutionResult LaunchTerminal(string workingDirectory)
		{
			var normalized = NormalizeWorkingDirectory(workingDirectory);
			var result = LaunchProcess("wt.exe", $"-d {Quote(normalized)}", normalized, "Opened Windows Terminal.");
			if (result.Succeeded)
				return result;

			return LaunchProcess("powershell.exe", "-NoExit", normalized, "Opened PowerShell.");
		}

		private CommandPaletteExecutionResult CopyQueueStatus()
		{
			var snapshot = copyQueueService.GetSnapshot();
			if (!snapshot.IsRunning)
				return Succeeded("Copy queue is idle.");

			var byteProgress = snapshot.TotalBytes > 0
				? $" {snapshot.BytesProcessed.ToSizeString()} / {snapshot.TotalBytes.ToSizeString()} ({snapshot.PercentComplete:0.#}%)."
				: string.Empty;
			return Succeeded($"Copy queue running: {snapshot.CompletedCount:#,##0} completed, {snapshot.PendingCount:#,##0} pending.{byteProgress} Current: {snapshot.CurrentPath}");
		}

		private static CommandPaletteExecutionResult RunConfirmedShellCommand(CommandPaletteExecutionRequest request)
		{
			if (string.IsNullOrWhiteSpace(request.ShellCommand))
				return Failed("No shell command was supplied.");

			var normalized = NormalizeWorkingDirectory(request.WorkingDirectory);
			return LaunchProcess(
				"powershell.exe",
				$"-NoExit -Command {Quote(request.ShellCommand)}",
				normalized,
				"Opened confirmed shell command in PowerShell.");
		}

		private static CommandPaletteExecutionResult LaunchProcess(
			string fileName,
			string arguments,
			string workingDirectory,
			string successMessage)
		{
			var normalized = NormalizeWorkingDirectory(workingDirectory);
			var startInfo = new ProcessStartInfo(fileName, arguments)
			{
				UseShellExecute = true,
				WorkingDirectory = normalized
			};

			Process.Start(startInfo);
			return Succeeded(successMessage);
		}

		private static string NormalizeWorkingDirectory(string workingDirectory)
			=> Directory.Exists(workingDirectory)
				? workingDirectory
				: Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

		private static string Quote(string value)
			=> "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

		private static CommandPaletteExecutionResult Succeeded(string message)
			=> new()
			{
				Succeeded = true,
				Message = message
			};

		private static CommandPaletteExecutionResult Failed(string message)
			=> new()
			{
				Succeeded = false,
				Message = message
			};

		private static CommandPaletteExecutionResult View(string viewName, string message)
			=> new()
			{
				Succeeded = true,
				Message = message,
				TargetView = viewName
			};
	}
}
