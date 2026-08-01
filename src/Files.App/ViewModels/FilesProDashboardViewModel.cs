// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Cleanup;
using Files.App.CommandPalette;
using Files.App.Diagnostics;
using Files.App.Indexing;
using Files.App.ProjectDiscovery;
using Files.App.StorageAnalysis;
using Microsoft.Extensions.Logging;
using System.IO;
using Windows.ApplicationModel.DataTransfer;

namespace Files.App.ViewModels
{
	public sealed partial class FilesProDashboardViewModel : ObservableObject, IDisposable
	{
		private readonly IProjectDiscoveryService projectDiscoveryService;
		private readonly ICleanupPlanService cleanupPlanService;
		private readonly IStorageScanService storageScanService;
		private readonly IFileIndexService fileIndexService;
		private readonly IEverythingSearchService everythingSearchService;
		private readonly ICommandPaletteService commandPaletteService;
		private readonly ILogger<FilesProDashboardViewModel> logger;

		private CancellationTokenSource? workCancellation;

		private string scanRootsText = string.Empty;
		public string ScanRootsText
		{
			get => scanRootsText;
			set => SetProperty(ref scanRootsText, value);
		}

		private string downloadsPath = string.Empty;
		public string DownloadsPath
		{
			get => downloadsPath;
			set => SetProperty(ref downloadsPath, value);
		}

		private string largeFilesRoot = string.Empty;
		public string LargeFilesRoot
		{
			get => largeFilesRoot;
			set => SetProperty(ref largeFilesRoot, value);
		}

		private double minimumLargeFileSizeMb = 512d;
		public double MinimumLargeFileSizeMb
		{
			get => minimumLargeFileSizeMb;
			set => SetProperty(ref minimumLargeFileSizeMb, Math.Max(1d, value));
		}

		private string searchText = "roblox:true ext:luau";
		public string SearchText
		{
			get => searchText;
			set => SetProperty(ref searchText, value);
		}

		private bool includeTextContent;
		public bool IncludeTextContent
		{
			get => includeTextContent;
			set => SetProperty(ref includeTextContent, value);
		}

		private bool allowCleanupExecution;
		public bool AllowCleanupExecution
		{
			get => allowCleanupExecution;
			set
			{
				if (SetProperty(ref allowCleanupExecution, value))
					ExecuteCleanupMovePlanCommand.NotifyCanExecuteChanged();
			}
		}

		private bool confirmCleanupDelete;
		public bool ConfirmCleanupDelete
		{
			get => confirmCleanupDelete;
			set
			{
				if (SetProperty(ref confirmCleanupDelete, value))
					ExecuteCleanupRecycleSelectedCommand.NotifyCanExecuteChanged();
			}
		}

		private string commandPaletteQueryText = string.Empty;
		public string CommandPaletteQueryText
		{
			get => commandPaletteQueryText;
			set => SetProperty(ref commandPaletteQueryText, value);
		}

		private string shellCommandText = string.Empty;
		public string ShellCommandText
		{
			get => shellCommandText;
			set => SetProperty(ref shellCommandText, value);
		}

		private bool confirmShellCommandExecution;
		public bool ConfirmShellCommandExecution
		{
			get => confirmShellCommandExecution;
			set => SetProperty(ref confirmShellCommandExecution, value);
		}

		private CommandPaletteItem? selectedCommand;
		public CommandPaletteItem? SelectedCommand
		{
			get => selectedCommand;
			set
			{
				if (SetProperty(ref selectedCommand, value))
					ExecuteSelectedCommandCommand.NotifyCanExecuteChanged();
			}
		}

		private CleanupMovePlanItem? selectedCleanupMovePlanItem;
		public CleanupMovePlanItem? SelectedCleanupMovePlanItem
		{
			get => selectedCleanupMovePlanItem;
			set
			{
				if (SetProperty(ref selectedCleanupMovePlanItem, value))
					ExecuteCleanupRecycleSelectedCommand.NotifyCanExecuteChanged();
			}
		}

		private bool isScanning;
		public bool IsScanning
		{
			get => isScanning;
			set
			{
				if (SetProperty(ref isScanning, value))
					NotifyBusyCommands();
			}
		}

		private bool isIndexing;
		public bool IsIndexing
		{
			get => isIndexing;
			set
			{
				if (SetProperty(ref isIndexing, value))
					NotifyBusyCommands();
			}
		}

