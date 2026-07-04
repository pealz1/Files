// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Diagnostics;

namespace Files.App.StorageAnalysis
{
	public interface IStorageScanService
	{
		Task<IReadOnlyList<LargeFileInfo>> ScanLargeFilesAsync(
			LargeFileScanOptions options,
			IProgress<FilesProScanProgress>? progress,
			CancellationToken cancellationToken);

		Task<IReadOnlyList<DuplicateGroupInfo>> FindDuplicatesAsync(
			DuplicateScanOptions options,
			IProgress<FilesProScanProgress>? progress,
			CancellationToken cancellationToken);

		Task<IReadOnlyList<WastedSpaceSuggestion>> FindWastedSpaceAsync(
			WastedSpaceScanOptions options,
			IProgress<FilesProScanProgress>? progress,
			CancellationToken cancellationToken);

		Task<IReadOnlyList<TreemapTileInfo>> BuildTreemapAsync(
			TreemapScanOptions options,
			IProgress<FilesProScanProgress>? progress,
			CancellationToken cancellationToken);

		Task<StorageAnalysisSnapshot> AnalyzeStorageAsync(
			StorageAnalysisOptions options,
			IProgress<FilesProScanProgress>? progress,
			CancellationToken cancellationToken);

		Task<NtfsFastPathStatus> GetNtfsFastPathStatusAsync(
			string rootPath,
			CancellationToken cancellationToken);

		Task<StorageReportExportResult> ExportReportAsync(
			StorageReportSnapshot snapshot,
			StorageReportExportFormat format,
			string? outputFolder,
			CancellationToken cancellationToken);
	}
}
