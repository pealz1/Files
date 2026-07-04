// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.StorageAnalysis
{
	public sealed record LargeFileScanOptions(
		string RootPath,
		ulong MinimumSizeBytes,
		int MaxDepth = 8,
		int MaxResults = 250);

	public sealed class LargeFileInfo
	{
		public string Name { get; init; } = string.Empty;

		public string Path { get; init; } = string.Empty;

		public ulong SizeBytes { get; init; }

		public string SizeText => SizeBytes.ToSizeString();

		public DateTimeOffset LastModified { get; init; }

		public string LastModifiedText => LastModified.LocalDateTime.ToString("g");
	}

	public sealed record DuplicateScanOptions(
		string RootPath,
		ulong MinimumSizeBytes = 4 * 1024 * 1024,
		int MaxDepth = 8,
		int MaxFiles = 15000,
		int MaxGroups = 80,
		int PartialHashBytes = 64 * 1024);

	public sealed class DuplicateFileInfo
	{
		public string Name { get; init; } = string.Empty;

		public string Path { get; init; } = string.Empty;

		public ulong SizeBytes { get; init; }

		public string SizeText => SizeBytes.ToSizeString();

		public DateTimeOffset LastModified { get; init; }

		public string LastModifiedText => LastModified.LocalDateTime.ToString("g");

		public string PartialHash { get; init; } = string.Empty;

		public string FullHash { get; init; } = string.Empty;
	}

	public sealed class DuplicateGroupInfo
	{
		public ulong SizeBytes { get; init; }

		public string SizeText => SizeBytes.ToSizeString();

		public ulong WastedBytes => Items.Count <= 1 ? 0 : SizeBytes * (ulong)(Items.Count - 1);

		public string WastedText => WastedBytes.ToSizeString();

		public int Count => Items.Count;

		public string VerificationLevel { get; init; } = "Full hash";

		public IReadOnlyList<DuplicateFileInfo> Items { get; init; } = [];

		public string PrimaryPath => Items.FirstOrDefault()?.Path ?? string.Empty;
	}

	public sealed record WastedSpaceScanOptions(
		string RootPath,
		int MaxDepth = 8,
		int MaxResults = 120);

	public sealed record TreemapScanOptions(
		string RootPath,
		int MaxDepth = 4,
		int MaxTiles = 120);

	public sealed record StorageAnalysisOptions(
		string RootPath,
		ulong MinimumLargeFileSizeBytes = 512UL * 1024UL * 1024UL,
		int MaxDepth = 32,
		int MaxLargeFiles = 250,
		int MaxFolders = 250,
		int MaxExtensions = 80,
		int MaxTreemapTiles = 120,
		int MaxFilesToInspect = int.MaxValue);

	public sealed class TreemapTileInfo
	{
		public string Name { get; init; } = string.Empty;

		public string Path { get; init; } = string.Empty;

		public string Kind { get; init; } = "File";

		public ulong SizeBytes { get; init; }

		public string SizeText => SizeBytes.ToSizeString();

		public double X { get; init; }

		public double Y { get; init; }

		public double Width { get; init; }

		public double Height { get; init; }

		public int Depth { get; init; }

		public string DisplayText => $"{Name}  {SizeText}";
	}

	public sealed class WastedSpaceSuggestion
	{
		public string Name { get; init; } = string.Empty;

		public string Path { get; init; } = string.Empty;

		public string Category { get; init; } = string.Empty;

		public ulong SizeBytes { get; init; }

		public string SizeText => SizeBytes.ToSizeString();

		public string SuggestedAction { get; init; } = "Review";

		public string Reason { get; init; } = string.Empty;

		public string Risk { get; init; } = "Normal";
	}

	public sealed class FolderSizeInfo
	{
		public string Name { get; init; } = string.Empty;

		public string Path { get; init; } = string.Empty;

		public ulong SizeBytes { get; init; }

		public string SizeText => SizeBytes.ToSizeString();

		public int FileCount { get; init; }

		public int DirectoryCount { get; init; }
	}

	public sealed class ExtensionSizeInfo
	{
		public string Extension { get; init; } = string.Empty;

		public ulong SizeBytes { get; init; }

		public string SizeText => SizeBytes.ToSizeString();

		public int FileCount { get; init; }
	}

	public sealed class StorageAnalysisSnapshot
	{
		public string RootPath { get; init; } = string.Empty;

		public string ScanMode { get; init; } = "Recursive fallback";

		public string Reason { get; init; } = string.Empty;

		public ulong TotalBytes { get; init; }

		public string TotalSizeText => TotalBytes.ToSizeString();

		public int FileCount { get; init; }

		public int DirectoryCount { get; init; }

		public bool WasTruncated { get; init; }

		public DateTimeOffset ScannedAt { get; init; } = DateTimeOffset.Now;

		public IReadOnlyList<LargeFileInfo> LargeFiles { get; init; } = [];

		public IReadOnlyList<FolderSizeInfo> LargestFolders { get; init; } = [];

		public IReadOnlyList<ExtensionSizeInfo> Extensions { get; init; } = [];

		public IReadOnlyList<TreemapTileInfo> TreemapTiles { get; init; } = [];

		public string SummaryText
			=> $"{TotalSizeText}, {FileCount:#,##0} files, {DirectoryCount:#,##0} folders via {ScanMode}";
	}

	public enum StorageReportExportFormat
	{
		Csv,
		Json
	}

	public sealed record StorageReportSnapshot(
		string RootPath,
		DateTimeOffset GeneratedAt,
		IReadOnlyList<LargeFileInfo> LargeFiles,
		IReadOnlyList<DuplicateGroupInfo> DuplicateGroups,
		IReadOnlyList<WastedSpaceSuggestion> WastedSpaceSuggestions);

	public sealed record StorageReportExportResult(
		string OutputPath,
		StorageReportExportFormat Format,
		int FileCount,
		DateTimeOffset ExportedAt);

	public sealed class NtfsFastPathStatus
	{
		public string RootPath { get; init; } = string.Empty;

		public string VolumeRoot { get; init; } = string.Empty;

		public bool IsNtfs { get; init; }

		public bool IsElevated { get; init; }

		public bool IsLocalFixedDrive { get; init; }

		public bool CanUseUsnJournal { get; init; }

		public string Mode { get; init; } = "Recursive fallback";

		public string Reason { get; init; } = string.Empty;
	}
}