		private bool hasScanned;
		public bool HasScanned
		{
			get => hasScanned;
			set => SetProperty(ref hasScanned, value);
		}

		private string progressText = "Ready to scan";
		public string ProgressText
		{
			get => progressText;
			set => SetProperty(ref progressText, value);
		}

		private string summaryText = "Select Scan now to discover projects, Roblox files, cleanup opportunities, and storage usage.";
		public string SummaryText
		{
			get => summaryText;
			set => SetProperty(ref summaryText, value);
		}

		private string indexStatsText = "Build the search index to find files by name, type, or content.";
		public string IndexStatsText
		{
			get => indexStatsText;
			set => SetProperty(ref indexStatsText, value);
		}

		private string integrationStatusText = "Performance support has not been checked.";
		public string IntegrationStatusText
		{
			get => integrationStatusText;
			set => SetProperty(ref integrationStatusText, value);
		}

		private string storageSummaryText = "Select Scan now to analyze storage usage.";
		public string StorageSummaryText
		{
			get => storageSummaryText;
			set => SetProperty(ref storageSummaryText, value);
		}

		private string lastStorageReportPath = string.Empty;
		public string LastStorageReportPath
		{
			get => lastStorageReportPath;
			set => SetProperty(ref lastStorageReportPath, value);
		}

		public ObservableCollection<DiscoveredProject> Projects { get; } = [];

		public ObservableCollection<RobloxFileInfo> RobloxFiles { get; } = [];

		public ObservableCollection<CleanupSuggestion> CleanupSuggestions { get; } = [];

		public ObservableCollection<CleanupMovePlanItem> CleanupMovePlanItems { get; } = [];

		public ObservableCollection<LargeFileInfo> LargeFiles { get; } = [];

		public ObservableCollection<FolderSizeInfo> FolderSizes { get; } = [];

		public ObservableCollection<ExtensionSizeInfo> ExtensionSizes { get; } = [];

		public ObservableCollection<DuplicateGroupInfo> DuplicateGroups { get; } = [];

		public ObservableCollection<WastedSpaceSuggestion> WastedSpaceSuggestions { get; } = [];

		public ObservableCollection<TreemapTileInfo> TreemapTiles { get; } = [];

		public ObservableCollection<FileSearchResult> SearchResults { get; } = [];

		public ObservableCollection<CommandPaletteItem> CommandPaletteItems { get; } = [];

		public IAsyncRelayCommand StartScanCommand { get; }

		public IAsyncRelayCommand RebuildIndexCommand { get; }

		public IAsyncRelayCommand SearchIndexCommand { get; }

		public IAsyncRelayCommand SearchCommandsCommand { get; }

		public IAsyncRelayCommand<string?> ExportStorageReportCommand { get; }

		public IAsyncRelayCommand ExecuteCleanupMovePlanCommand { get; }

		public IAsyncRelayCommand ExecuteCleanupRecycleSelectedCommand { get; }

		public IAsyncRelayCommand RefreshFastPathStatusCommand { get; }

		public IAsyncRelayCommand ExecuteSelectedCommandCommand { get; }

		public IRelayCommand CancelScanCommand { get; }

		public IAsyncRelayCommand<string?> OpenPathCommand { get; }

		public IRelayCommand<string?> CopyPathCommand { get; }

