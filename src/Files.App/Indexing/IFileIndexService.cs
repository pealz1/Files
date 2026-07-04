// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Diagnostics;

namespace Files.App.Indexing
{
	public interface IFileIndexService
	{
		Task<FileIndexStats> GetStatsAsync(CancellationToken cancellationToken);

		Task<FileIndexStats> RebuildAsync(
			FileIndexOptions options,
			IProgress<FilesProScanProgress>? progress,
			CancellationToken cancellationToken);

		Task<IReadOnlyList<FileSearchResult>> SearchAsync(
			FileSearchQuery query,
			CancellationToken cancellationToken);
	}
}
