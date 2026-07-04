// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Diagnostics;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Windows.Storage;

namespace Files.App.StorageAnalysis
{
	internal sealed class StorageScanService : IStorageScanService
	{
		private static readonly HashSet<string> SkipDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
		{
			"$Recycle.Bin",
			"System Volume Information",
			".git",
			"node_modules",
			".cache",
			".venv",
			"__pycache__"
		};

		private static readonly HashSet<string> WastedSpaceDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
		{
			"node_modules",
			".git",
			".cache",
			".turbo",
			"dist",
			"build",
			"target",
			"Temp",
			"tmp",
			"Library"
		};

		private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
		{
			".zip",
			".rar",
			".7z",
			".tar",
			".gz",
			".bz2",
			".xz",
			".iso"
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

		private readonly ILogger<StorageScanService> logger;

		public StorageScanService(ILogger<StorageScanService> logger)
		{
			this.logger = logger;
		}

		public Task<IReadOnlyList<LargeFileInfo>> ScanLargeFilesAsync(
			LargeFileScanOptions options,
			IProgress<FilesProScanProgress>? progress,
			CancellationToken cancellationToken)
		{
			return Task.Run(() => ScanCore(options, progress, cancellationToken), cancellationToken);
		}

		public Task<IReadOnlyList<DuplicateGroupInfo>> FindDuplicatesAsync(
			DuplicateScanOptions options,
			IProgress<FilesProScanProgress>? progress,
			CancellationToken cancellationToken)
		{
			return Task.Run(() => FindDuplicatesCore(options, progress, cancellationToken), cancellationToken);
		}

		public Task<IReadOnlyList<WastedSpaceSuggestion>> FindWastedSpaceAsync(
			WastedSpaceScanOptions options,
			IProgress<FilesProScanProgress>? progress,
			CancellationToken cancellationToken)
		{
			return Task.Run(() => FindWastedSpaceCore(options, progress, cancellationToken), cancellationToken);
		}

		public Task<IReadOnlyList<TreemapTileInfo>> BuildTreemapAsync(
			TreemapScanOptions options,
			IProgress<FilesProScanProgress>? progress,
			CancellationToken cancellationToken)
		{
			return Task.Run(() => BuildTreemapCore(options, progress, cancellationToken), cancellationToken);
		}

		public Task<StorageAnalysisSnapshot> AnalyzeStorageAsync(
			StorageAnalysisOptions options,
			IProgress<FilesProScanProgress>? progress,
			CancellationToken cancellationToken)
		{
			return Task.Run(() => AnalyzeStorageCore(options, progress, cancellationToken), cancellationToken);
		}

		public Task<NtfsFastPathStatus> GetNtfsFastPathStatusAsync(
			string rootPath,
			CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			return Task.FromResult(GetNtfsFastPathStatus(rootPath));
		}

		public Task<StorageReportExportResult> ExportReportAsync(
			StorageReportSnapshot snapshot,
			StorageReportExportFormat format,
			string? outputFolder,
			CancellationToken cancellationToken)
		{
			return Task.Run(() => ExportReportCore(snapshot, format, outputFolder, cancellationToken), cancellationToken);
		}

		private IReadOnlyList<LargeFileInfo> ScanCore(
			LargeFileScanOptions options,
			IProgress<FilesProScanProgress>? progress,
			CancellationToken cancellationToken)
		{
			if (!Directory.Exists(options.RootPath))
				return [];

			var status = GetNtfsFastPathStatus(options.RootPath);
			if (status.CanUseUsnJournal &&
				NtfsUsnScanner.TryScanLargeFiles(options, progress, cancellationToken, out var usnFiles, out var usnReason))
			{
				logger.LogDebug("Files Pro used NTFS USN large-file scan for {RootPath}: {Reason}", options.RootPath, usnReason);
				return usnFiles;
			}

			var files = new List<LargeFileInfo>();
			var counters = new ScanCounters();
			ScanDirectory(new DirectoryInfo(options.RootPath), 0, options, files, counters, progress, cancellationToken);

			return files
				.OrderByDescending(x => x.SizeBytes)
				.Take(options.MaxResults)
				.ToArray();
		}

		private StorageAnalysisSnapshot AnalyzeStorageCore(
			StorageAnalysisOptions options,
			IProgress<FilesProScanProgress>? progress,
			CancellationToken cancellationToken)
		{
			if (!Directory.Exists(options.RootPath))
				return new() { RootPath = options.RootPath, Reason = "Root path does not exist." };

			var status = GetNtfsFastPathStatus(options.RootPath);
			if (status.CanUseUsnJournal &&
				NtfsUsnScanner.TryBuildStorageSnapshot(options, progress, cancellationToken, out var usnSnapshot, out var usnReason))
			{
				logger.LogDebug("Files Pro used NTFS USN/MFT storage analysis for {RootPath}: {Reason}", options.RootPath, usnReason);
				return usnSnapshot;
			}

			logger.LogDebug("Files Pro storage analysis is using recursive fallback for {RootPath}: {Reason}", options.RootPath, status.Reason);
			return AnalyzeStorageRecursive(options, status.Reason, progress, cancellationToken);
		}

		private StorageAnalysisSnapshot AnalyzeStorageRecursive(
			StorageAnalysisOptions options,
			string fallbackReason,
			IProgress<FilesProScanProgress>? progress,
			CancellationToken cancellationToken)
		{
			var rootPath = Path.GetFullPath(options.RootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			var largeFiles = new List<LargeFileInfo>();
			var folderStats = new Dictionary<string, FolderAccumulator>(StringComparer.OrdinalIgnoreCase);
			var extensionStats = new Dictionary<string, ExtensionAccumulator>(StringComparer.OrdinalIgnoreCase);
			var counters = new StorageAnalysisCounters();
			var totalBytes = 0UL;

			AnalyzeDirectory(
				new DirectoryInfo(rootPath),
				rootPath,
				0,
				options,
				largeFiles,
				folderStats,
				extensionStats,
				counters,
				ref totalBytes,
				progress,
				cancellationToken);

			var largestFolders = folderStats
				.Select(pair => new FolderSizeInfo
				{
					Name = Path.GetFileName(pair.Key.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
					Path = pair.Key,
					SizeBytes = pair.Value.SizeBytes,
					FileCount = pair.Value.FileCount,
					DirectoryCount = pair.Value.DirectoryCount
				})
				.OrderByDescending(x => x.SizeBytes)
				.Take(options.MaxFolders)
				.ToArray();

			var selectedTreemapEntries = largestFolders
				.Where(x => x.SizeBytes > 0)
				.Take(options.MaxTreemapTiles)
				.Select(x => new TreemapEntry(
					string.IsNullOrWhiteSpace(x.Name) ? x.Path : x.Name,
					x.Path,
					"Folder",
					x.SizeBytes,
					GetRelativeDepth(rootPath, x.Path)))
				.ToArray();

			return new()
			{
				RootPath = rootPath,
				ScanMode = "Recursive fallback",
				Reason = fallbackReason,
				TotalBytes = totalBytes,
				FileCount = counters.FilesVisited,
				DirectoryCount = counters.DirectoriesVisited,
				WasTruncated = counters.WasTruncated,
				LargeFiles = largeFiles
					.OrderByDescending(x => x.SizeBytes)
					.Take(options.MaxLargeFiles)
					.ToArray(),
				LargestFolders = largestFolders,
				Extensions = extensionStats
					.Select(pair => new ExtensionSizeInfo
					{
						Extension = pair.Key,
						SizeBytes = pair.Value.SizeBytes,
						FileCount = pair.Value.FileCount
					})
					.OrderByDescending(x => x.SizeBytes)
					.Take(options.MaxExtensions)
					.ToArray(),
				TreemapTiles = LayoutTreemap(selectedTreemapEntries, 1000d, 420d)
			};
		}

		private IReadOnlyList<DuplicateGroupInfo> FindDuplicatesCore(
			DuplicateScanOptions options,
			IProgress<FilesProScanProgress>? progress,
			CancellationToken cancellationToken)
		{
			if (!Directory.Exists(options.RootPath))
				return [];

			var sizeGroups = new Dictionary<long, List<FileInfo>>();
			var counters = new ScanCounters();
			CollectDuplicateCandidates(new DirectoryInfo(options.RootPath), 0, options, sizeGroups, counters, progress, cancellationToken);

			var groups = new List<DuplicateGroupInfo>();
			foreach (var sizeGroup in sizeGroups
				.Where(pair => pair.Value.Count > 1)
				.OrderByDescending(pair => pair.Key)
				.Select(pair => pair.Value))
			{
				cancellationToken.ThrowIfCancellationRequested();

				foreach (var partialGroup in sizeGroup
					.GroupBy(file => ComputePartialHash(file, options.PartialHashBytes, cancellationToken))
					.Where(group => !string.IsNullOrWhiteSpace(group.Key) && group.Count() > 1))
				{
					cancellationToken.ThrowIfCancellationRequested();

					foreach (var fullHashGroup in partialGroup
						.GroupBy(file => ComputeFullHash(file, cancellationToken))
						.Where(group => !string.IsNullOrWhiteSpace(group.Key) && group.Count() > 1))
					{
						var items = fullHashGroup
							.Select(file => new DuplicateFileInfo
							{
								Name = file.Name,
								Path = file.FullName,
								SizeBytes = (ulong)Math.Max(0L, file.Length),
								LastModified = new DateTimeOffset(file.LastWriteTime),
								PartialHash = partialGroup.Key,
								FullHash = fullHashGroup.Key
							})
							.OrderByDescending(file => file.LastModified)
							.ToArray();

						groups.Add(new()
						{
							SizeBytes = items.First().SizeBytes,
							VerificationLevel = "Full hash",
							Items = items
						});

						progress?.Report(new("Duplicate scan", items.First().Path, groups.Count, counters.CandidateFilesRetained));
						if (groups.Count >= options.MaxGroups)
							return groups.OrderByDescending(x => x.WastedBytes).ToArray();
					}
				}
			}

			return groups
				.OrderByDescending(x => x.WastedBytes)
				.Take(options.MaxGroups)
				.ToArray();
		}

		private IReadOnlyList<WastedSpaceSuggestion> FindWastedSpaceCore(
			WastedSpaceScanOptions options,
			IProgress<FilesProScanProgress>? progress,
			CancellationToken cancellationToken)
		{
			if (!Directory.Exists(options.RootPath))
				return [];

			var suggestions = new List<WastedSpaceSuggestion>();
			var counters = new ScanCounters();
			ScanWasteDirectory(new DirectoryInfo(options.RootPath), 0, options, suggestions, counters, progress, cancellationToken);

			return suggestions
				.OrderByDescending(x => x.Risk == "Risky")
				.ThenByDescending(x => x.SizeBytes)
				.Take(options.MaxResults)
				.ToArray();
		}

		private IReadOnlyList<TreemapTileInfo> BuildTreemapCore(
			TreemapScanOptions options,
			IProgress<FilesProScanProgress>? progress,
			CancellationToken cancellationToken)
		{
			if (!Directory.Exists(options.RootPath))
				return [];

			var root = new DirectoryInfo(options.RootPath);
			var entries = new List<TreemapEntry>();
			var counters = new ScanCounters();
			CollectTreemapEntries(root, 0, options.MaxDepth, options.MaxTiles * 3, entries, counters, progress, cancellationToken);

			var selected = entries
				.Where(x => x.SizeBytes > 0)
				.OrderByDescending(x => x.SizeBytes)
				.Take(options.MaxTiles)
				.ToArray();

			return LayoutTreemap(selected, 1000d, 420d);
		}

		private void AnalyzeDirectory(
			DirectoryInfo directory,
			string rootPath,
			int depth,
			StorageAnalysisOptions options,
			List<LargeFileInfo> largeFiles,
			IDictionary<string, FolderAccumulator> folderStats,
			IDictionary<string, ExtensionAccumulator> extensionStats,
			StorageAnalysisCounters counters,
			ref ulong totalBytes,
			IProgress<FilesProScanProgress>? progress,
			CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (depth > options.MaxDepth || counters.WasTruncated || SkipDirectoryNames.Contains(directory.Name))
				return;

			counters.DirectoriesVisited++;
			AddDirectoryUsage(rootPath, directory.FullName, folderStats);

			if (counters.DirectoriesVisited % 20 == 0)
				progress?.Report(new("Storage accounting", directory.FullName, counters.DirectoriesVisited, counters.FilesVisited));

			try
			{
				foreach (var file in directory.EnumerateFiles())
				{
					cancellationToken.ThrowIfCancellationRequested();
					if (counters.FilesVisited >= options.MaxFilesToInspect)
					{
						counters.WasTruncated = true;
						return;
					}

					counters.FilesVisited++;
					var sizeBytes = (ulong)Math.Max(0L, file.Length);
					totalBytes += sizeBytes;
					AddFolderUsage(rootPath, file.FullName, sizeBytes, folderStats);
					AddExtensionUsage(file.FullName, sizeBytes, extensionStats);

					if (sizeBytes >= options.MinimumLargeFileSizeBytes)
					{
						largeFiles.Add(new()
						{
							Name = file.Name,
							Path = file.FullName,
							SizeBytes = sizeBytes,
							LastModified = new DateTimeOffset(file.LastWriteTime)
						});
						TrimLargeFiles(largeFiles, options.MaxLargeFiles * 4);
					}

					if (counters.FilesVisited % 512 == 0)
						progress?.Report(new("Storage accounting", file.FullName, counters.DirectoriesVisited, counters.FilesVisited));
				}

				foreach (var child in directory.EnumerateDirectories())
				{
					AnalyzeDirectory(
						child,
						rootPath,
						depth + 1,
						options,
						largeFiles,
						folderStats,
						extensionStats,
						counters,
						ref totalBytes,
						progress,
						cancellationToken);

					if (counters.WasTruncated)
						return;
				}
			}
			catch (Exception ex) when (IsSkippableScanException(ex))
			{
				logger.LogDebug(ex, "Files Pro storage accounting skipped {Path}", directory.FullName);
			}
		}

		private void ScanDirectory(
			DirectoryInfo directory,
			int depth,
			LargeFileScanOptions options,
			List<LargeFileInfo> files,
			ScanCounters counters,
			IProgress<FilesProScanProgress>? progress,
			CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (depth > options.MaxDepth || SkipDirectoryNames.Contains(directory.Name))
				return;

			counters.DirectoriesVisited++;
			if (counters.DirectoriesVisited % 25 == 0)
				progress?.Report(new("Large files", directory.FullName, counters.DirectoriesVisited, counters.FilesVisited));

			try
			{
				foreach (var file in directory.EnumerateFiles())
				{
					cancellationToken.ThrowIfCancellationRequested();
					counters.FilesVisited++;

					var length = (ulong)Math.Max(0L, file.Length);
					if (length >= options.MinimumSizeBytes)
					{
						files.Add(new()
						{
							Name = file.Name,
							Path = file.FullName,
							SizeBytes = length,
							LastModified = new DateTimeOffset(file.LastWriteTime)
						});
						TrimLargeFiles(files, options.MaxResults * 4);
					}
				}

				foreach (var child in directory.EnumerateDirectories())
					ScanDirectory(child, depth + 1, options, files, counters, progress, cancellationToken);
			}
			catch (Exception ex) when (IsSkippableScanException(ex))
			{
				logger.LogDebug(ex, "Files Pro large-file scan skipped {Path}", directory.FullName);
			}
		}

		private void CollectTreemapEntries(
			DirectoryInfo directory,
			int depth,
			int maxDepth,
			int maxEntries,
			List<TreemapEntry> entries,
			ScanCounters counters,
			IProgress<FilesProScanProgress>? progress,
			CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (depth > maxDepth || entries.Count >= maxEntries || SkipDirectoryNames.Contains(directory.Name))
				return;

			counters.DirectoriesVisited++;
			if (counters.DirectoriesVisited % 20 == 0)
				progress?.Report(new("Treemap", directory.FullName, counters.DirectoriesVisited, counters.FilesVisited));

			try
			{
				foreach (var file in directory.EnumerateFiles())
				{
					cancellationToken.ThrowIfCancellationRequested();
					counters.FilesVisited++;

					var length = (ulong)Math.Max(0L, file.Length);
					if (length > 0)
					{
						entries.Add(new(file.Name, file.FullName, "File", length, depth));
						if (entries.Count >= maxEntries)
							return;
					}
				}

				foreach (var child in directory.EnumerateDirectories())
				{
					cancellationToken.ThrowIfCancellationRequested();
					var size = EstimateDirectorySize(child, cancellationToken);
					if (size > 0)
					{
						entries.Add(new(child.Name, child.FullName, "Folder", size, depth));
						if (entries.Count >= maxEntries)
							return;
					}

					CollectTreemapEntries(child, depth + 1, maxDepth, maxEntries, entries, counters, progress, cancellationToken);
					if (entries.Count >= maxEntries)
						return;
				}
			}
			catch (Exception ex) when (IsSkippableScanException(ex))
			{
				logger.LogDebug(ex, "Files Pro treemap scan skipped {Path}", directory.FullName);
			}
		}

		private void CollectDuplicateCandidates(
			DirectoryInfo directory,
			int depth,
			DuplicateScanOptions options,
			IDictionary<long, List<FileInfo>> sizeGroups,
			ScanCounters counters,
			IProgress<FilesProScanProgress>? progress,
			CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (depth > options.MaxDepth || SkipDirectoryNames.Contains(directory.Name) || counters.FilesVisited >= options.MaxFiles)
				return;

			counters.DirectoriesVisited++;
			if (counters.DirectoriesVisited % 25 == 0)
				progress?.Report(new("Duplicate scan", directory.FullName, counters.DirectoriesVisited, counters.FilesVisited));

			try
			{
				foreach (var file in directory.EnumerateFiles())
				{
					cancellationToken.ThrowIfCancellationRequested();
					counters.FilesVisited++;

					if ((ulong)Math.Max(0L, file.Length) >= options.MinimumSizeBytes)
					{
						if (!sizeGroups.TryGetValue(file.Length, out var sizeGroup))
						{
							sizeGroup = [];
							sizeGroups[file.Length] = sizeGroup;
						}

						sizeGroup.Add(file);
						counters.CandidateFilesRetained++;
					}

					if (counters.FilesVisited >= options.MaxFiles)
						return;
				}

				foreach (var child in directory.EnumerateDirectories())
				{
					CollectDuplicateCandidates(child, depth + 1, options, sizeGroups, counters, progress, cancellationToken);
					if (counters.FilesVisited >= options.MaxFiles)
						return;
				}
			}
			catch (Exception ex) when (IsSkippableScanException(ex))
			{
				logger.LogDebug(ex, "Files Pro file collection skipped {Path}", directory.FullName);
			}
		}

		private void ScanWasteDirectory(
			DirectoryInfo directory,
			int depth,
			WastedSpaceScanOptions options,
			List<WastedSpaceSuggestion> suggestions,
			ScanCounters counters,
			IProgress<FilesProScanProgress>? progress,
			CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (depth > options.MaxDepth || suggestions.Count >= options.MaxResults)
				return;

			counters.DirectoriesVisited++;
			if (counters.DirectoriesVisited % 25 == 0)
				progress?.Report(new("Wasted space", directory.FullName, counters.DirectoriesVisited, counters.FilesVisited));

			try
			{
				var currentDirectoryIsWaste = WastedSpaceDirectoryNames.Contains(directory.Name);
				if (currentDirectoryIsWaste)
				{
					suggestions.Add(new()
					{
						Name = directory.Name,
						Path = directory.FullName,
						Category = "Generated/cache folder",
						SizeBytes = EstimateDirectorySize(directory, cancellationToken),
						SuggestedAction = "Review before cleanup",
						Reason = "Folder name usually indicates generated dependency, build, or cache output.",
						Risk = directory.Name.Equals(".git", StringComparison.OrdinalIgnoreCase) ? "Risky" : "Normal"
					});

					if (depth > 0)
						return;
				}

				var childDirectories = directory.EnumerateDirectories().ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
				foreach (var file in directory.EnumerateFiles())
				{
					cancellationToken.ThrowIfCancellationRequested();
					counters.FilesVisited++;

					var extension = file.Extension;
					var age = DateTimeOffset.Now - new DateTimeOffset(file.LastWriteTime);
					if (ArchiveExtensions.Contains(extension) && childDirectories.ContainsKey(Path.GetFileNameWithoutExtension(file.Name)))
					{
						suggestions.Add(new()
						{
							Name = file.Name,
							Path = file.FullName,
							Category = "Extracted archive pair",
							SizeBytes = (ulong)Math.Max(0L, file.Length),
							SuggestedAction = "Review archive and sibling folder",
							Reason = "An archive and same-named extracted folder exist side by side.",
							Risk = "Risky"
						});
					}
					else if (InstallerExtensions.Contains(extension) && age > TimeSpan.FromDays(30))
					{
						suggestions.Add(new()
						{
							Name = file.Name,
							Path = file.FullName,
							Category = "Old installer",
							SizeBytes = (ulong)Math.Max(0L, file.Length),
							SuggestedAction = "Move to installer archive",
							Reason = "Installer package is older than 30 days.",
							Risk = "Normal"
						});
					}
					else if (ArchiveExtensions.Contains(extension) && LooksLikeGameArchive(file.Name))
					{
						suggestions.Add(new()
						{
							Name = file.Name,
							Path = file.FullName,
							Category = "Game archive",
							SizeBytes = (ulong)Math.Max(0L, file.Length),
							SuggestedAction = "Move to game archive review",
							Reason = "Archive name matches common game, mod, asset, or texture patterns.",
							Risk = "Normal"
						});
					}

					if (suggestions.Count >= options.MaxResults)
						return;
				}

				foreach (var child in childDirectories.Values)
					ScanWasteDirectory(child, depth + 1, options, suggestions, counters, progress, cancellationToken);
			}
			catch (Exception ex) when (IsSkippableScanException(ex))
			{
				logger.LogDebug(ex, "Files Pro wasted-space scan skipped {Path}", directory.FullName);
			}
		}

		private StorageReportExportResult ExportReportCore(
			StorageReportSnapshot snapshot,
			StorageReportExportFormat format,
			string? outputFolder,
			CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var reportFolder = string.IsNullOrWhiteSpace(outputFolder)
				? Path.Combine(
					ApplicationData.Current.LocalFolder.Path,
					"FilesPro",
					"Reports",
					DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture))
				: outputFolder;

			Directory.CreateDirectory(reportFolder);
			var exportedFiles = 0;

			if (format is StorageReportExportFormat.Json)
			{
				var jsonPath = Path.Combine(reportFolder, "storage-report.json");
				var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
				{
					WriteIndented = true
				});

				cancellationToken.ThrowIfCancellationRequested();
				File.WriteAllText(jsonPath, json, Encoding.UTF8);
				exportedFiles++;
			}
			else
			{
				WriteLargeFilesCsv(Path.Combine(reportFolder, "large-files.csv"), snapshot.LargeFiles, cancellationToken);
				WriteDuplicateGroupsCsv(Path.Combine(reportFolder, "duplicates.csv"), snapshot.DuplicateGroups, cancellationToken);
				WriteWastedSpaceCsv(Path.Combine(reportFolder, "wasted-space.csv"), snapshot.WastedSpaceSuggestions, cancellationToken);
				exportedFiles += 3;
			}

			return new(reportFolder, format, exportedFiles, DateTimeOffset.Now);
		}

		private static IReadOnlyList<TreemapTileInfo> LayoutTreemap(
			IReadOnlyList<TreemapEntry> entries,
			double width,
			double height)
		{
			if (entries.Count == 0)
				return [];

			var total = entries.Aggregate(0UL, (current, entry) => current + entry.SizeBytes);
			if (total == 0)
				return [];

			var tiles = new List<TreemapTileInfo>(entries.Count);
			LayoutSlice(entries, 0, entries.Count, 0d, 0d, width, height, total, tiles);
			return tiles;
		}

		private static void LayoutSlice(
			IReadOnlyList<TreemapEntry> entries,
			int start,
			int end,
			double x,
			double y,
			double width,
			double height,
			ulong totalSize,
			List<TreemapTileInfo> tiles)
		{
			if (start >= end || width <= 1d || height <= 1d)
				return;

			if (end - start == 1)
			{
				var entry = entries[start];
				tiles.Add(new()
				{
					Name = entry.Name,
					Path = entry.Path,
					Kind = entry.Kind,
					SizeBytes = entry.SizeBytes,
					X = x,
					Y = y,
					Width = Math.Max(1d, width),
					Height = Math.Max(1d, height),
					Depth = entry.Depth
				});
				return;
			}

			var half = totalSize / 2UL;
			var running = 0UL;
			var split = start;
			for (; split < end - 1; split++)
			{
				var next = running + entries[split].SizeBytes;
				if (running > 0 && next > half)
					break;

				running = next;
			}

			if (running == 0)
				running = entries[start].SizeBytes;

			var firstSize = running;
			var secondSize = totalSize > firstSize ? totalSize - firstSize : 0UL;
			var ratio = totalSize == 0 ? 0.5d : Math.Clamp((double)firstSize / totalSize, 0.05d, 0.95d);

			if (width >= height)
			{
				var firstWidth = width * ratio;
				LayoutSlice(entries, start, split + 1, x, y, firstWidth, height, firstSize, tiles);
				LayoutSlice(entries, split + 1, end, x + firstWidth, y, width - firstWidth, height, secondSize, tiles);
			}
			else
			{
				var firstHeight = height * ratio;
				LayoutSlice(entries, start, split + 1, x, y, width, firstHeight, firstSize, tiles);
				LayoutSlice(entries, split + 1, end, x, y + firstHeight, width, height - firstHeight, secondSize, tiles);
			}
		}

		private static NtfsFastPathStatus GetNtfsFastPathStatus(string rootPath)
		{
			if (string.IsNullOrWhiteSpace(rootPath))
			{
				return new()
				{
					RootPath = rootPath,
					Reason = "No root path was supplied."
				};
			}

			try
			{
				var fullPath = Path.GetFullPath(rootPath);
				var root = Path.GetPathRoot(fullPath) ?? string.Empty;
				var drive = DriveInfo.GetDrives().FirstOrDefault(x => string.Equals(x.Name, root, StringComparison.OrdinalIgnoreCase));
				var isNtfs = drive is { IsReady: true } && string.Equals(drive.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase);
				var isLocalFixed = drive?.DriveType is System.IO.DriveType.Fixed;
				var isElevated = IsRunningElevated();
				var openReason = string.Empty;
				var canUseUsn = isNtfs &&
					isLocalFixed &&
					isElevated &&
					NtfsUsnScanner.CanOpenVolume(fullPath, out openReason);

				return new()
				{
					RootPath = fullPath,
					VolumeRoot = root,
					IsNtfs = isNtfs,
					IsLocalFixedDrive = isLocalFixed,
					IsElevated = isElevated,
					CanUseUsnJournal = canUseUsn,
					Mode = canUseUsn ? "Native NTFS USN scanner" : "Recursive fallback",
					Reason = canUseUsn
						? openReason
						: BuildNtfsFallbackReason(isNtfs, isLocalFixed, isElevated, openReason)
				};
			}
			catch (Exception ex) when (IsSkippableScanException(ex))
			{
				return new()
				{
					RootPath = rootPath,
					Reason = ex.Message
				};
			}
		}

		private static string BuildNtfsFallbackReason(bool isNtfs, bool isFixed, bool isElevated, string openReason)
		{
			if (!isNtfs)
				return "The target is not on an NTFS volume.";
			if (!isFixed)
				return "The target is not on a local fixed drive.";
			if (!isElevated)
				return "Native NTFS enumeration normally requires elevation; using recursive scanning.";
			if (!string.IsNullOrWhiteSpace(openReason))
				return openReason;

			return "USN fast path is unavailable; using recursive scanning.";
		}

		private static void WriteLargeFilesCsv(string path, IReadOnlyList<LargeFileInfo> files, CancellationToken cancellationToken)
		{
			var rows = new List<string>
			{
				CsvRow("name", "path", "size_bytes", "size", "last_modified")
			};

			foreach (var file in files)
			{
				cancellationToken.ThrowIfCancellationRequested();
				rows.Add(CsvRow(file.Name, file.Path, file.SizeBytes, file.SizeText, file.LastModified));
			}

			File.WriteAllLines(path, rows, Encoding.UTF8);
		}

		private static void WriteDuplicateGroupsCsv(string path, IReadOnlyList<DuplicateGroupInfo> groups, CancellationToken cancellationToken)
		{
			var rows = new List<string>
			{
				CsvRow("group", "name", "path", "size_bytes", "size", "wasted_bytes", "wasted", "last_modified", "partial_hash", "full_hash")
			};

			var groupNumber = 0;
			foreach (var group in groups)
			{
				groupNumber++;
				foreach (var item in group.Items)
				{
					cancellationToken.ThrowIfCancellationRequested();
					rows.Add(CsvRow(groupNumber, item.Name, item.Path, item.SizeBytes, item.SizeText, group.WastedBytes, group.WastedText, item.LastModified, item.PartialHash, item.FullHash));
				}
			}

			File.WriteAllLines(path, rows, Encoding.UTF8);
		}

		private static void WriteWastedSpaceCsv(string path, IReadOnlyList<WastedSpaceSuggestion> suggestions, CancellationToken cancellationToken)
		{
			var rows = new List<string>
			{
				CsvRow("name", "path", "category", "size_bytes", "size", "suggested_action", "risk", "reason")
			};

			foreach (var suggestion in suggestions)
			{
				cancellationToken.ThrowIfCancellationRequested();
				rows.Add(CsvRow(suggestion.Name, suggestion.Path, suggestion.Category, suggestion.SizeBytes, suggestion.SizeText, suggestion.SuggestedAction, suggestion.Risk, suggestion.Reason));
			}

			File.WriteAllLines(path, rows, Encoding.UTF8);
		}

		private static string CsvRow(params object?[] values)
			=> string.Join(",", values.Select(EscapeCsv));

		private static string EscapeCsv(object? value)
		{
			var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
			if (text.Contains('"', StringComparison.Ordinal))
				text = text.Replace("\"", "\"\"", StringComparison.Ordinal);

			return text.IndexOfAny([',', '"', '\r', '\n']) >= 0
				? $"\"{text}\""
				: text;
		}

		private static string ComputePartialHash(FileInfo file, int hashBytes, CancellationToken cancellationToken)
		{
			try
			{
				cancellationToken.ThrowIfCancellationRequested();

				var chunkSize = (int)Math.Min(Math.Max(4096, hashBytes), Math.Max(0L, file.Length));
				if (chunkSize == 0)
					return string.Empty;

				var first = new byte[chunkSize];
				var last = file.Length > chunkSize ? new byte[chunkSize] : [];

				using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 128 * 1024, FileOptions.SequentialScan);
				var firstRead = stream.Read(first, 0, first.Length);
				var lastRead = 0;
				if (last.Length > 0)
				{
					stream.Position = Math.Max(0L, file.Length - last.Length);
					lastRead = stream.Read(last, 0, last.Length);
				}

				var sizeBytes = BitConverter.GetBytes(file.Length);
				var combined = new byte[sizeBytes.Length + firstRead + lastRead];
				Buffer.BlockCopy(sizeBytes, 0, combined, 0, sizeBytes.Length);
				Buffer.BlockCopy(first, 0, combined, sizeBytes.Length, firstRead);
				if (lastRead > 0)
					Buffer.BlockCopy(last, 0, combined, sizeBytes.Length + firstRead, lastRead);

				return Convert.ToHexString(SHA256.HashData(combined));
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch
			{
				return string.Empty;
			}
		}

		private static string ComputeFullHash(FileInfo file, CancellationToken cancellationToken)
		{
			try
			{
				using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
				using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1024 * 1024, FileOptions.SequentialScan);
				var buffer = new byte[1024 * 1024];

				while (true)
				{
					cancellationToken.ThrowIfCancellationRequested();
					var read = stream.Read(buffer, 0, buffer.Length);
					if (read <= 0)
						break;

					hash.AppendData(buffer, 0, read);
				}

				return Convert.ToHexString(hash.GetHashAndReset());
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch
			{
				return string.Empty;
			}
		}

		private static void AddFolderUsage(
			string rootPath,
			string filePath,
			ulong sizeBytes,
			IDictionary<string, FolderAccumulator> folderStats)
		{
			var directory = Path.GetDirectoryName(filePath);
			while (!string.IsNullOrWhiteSpace(directory) && IsSameOrSubPath(rootPath, directory))
			{
				if (!folderStats.TryGetValue(directory, out var accumulator))
				{
					accumulator = new();
					folderStats[directory] = accumulator;
				}

				accumulator.SizeBytes += sizeBytes;
				accumulator.FileCount++;
				directory = Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
			}
		}

		private static void AddDirectoryUsage(
			string rootPath,
			string directoryPath,
			IDictionary<string, FolderAccumulator> folderStats)
		{
			var parent = Path.GetDirectoryName(directoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
			while (!string.IsNullOrWhiteSpace(parent) && IsSameOrSubPath(rootPath, parent))
			{
				if (!folderStats.TryGetValue(parent, out var accumulator))
				{
					accumulator = new();
					folderStats[parent] = accumulator;
				}

				accumulator.DirectoryCount++;
				parent = Path.GetDirectoryName(parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
			}
		}

		private static void AddExtensionUsage(
			string path,
			ulong sizeBytes,
			IDictionary<string, ExtensionAccumulator> extensionStats)
		{
			var extension = Path.GetExtension(path);
			if (string.IsNullOrWhiteSpace(extension))
				extension = "[none]";

			if (!extensionStats.TryGetValue(extension, out var accumulator))
			{
				accumulator = new();
				extensionStats[extension] = accumulator;
			}

			accumulator.SizeBytes += sizeBytes;
			accumulator.FileCount++;
		}

		private static bool IsSameOrSubPath(string rootPath, string candidatePath)
		{
			var root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
			var candidate = Path.GetFullPath(candidatePath);
			return candidate.Equals(rootPath, StringComparison.OrdinalIgnoreCase) ||
				candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase);
		}

		private static int GetRelativeDepth(string rootPath, string candidatePath)
		{
			var relativePath = Path.GetRelativePath(rootPath, candidatePath);
			if (string.IsNullOrWhiteSpace(relativePath) || relativePath == ".")
				return 0;

			return relativePath
				.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
				.Length;
		}

		private static void TrimLargeFiles(List<LargeFileInfo> files, int maxRetained)
		{
			if (files.Count <= maxRetained)
				return;

			var retained = files
				.OrderByDescending(x => x.SizeBytes)
				.Take(maxRetained)
				.ToArray();
			files.Clear();
			files.AddRange(retained);
		}

		private static ulong EstimateDirectorySize(DirectoryInfo directory, CancellationToken cancellationToken)
		{
			var total = 0UL;
			var countedFiles = 0;
			var pending = new Stack<DirectoryInfo>();
			pending.Push(directory);

			while (pending.Count > 0 && countedFiles < 10000)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var current = pending.Pop();
				try
				{
					foreach (var file in current.EnumerateFiles())
					{
						total += (ulong)Math.Max(0L, file.Length);
						countedFiles++;
						if (countedFiles >= 10000)
							break;
					}

					foreach (var child in current.EnumerateDirectories())
						pending.Push(child);
				}
				catch (Exception ex) when (IsSkippableScanException(ex))
				{
				}
			}

			return total;
		}

		private static bool IsSkippableScanException(Exception ex)
			=> (ex is UnauthorizedAccessException or IOException or SystemException) && ex is not OperationCanceledException;

		private static bool IsRunningElevated()
		{
			using var identity = WindowsIdentity.GetCurrent();
			var principal = new WindowsPrincipal(identity);
			return principal.IsInRole(WindowsBuiltInRole.Administrator);
		}

		private static bool LooksLikeGameArchive(string name)
			=> name.Contains("mod", StringComparison.OrdinalIgnoreCase) ||
				name.Contains("game", StringComparison.OrdinalIgnoreCase) ||
				name.Contains("asset", StringComparison.OrdinalIgnoreCase) ||
				name.Contains("texture", StringComparison.OrdinalIgnoreCase) ||
				name.Contains("minecraft", StringComparison.OrdinalIgnoreCase) ||
				name.Contains("roblox", StringComparison.OrdinalIgnoreCase);

		private class ScanCounters
		{
			public int DirectoriesVisited { get; set; }

			public int FilesVisited { get; set; }

			public int CandidateFilesRetained { get; set; }
		}

		private sealed class StorageAnalysisCounters : ScanCounters
		{
			public bool WasTruncated { get; set; }
		}

		private sealed class FolderAccumulator
		{
			public ulong SizeBytes { get; set; }

			public int FileCount { get; set; }

			public int DirectoryCount { get; set; }
		}

		private sealed class ExtensionAccumulator
		{
			public ulong SizeBytes { get; set; }

			public int FileCount { get; set; }
		}

		private sealed record TreemapEntry(
			string Name,
			string Path,
			string Kind,
			ulong SizeBytes,
			int Depth);
	}
}