		public FilesProDashboardViewModel(
			IProjectDiscoveryService projectDiscoveryService,
			ICleanupPlanService cleanupPlanService,
			IStorageScanService storageScanService,
			IFileIndexService fileIndexService,
			IEverythingSearchService everythingSearchService,
			ICommandPaletteService commandPaletteService,
			ILogger<FilesProDashboardViewModel> logger)
		{
			this.projectDiscoveryService = projectDiscoveryService;
			this.cleanupPlanService = cleanupPlanService;
			this.storageScanService = storageScanService;
			this.fileIndexService = fileIndexService;
			this.everythingSearchService = everythingSearchService;
			this.commandPaletteService = commandPaletteService;
			this.logger = logger;

			var roots = GetDefaultRoots();
			ScanRootsText = string.Join(Environment.NewLine, roots);
			DownloadsPath = Constants.UserEnvironmentPaths.DownloadsPath;
			LargeFilesRoot = Directory.Exists(DownloadsPath)
				? DownloadsPath
				: Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

			StartScanCommand = new AsyncRelayCommand(() => StartScanAsync(), () => !IsBusy);
			RebuildIndexCommand = new AsyncRelayCommand(() => RebuildIndexAsync(), () => !IsBusy);
			SearchIndexCommand = new AsyncRelayCommand(() => SearchIndexAsync(), () => !IsBusy);
			SearchCommandsCommand = new AsyncRelayCommand(() => SearchCommandsAsync(), () => !IsBusy);
			ExportStorageReportCommand = new AsyncRelayCommand<string?>(ExportStorageReportAsync, _ => !IsBusy);
			ExecuteCleanupMovePlanCommand = new AsyncRelayCommand(ExecuteCleanupMovePlanAsync, () => !IsBusy && AllowCleanupExecution && CleanupMovePlanItems.Count > 0);
			ExecuteCleanupRecycleSelectedCommand = new AsyncRelayCommand(ExecuteCleanupRecycleSelectedAsync, () => !IsBusy && ConfirmCleanupDelete && SelectedCleanupMovePlanItem is not null);
			RefreshFastPathStatusCommand = new AsyncRelayCommand(() => RefreshFastPathStatusAsync(), () => !IsBusy);
			ExecuteSelectedCommandCommand = new AsyncRelayCommand(ExecuteSelectedCommandAsync, () => !IsBusy && SelectedCommand is not null);
			CancelScanCommand = new RelayCommand(CancelWork, () => IsBusy);
			OpenPathCommand = new AsyncRelayCommand<string?>(OpenPathAsync);
			CopyPathCommand = new RelayCommand<string?>(CopyPath);

			LoadCommandPaletteDefaults();
		}

		public bool IsBusy => IsScanning || IsIndexing;

		private async Task StartScanAsync()
		{
			if (IsBusy)
				return;

			workCancellation?.Dispose();
			workCancellation = new CancellationTokenSource();
			var cancellationToken = workCancellation.Token;
			var progress = new Progress<FilesProScanProgress>(value => ProgressText = value.DisplayText);

			IsScanning = true;
			HasScanned = true;
			ProgressText = "Starting scan";
			SummaryText = "Scanning selected roots.";

			ClearScanCollections();

			try
			{
				var roots = ParseRoots(ScanRootsText);
				var projectResult = await projectDiscoveryService.ScanAsync(
					new ProjectScanOptions(roots),
					progress,
					cancellationToken);

				foreach (var project in projectResult.Projects)
					Projects.Add(project);

				foreach (var robloxFile in projectResult.RobloxFiles)
					RobloxFiles.Add(robloxFile);

				var cleanupSuggestions = await cleanupPlanService.CreateDownloadsPlanAsync(
					new DownloadsCleanupOptions(DownloadsPath),
					progress,
					cancellationToken);

				foreach (var suggestion in cleanupSuggestions)
					CleanupSuggestions.Add(suggestion);

				var dryRunPlan = await cleanupPlanService.CreateDryRunMovePlanAsync(
					cleanupSuggestions,
					GetCleanupReviewRoot(),
					cancellationToken);

				foreach (var movePlanItem in dryRunPlan.Items)
					CleanupMovePlanItems.Add(movePlanItem);

				var minimumBytes = (ulong)(MinimumLargeFileSizeMb * 1024d * 1024d);
				var storageSnapshot = await storageScanService.AnalyzeStorageAsync(
					new StorageAnalysisOptions(LargeFilesRoot, minimumBytes),
					progress,
					cancellationToken);

				StorageSummaryText = storageSnapshot.WasTruncated
					? $"{storageSnapshot.SummaryText} (truncated by safety limit)"
					: storageSnapshot.SummaryText;

				foreach (var largeFile in storageSnapshot.LargeFiles)
					LargeFiles.Add(largeFile);

				foreach (var folder in storageSnapshot.LargestFolders)
					FolderSizes.Add(folder);

				foreach (var extension in storageSnapshot.Extensions)
					ExtensionSizes.Add(extension);

				foreach (var tile in storageSnapshot.TreemapTiles)
					TreemapTiles.Add(tile);

				var duplicateGroups = await storageScanService.FindDuplicatesAsync(
					new DuplicateScanOptions(LargeFilesRoot),
					progress,
					cancellationToken);

				foreach (var duplicateGroup in duplicateGroups)
					DuplicateGroups.Add(duplicateGroup);

				var wastedSpace = await storageScanService.FindWastedSpaceAsync(
					new WastedSpaceScanOptions(LargeFilesRoot),
					progress,
					cancellationToken);

				foreach (var suggestion in wastedSpace)
					WastedSpaceSuggestions.Add(suggestion);

				await RefreshFastPathStatusAsync(cancellationToken);
				await SearchCommandsAsync(cancellationToken);

				ProgressText = "Scan complete";
				SummaryText = $"{Projects.Count:#,##0} projects, {RobloxFiles.Count:#,##0} Roblox/Lua files, {CleanupSuggestions.Count:#,##0} cleanup suggestions, {CleanupMovePlanItems.Count:#,##0} dry-run moves, {LargeFiles.Count:#,##0} large files, {DuplicateGroups.Count:#,##0} duplicate groups.";
			}
			catch (OperationCanceledException)
			{
				ProgressText = "Scan canceled";
				SummaryText = "The scan was canceled before it finished.";
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Files Pro scan failed");
				ProgressText = "Scan failed";
				SummaryText = ex.Message;
			}
			finally
			{
				IsScanning = false;
			}
		}

