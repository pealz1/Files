// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Diagnostics;
using Microsoft.Extensions.Logging;
using System.IO;
using Windows.Storage;

namespace Files.App.Cleanup
{
	internal sealed class CleanupPlanService : ICleanupPlanService
	{
		private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
		{
			".zip",
			".rar",
			".7z",
			".tar",
			".gz",
			".bz2",
			".xz"
		};

		private static readonly HashSet<string> InstallerExtensions = new(StringComparer.OrdinalIgnoreCase)
		{
			".exe",
			".msi",
			".msix",
			".msixbundle",
			".appx",
			".appxbundle"
		};

		private static readonly HashSet<string> MediaExtensions = new(StringComparer.OrdinalIgnoreCase)
		{
			".png",
			".jpg",
			".jpeg",
			".gif",
			".webp",
			".mp4",
			".mov",
			".mkv",
			".avi",
			".mp3",
			".wav",
			".flac"
		};

		private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
		{
			".pdf",
			".doc",
			".docx",
			".xls",
			".xlsx",
			".ppt",
			".pptx",
			".txt",
			".md",
			".csv",
			".json"
		};

		private static readonly HashSet<string> ScriptExtensions = new(StringComparer.OrdinalIgnoreCase)
		{
			".ps1",
			".bat",
			".cmd",
			".sh",
			".js",
			".ts",
			".py",
			".lua",
			".luau"
		};

		private static readonly HashSet<string> RobloxExtensions = new(StringComparer.OrdinalIgnoreCase)
		{
			".lua",
			".luau",
			".rbxl",
			".rbxlx",
			".rbxm",
			".rbxmx"
		};

		private readonly ILogger<CleanupPlanService> logger;

		public CleanupPlanService(ILogger<CleanupPlanService> logger)
		{
			this.logger = logger;
		}

		public Task<IReadOnlyList<CleanupSuggestion>> CreateDownloadsPlanAsync(
			DownloadsCleanupOptions options,
			IProgress<FilesProScanProgress>? progress,
			CancellationToken cancellationToken)
		{
			return Task.Run(() => CreatePlanCore(options, progress, cancellationToken), cancellationToken);
		}

		public Task<CleanupMovePlan> CreateDryRunMovePlanAsync(
			IReadOnlyList<CleanupSuggestion> suggestions,
			string destinationRoot,
			CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var items = suggestions
				.Where(x => !string.Equals(x.SuggestedAction, "Leave alone", StringComparison.OrdinalIgnoreCase))
				.Where(x => !string.Equals(x.SuggestedAction, "Review manually", StringComparison.OrdinalIgnoreCase))
				.Select(suggestion =>
				{
					cancellationToken.ThrowIfCancellationRequested();
					var destinationDirectory = Path.Combine(destinationRoot, SanitizeFolderName(suggestion.Category));
					return new CleanupMovePlanItem
					{
						SourcePath = suggestion.Path,
						DestinationPath = Path.Combine(destinationDirectory, suggestion.Name),
						Action = suggestion.SuggestedAction,
						Reason = suggestion.Reason,
						Risk = suggestion.Risk
					};
				})
				.ToArray();

			return Task.FromResult(new CleanupMovePlan { Items = items });
		}

		public Task<CleanupExecutionResult> ExecuteMovePlanAsync(
			CleanupMovePlan plan,
			CleanupExecutionOptions options,
			IProgress<FilesProScanProgress>? progress,
			CancellationToken cancellationToken)
		{
			return Task.Run(() => ExecuteMovePlanCore(plan, options, progress, cancellationToken), cancellationToken);
		}

		public Task<CleanupExecutionResult> ExecuteDeletePlanAsync(
			CleanupDeletePlan plan,
			CleanupExecutionOptions options,
			IProgress<FilesProScanProgress>? progress,
			CancellationToken cancellationToken)
		{
			return Task.Run(() => ExecuteDeletePlanCore(plan, options, progress, cancellationToken), cancellationToken);
		}

