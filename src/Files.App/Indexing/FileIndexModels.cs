// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Indexing
{
	public sealed record FileIndexOptions(
		IReadOnlyList<string> IncludedRoots,
		IReadOnlyList<string>? ExcludedRoots = null,
		IReadOnlyList<string>? IgnoredFolderNames = null,
		bool IncludeTextContent = false,
		long MaxContentBytes = 256 * 1024,
		int MaxDepth = 12,
		int MaxFiles = 250_000,
		int CpuThrottleDelayMs = 0);

	public sealed record FileSearchQuery(
		string Text,
		int MaxResults = 250,
		IReadOnlyList<string>? PinnedRoots = null);

	public sealed class FileSearchResult
	{
		public string Name { get; init; } = string.Empty;

		public string Path { get; init; } = string.Empty;

		public string DirectoryPath { get; init; } = string.Empty;

		public string Extension { get; init; } = string.Empty;

		public ulong SizeBytes { get; init; }

		public string SizeText => SizeBytes.ToSizeString();

		public DateTimeOffset LastModified { get; init; }

		public string LastModifiedText => LastModified.LocalDateTime.ToString("g");

		public bool IsArchive { get; init; }

		public bool IsRoblox { get; init; }

		public bool IsRepository { get; init; }

		public bool IsDownloads { get; init; }

		public int Rank { get; set; }
	}

	public sealed class FileIndexStats
	{
		public int SchemaVersion { get; init; }

		public int IndexedItems { get; init; }

		public long IndexSizeBytes { get; init; }

		public string IndexSizeText => ((ulong)Math.Max(0, IndexSizeBytes)).ToSizeString();

		public DateTimeOffset? LastScanTime { get; init; }

		public string LastScanText => LastScanTime?.LocalDateTime.ToString("g") ?? "Never";

		public string DatabasePath { get; init; } = string.Empty;
	}

	internal sealed class ParsedFileSearch
	{
		public string Term { get; init; } = string.Empty;

		public string? Extension { get; init; }

		public bool? Repo { get; init; }

		public bool? Roblox { get; init; }

		public bool? Downloads { get; init; }

		public bool? Archive { get; init; }

		public bool? Duplicate { get; init; }

		public long? MinSizeBytes { get; init; }

		public long? MaxSizeBytes { get; init; }

		public DateTimeOffset? ModifiedAfter { get; init; }
	}
}