		private async Task RebuildIndexAsync()
		{
			if (IsBusy)
				return;

			workCancellation?.Dispose();
			workCancellation = new CancellationTokenSource();
			var cancellationToken = workCancellation.Token;
			var progress = new Progress<FilesProScanProgress>(value => ProgressText = value.DisplayText);

			IsIndexing = true;
			ProgressText = "Rebuilding index";

			try
			{
				var roots = ParseRoots(ScanRootsText);
				var stats = await fileIndexService.RebuildAsync(
					new FileIndexOptions(
						roots,
						IncludeTextContent: IncludeTextContent,
						CpuThrottleDelayMs: 1),
					progress,
					cancellationToken);

				UpdateIndexStats(stats);
				ProgressText = $"Index complete: {stats.IndexedItems:#,##0} items";
			}
			catch (OperationCanceledException)
			{
				ProgressText = "Index canceled";
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Files Pro index rebuild failed");
				ProgressText = "Index failed";
				IndexStatsText = ex.Message;
			}
			finally
			{
				IsIndexing = false;
			}
		}

		private Task SearchIndexAsync()
			=> SearchIndexAsync(CancellationToken.None);

		private async Task SearchIndexAsync(CancellationToken cancellationToken)
		{
			SearchResults.Clear();
			var stats = await fileIndexService.GetStatsAsync(cancellationToken);
			UpdateIndexStats(stats);

			var results = await fileIndexService.SearchAsync(
				new FileSearchQuery(SearchText, PinnedRoots: ParseRoots(ScanRootsText)),
				cancellationToken);

			foreach (var result in results)
				SearchResults.Add(result);

			var everythingStatus = await everythingSearchService.GetStatusAsync(cancellationToken);
			if (everythingStatus.IsAvailable)
			{
				var everythingResults = await everythingSearchService.SearchAsync(
					new EverythingSearchQuery(SearchText, 50),
					cancellationToken);

				foreach (var result in everythingResults.Where(x => SearchResults.All(existing => !string.Equals(existing.Path, x.Path, StringComparison.OrdinalIgnoreCase))))
				{
					var fileInfo = new FileInfo(result.Path);
					SearchResults.Add(new()
					{
						Name = fileInfo.Name,
						Path = fileInfo.FullName,
						DirectoryPath = fileInfo.DirectoryName ?? string.Empty,
						Extension = fileInfo.Extension,
						SizeBytes = File.Exists(fileInfo.FullName) ? (ulong)Math.Max(0L, fileInfo.Length) : 0UL,
						LastModified = File.Exists(fileInfo.FullName) ? new DateTimeOffset(fileInfo.LastWriteTime) : DateTimeOffset.MinValue,
						Rank = 50
					});
				}
			}

			ProgressText = $"Index search returned {SearchResults.Count:#,##0} results";
		}

		private Task SearchCommandsAsync()
			=> SearchCommandsAsync(CancellationToken.None);

		private async Task SearchCommandsAsync(CancellationToken cancellationToken)
		{
			CommandPaletteItems.Clear();
			var commands = await commandPaletteService.SearchAsync(
				new CommandPaletteQuery(CommandPaletteQueryText),
				cancellationToken);

			foreach (var command in commands)
				CommandPaletteItems.Add(command);
		}

		private void CancelWork()
		{
			workCancellation?.Cancel();
		}