		private IReadOnlyList<CleanupSuggestion> CreatePlanCore(
			DownloadsCleanupOptions options,
			IProgress<FilesProScanProgress>? progress,
			CancellationToken cancellationToken)
		{
			if (!Directory.Exists(options.DownloadsPath))
				return [];

			var suggestions = new List<CleanupSuggestion>();
			var directories = GetDirectories(options.DownloadsPath)
				.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
			var files = GetFiles(options.DownloadsPath).ToArray();
			var recentCutoff = DateTimeOffset.Now.AddDays(-Math.Max(1, options.RecentDays));

			for (var i = 0; i < files.Length && suggestions.Count < options.MaxItems; i++)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var file = files[i];
				progress?.Report(new("Downloads cleanup", file.FullName, 1, i + 1));

				var baseName = Path.GetFileNameWithoutExtension(file.Name);
				var hasExtractedPair = ArchiveExtensions.Contains(file.Extension) && directories.ContainsKey(baseName);

				suggestions.Add(CreateFileSuggestion(file, hasExtractedPair, recentCutoff));
			}

			foreach (var directory in directories.Values.OrderByDescending(x => x.LastWriteTime).Take(options.MaxItems - suggestions.Count))
			{
				cancellationToken.ThrowIfCancellationRequested();

				if (LooksLikeBuildOrCacheFolder(directory.Name))
				{
					suggestions.Add(new()
					{
						Name = directory.Name,
						Path = directory.FullName,
						Category = "Build/cache folder",
						SuggestedAction = "Review manually",
						Reason = "Folder name usually indicates generated dependencies, build output, or cache data.",
						Risk = "Risky",
						LastModified = new DateTimeOffset(directory.LastWriteTime)
					});
				}
			}

			return suggestions
				.OrderByDescending(x => x.Risk == "Risky")
				.ThenByDescending(x => x.LastModified)
				.Take(options.MaxItems)
				.ToArray();
		}

		private CleanupExecutionResult ExecuteMovePlanCore(
			CleanupMovePlan plan,
			CleanupExecutionOptions options,
			IProgress<FilesProScanProgress>? progress,
			CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var logPath = GetOperationLogPath();
			Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
			var results = new List<CleanupExecutionItemResult>();

			if (!options.AllowMoves || !string.Equals(options.ConfirmationText, "MOVE", StringComparison.Ordinal))
			{
				return new()
				{
					LogPath = logPath,
					Items =
					[
						new()
						{
							Succeeded = false,
							Message = "Cleanup execution requires the review toggle and MOVE confirmation."
						}
					]
				};
			}

			for (var index = 0; index < plan.Items.Count; index++)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var item = plan.Items[index];
				progress?.Report(new("Cleanup moves", item.SourcePath, 0, index + 1));

				var result = ExecuteMove(item);
				results.Add(result);
				AppendOperationLog(logPath, result);
			}