		private void NotifyBusyCommands()
		{
			StartScanCommand.NotifyCanExecuteChanged();
			RebuildIndexCommand.NotifyCanExecuteChanged();
			SearchIndexCommand.NotifyCanExecuteChanged();
			SearchCommandsCommand.NotifyCanExecuteChanged();
			ExportStorageReportCommand.NotifyCanExecuteChanged();
			ExecuteCleanupMovePlanCommand.NotifyCanExecuteChanged();
			ExecuteCleanupRecycleSelectedCommand.NotifyCanExecuteChanged();
			RefreshFastPathStatusCommand.NotifyCanExecuteChanged();
			ExecuteSelectedCommandCommand.NotifyCanExecuteChanged();
			CancelScanCommand.NotifyCanExecuteChanged();
			OnPropertyChanged(nameof(IsBusy));
		}

		private void ClearScanCollections()
		{
			Projects.Clear();
			RobloxFiles.Clear();
			CleanupSuggestions.Clear();
			CleanupMovePlanItems.Clear();
			SelectedCleanupMovePlanItem = null;
			ConfirmCleanupDelete = false;
			LargeFiles.Clear();
			FolderSizes.Clear();
			ExtensionSizes.Clear();
			DuplicateGroups.Clear();
			WastedSpaceSuggestions.Clear();
			TreemapTiles.Clear();
			StorageSummaryText = "Select Scan now to analyze storage usage.";
		}

		private void LoadCommandPaletteDefaults()
		{
			CommandPaletteItems.Clear();
			foreach (var command in commandPaletteService.GetCommandsAsync(CancellationToken.None).GetAwaiter().GetResult())
				CommandPaletteItems.Add(command);
		}

		private void UpdateIndexStats(FileIndexStats stats)
		{
			IndexStatsText = $"{stats.IndexedItems:#,##0} indexed items, {stats.IndexSizeText}, last scan {stats.LastScanText}";
		}

		private async Task ExportStorageReportAsync(string? formatText)
		{
			if (IsBusy)
				return;

			var format = string.Equals(formatText, "Json", StringComparison.OrdinalIgnoreCase)
				? StorageReportExportFormat.Json
				: StorageReportExportFormat.Csv;

			try
			{
				var result = await storageScanService.ExportReportAsync(
					new StorageReportSnapshot(
						LargeFilesRoot,
						DateTimeOffset.Now,
						LargeFiles.ToArray(),
						DuplicateGroups.ToArray(),
						WastedSpaceSuggestions.ToArray()),
					format,
					outputFolder: null,
					CancellationToken.None);

				LastStorageReportPath = result.OutputPath;
				ProgressText = $"Exported {result.Format} storage report to {result.OutputPath}";
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Files Pro storage report export failed");
				ProgressText = "Storage report export failed";
				SummaryText = ex.Message;
			}
		}

		private async Task ExecuteCleanupMovePlanAsync()
		{
			if (IsBusy || !AllowCleanupExecution)
				return;

			workCancellation?.Dispose();
			workCancellation = new CancellationTokenSource();
			var cancellationToken = workCancellation.Token;
			var progress = new Progress<FilesProScanProgress>(value => ProgressText = value.DisplayText);

			IsScanning = true;
			try
			{
				var result = await cleanupPlanService.ExecuteMovePlanAsync(
					new CleanupMovePlan { Items = CleanupMovePlanItems.ToArray() },
					new CleanupExecutionOptions(
						AllowMoves: true,
						AllowDeletes: false,
						UseRecycleBin: true,
						ConfirmationText: "MOVE"),
					progress,
					cancellationToken);

				ProgressText = $"Cleanup apply finished: {result.Summary}";
				SummaryText = $"Cleanup operation log: {result.LogPath}";
			}
			catch (OperationCanceledException)
			{
				ProgressText = "Cleanup apply canceled";
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Files Pro cleanup apply failed");
				ProgressText = "Cleanup apply failed";
				SummaryText = ex.Message;
			}
			finally
			{
				IsScanning = false;
				AllowCleanupExecution = false;
			}
		}

		private async Task ExecuteCleanupRecycleSelectedAsync()
		{
			if (IsBusy || !ConfirmCleanupDelete || SelectedCleanupMovePlanItem is null)
				return;

			workCancellation?.Dispose();
			workCancellation = new CancellationTokenSource();
			var cancellationToken = workCancellation.Token;
			var progress = new Progress<FilesProScanProgress>(value => ProgressText = value.DisplayText);

			IsScanning = true;
			try
			{
				var result = await cleanupPlanService.ExecuteDeletePlanAsync(
					new CleanupDeletePlan
					{
						Items =
						[
							new()
							{
								SourcePath = SelectedCleanupMovePlanItem.SourcePath,
								Reason = SelectedCleanupMovePlanItem.Reason,
								Risk = SelectedCleanupMovePlanItem.Risk
							}
						]
					},
					new CleanupExecutionOptions(
						AllowMoves: false,
						AllowDeletes: true,
						UseRecycleBin: true,
						ConfirmationText: "DELETE"),
					progress,
					cancellationToken);

				ProgressText = $"Cleanup recycle finished: {result.Summary}";
				SummaryText = $"Cleanup operation log: {result.LogPath}";
			}
			catch (OperationCanceledException)
			{
				ProgressText = "Cleanup recycle canceled";
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Files Pro cleanup recycle failed");
				ProgressText = "Cleanup recycle failed";
				SummaryText = ex.Message;
			}
			finally
			{
				IsScanning = false;
				ConfirmCleanupDelete = false;
			}
		}

		private Task RefreshFastPathStatusAsync()
			=> RefreshFastPathStatusAsync(CancellationToken.None);

		private async Task RefreshFastPathStatusAsync(CancellationToken cancellationToken)
		{
			var ntfsStatus = await storageScanService.GetNtfsFastPathStatusAsync(LargeFilesRoot, cancellationToken);
			var everythingStatus = await everythingSearchService.GetStatusAsync(cancellationToken);
			IntegrationStatusText = $"Storage: {ntfsStatus.Mode}. {ntfsStatus.Reason} Search: {everythingStatus.Mode}. {everythingStatus.Reason}";
		}

		private async Task ExecuteSelectedCommandAsync()
		{
			if (SelectedCommand is null)
				return;

			try
			{
				var result = await commandPaletteService.ExecuteAsync(
					new CommandPaletteExecutionRequest(
						SelectedCommand.Id,
						LargeFilesRoot,
						ShellCommandText,
						Confirmed: !SelectedCommand.RequiresConfirmation || ConfirmShellCommandExecution),
					CancellationToken.None);

				ProgressText = result.Message;
				if (!string.IsNullOrWhiteSpace(result.TargetView))
					SummaryText = $"Command target: {result.TargetView}";
				if (SelectedCommand.RequiresConfirmation)
					ConfirmShellCommandExecution = false;
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Files Pro command execution failed");
				ProgressText = "Command execution failed";
				SummaryText = ex.Message;
			}
		}

		private string GetCleanupReviewRoot()
		{
			var root = Directory.Exists(DownloadsPath)
				? DownloadsPath
				: Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

			return Path.Combine(root, "Files Pro Review");
		}

		private static IReadOnlyList<string> ParseRoots(string rootsText)
		{
			return rootsText
				.Split(['\r', '\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
				.Where(Directory.Exists)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToArray();
		}

		private static IReadOnlyList<string> GetDefaultRoots()
		{
			var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
			var systemDrive = Path.GetPathRoot(userProfile) ?? @"C:\";
			var candidates = new[]
			{
				Path.Combine(systemDrive, "dev"),
				Constants.UserEnvironmentPaths.DesktopPath,
				Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
				Constants.UserEnvironmentPaths.DownloadsPath,
				Path.Combine(userProfile, "source"),
				Path.Combine(userProfile, "repos")
			};

			return candidates
				.Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToArray();
		}

		private static Task OpenPathAsync(string? path)
		{
			if (string.IsNullOrWhiteSpace(path))
				return Task.CompletedTask;

			var pathToOpen = Directory.Exists(path)
				? path
				: File.Exists(path)
					? Path.GetDirectoryName(path)
					: null;

			return string.IsNullOrWhiteSpace(pathToOpen)
				? Task.CompletedTask
				: NavigationHelpers.OpenPathInNewTab(pathToOpen, true);
		}

		private static void CopyPath(string? path)
		{
			if (string.IsNullOrWhiteSpace(path))
				return;

			var dataPackage = new DataPackage();
			dataPackage.SetText(path);
			Clipboard.SetContent(dataPackage);
			Clipboard.Flush();
		}

		public void Dispose()
		{
			workCancellation?.Cancel();
			workCancellation?.Dispose();
			workCancellation = null;
		}
	}
}