			return new()
			{
				LogPath = logPath,
				Items = results
			};
		}

		private CleanupExecutionResult ExecuteDeletePlanCore(
			CleanupDeletePlan plan,
			CleanupExecutionOptions options,
			IProgress<FilesProScanProgress>? progress,
			CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var logPath = GetOperationLogPath();
			Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
			var results = new List<CleanupExecutionItemResult>();

			if (!options.AllowDeletes || !string.Equals(options.ConfirmationText, "DELETE", StringComparison.Ordinal))
			{
				return new()
				{
					LogPath = logPath,
					Items =
					[
						new()
						{
							Succeeded = false,
							Message = "Recycle-bin execution requires the delete review toggle and DELETE confirmation."
						}
					]
				};
			}

			if (!options.UseRecycleBin)
			{
				return new()
				{
					LogPath = logPath,
					Items =
					[
						new()
						{
							Succeeded = false,
							Message = "Permanent delete is blocked for Files Pro cleanup execution."
						}
					]
				};
			}

			for (var index = 0; index < plan.Items.Count; index++)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var item = plan.Items[index];
				progress?.Report(new("Recycle cleanup item", item.SourcePath, 0, index + 1));

				var result = ExecuteRecycleDelete(item);
				results.Add(result);
				AppendOperationLog(logPath, result);
			}

			return new()
			{
				LogPath = logPath,
				Items = results
			};
		}

		private CleanupExecutionItemResult ExecuteMove(CleanupMovePlanItem item)
		{
			try
			{
				if (IsProtectedPath(item.SourcePath))
				{
					return Failure(item, "Protected or risky source path.");
				}

				if (!File.Exists(item.SourcePath) && !Directory.Exists(item.SourcePath))
				{
					return Failure(item, "Source no longer exists.");
				}

				var destinationDirectory = Path.GetDirectoryName(item.DestinationPath);
				if (string.IsNullOrWhiteSpace(destinationDirectory))
					return Failure(item, "Destination directory is invalid.");

				Directory.CreateDirectory(destinationDirectory);
				var destination = GetUniqueDestinationPath(item.DestinationPath);

				if (File.Exists(item.SourcePath))
					File.Move(item.SourcePath, destination);
				else
					Directory.Move(item.SourcePath, destination);

				return new()
				{
					SourcePath = item.SourcePath,
					DestinationPath = destination,
					Succeeded = true,
					Message = "Moved to review location."
				};
			}
			catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SystemException)
			{
				return Failure(item, ex.Message);
			}
		}

		private CleanupExecutionItemResult ExecuteRecycleDelete(CleanupDeletePlanItem item)
		{
			try
			{
				if (IsProtectedPath(item.SourcePath))
				{
					return Failure(item, "Protected or risky source path.");
				}

				if (File.Exists(item.SourcePath))
				{
					Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
						item.SourcePath,
						Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
						Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
				}
				else if (Directory.Exists(item.SourcePath))
				{
					Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
						item.SourcePath,
						Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
						Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
				}
				else
				{
					return Failure(item, "Source no longer exists.");
				}

				return new()
				{
					SourcePath = item.SourcePath,
					Succeeded = true,
					Message = "Moved to Recycle Bin."
				};
			}
			catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SystemException)
			{
				return Failure(item, ex.Message);
			}
		}

		private CleanupSuggestion CreateFileSuggestion(FileInfo file, bool hasExtractedPair, DateTimeOffset recentCutoff)
		{
			var modified = new DateTimeOffset(file.LastWriteTime);
			var isRecent = modified >= recentCutoff;
			var extension = file.Extension;
			var category = ClassifyFile(extension, file.Name);

			if (isRecent)
			{
				return Build(file, category, "Leave alone", "Recently modified downloads are kept out of cleanup plans.", "Normal");
			}

			if (hasExtractedPair)
			{
				return Build(file, "Extracted archive pair", "Review extracted pair", "An archive with the same base name as a sibling folder was detected.", "Risky");
			}

			return category switch
			{
				"Installer" => Build(file, category, "Move to Installers", "Old installer package in Downloads.", "Normal"),
				"Archive" => Build(file, category, "Move to Archives", "Old archive package in Downloads.", "Normal"),
				"Media" => Build(file, category, "Move to Media", "Media file can usually be filed outside Downloads.", "Normal"),
				"Roblox" => Build(file, category, "Move to Roblox review", "Roblox or Lua-related file found in Downloads.", "Normal"),
				"Script" => Build(file, category, "Review manually", "Script files should be reviewed before moving.", "Risky"),
				"Session export" => Build(file, category, "Move to Session Exports", "Export-like file name found in Downloads.", "Normal"),
				"Game archive" => Build(file, category, "Move to Game Archives", "Game/mod/archive naming pattern found.", "Normal"),
				"Document" => Build(file, category, "Move to Documents", "Document-like file found in Downloads.", "Normal"),
				_ => Build(file, category, "Review manually", "No safe category matched.", "Normal")
			};
		}

		private static CleanupSuggestion Build(FileInfo file, string category, string action, string reason, string risk)
		{
			return new()
			{
				Name = file.Name,
				Path = file.FullName,
				Category = category,
				SuggestedAction = action,
				Reason = reason,
				Risk = risk,
				LastModified = new DateTimeOffset(file.LastWriteTime)
			};
		}

		private static string ClassifyFile(string extension, string name)
		{
			if (RobloxExtensions.Contains(extension))
				return "Roblox";
			if (LooksLikeSessionExport(name))
				return "Session export";
			if (LooksLikeGameArchive(name) && ArchiveExtensions.Contains(extension))
				return "Game archive";
			if (InstallerExtensions.Contains(extension))
				return "Installer";
			if (ArchiveExtensions.Contains(extension))
				return "Archive";
			if (MediaExtensions.Contains(extension))
				return "Media";
			if (ScriptExtensions.Contains(extension))
				return "Script";
			if (DocumentExtensions.Contains(extension))
				return "Document";

			return "Unknown";
		}

		private static bool LooksLikeSessionExport(string name)
			=> name.Contains("export", StringComparison.OrdinalIgnoreCase) ||
				name.Contains("transcript", StringComparison.OrdinalIgnoreCase) ||
				name.Contains("session", StringComparison.OrdinalIgnoreCase);

		private static bool LooksLikeGameArchive(string name)
			=> name.Contains("mod", StringComparison.OrdinalIgnoreCase) ||
				name.Contains("game", StringComparison.OrdinalIgnoreCase) ||
				name.Contains("asset", StringComparison.OrdinalIgnoreCase) ||
				name.Contains("texture", StringComparison.OrdinalIgnoreCase);

		private static bool LooksLikeBuildOrCacheFolder(string name)
			=> name.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ||
				name.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
				name.Equals(".cache", StringComparison.OrdinalIgnoreCase) ||
				name.Equals("dist", StringComparison.OrdinalIgnoreCase) ||
				name.Equals("build", StringComparison.OrdinalIgnoreCase) ||
				name.Equals("target", StringComparison.OrdinalIgnoreCase);

		private static string SanitizeFolderName(string value)
		{
			var invalidCharacters = Path.GetInvalidFileNameChars();
			var sanitized = new string(value.Select(character => invalidCharacters.Contains(character) ? '-' : character).ToArray());
			return string.IsNullOrWhiteSpace(sanitized) ? "Review" : sanitized;
		}

		private static string GetUniqueDestinationPath(string destinationPath)
		{
			if (!File.Exists(destinationPath) && !Directory.Exists(destinationPath))
				return destinationPath;

			var directory = Path.GetDirectoryName(destinationPath) ?? string.Empty;
			var fileName = Path.GetFileNameWithoutExtension(destinationPath);
			var extension = Path.GetExtension(destinationPath);

			for (var index = 1; index < 10000; index++)
			{
				var candidate = Path.Combine(directory, $"{fileName} ({index}){extension}");
				if (!File.Exists(candidate) && !Directory.Exists(candidate))
					return candidate;
			}

			throw new IOException("Could not create a unique destination path.");
		}

		private static bool IsProtectedPath(string path)
		{
			if (string.IsNullOrWhiteSpace(path))
				return true;

			var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			var protectedRoots = new[]
			{
				Environment.GetFolderPath(Environment.SpecialFolder.Windows),
				Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
				Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
				Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
				Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
			}
			.Where(x => !string.IsNullOrWhiteSpace(x))
			.Select(x => Path.GetFullPath(x).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

			if (protectedRoots.Any(root => string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase)))
				return true;

			try
			{
				var attributes = File.GetAttributes(fullPath);
				return attributes.HasFlag(System.IO.FileAttributes.System);
			}
			catch
			{
				return false;
			}
		}

		private static string GetOperationLogPath()
			=> Path.Combine(
				ApplicationData.Current.LocalFolder.Path,
				"FilesPro",
				"Operations",
				$"cleanup-{DateTimeOffset.Now:yyyyMMdd}.log");

		private static void AppendOperationLog(string logPath, CleanupExecutionItemResult result)
		{
			var line = $"{DateTimeOffset.Now:O}\t{result.Succeeded}\t{result.SourcePath}\t{result.DestinationPath}\t{result.Message}";
			File.AppendAllLines(logPath, [line]);
		}

		private static CleanupExecutionItemResult Failure(CleanupMovePlanItem item, string message)
			=> new()
			{
				SourcePath = item.SourcePath,
				DestinationPath = item.DestinationPath,
				Succeeded = false,
				Message = message
			};

		private static CleanupExecutionItemResult Failure(CleanupDeletePlanItem item, string message)
			=> new()
			{
				SourcePath = item.SourcePath,
				Succeeded = false,
				Message = message
			};

		private IEnumerable<FileInfo> GetFiles(string path)
		{
			try
			{
				return new DirectoryInfo(path).EnumerateFiles();
			}
			catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SystemException)
			{
				logger.LogDebug(ex, "Files Pro cleanup scan skipped files in {Path}", path);
				return [];
			}
		}

		private IEnumerable<DirectoryInfo> GetDirectories(string path)
		{
			try
			{
				return new DirectoryInfo(path).EnumerateDirectories();
			}
			catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SystemException)
			{
				logger.LogDebug(ex, "Files Pro cleanup scan skipped directories in {Path}", path);
				return [];
			}
		}
	}
}
