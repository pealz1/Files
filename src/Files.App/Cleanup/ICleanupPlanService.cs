// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Diagnostics;

namespace Files.App.Cleanup
{
	public interface ICleanupPlanService
	{
		Task<IReadOnlyList<CleanupSuggestion>> CreateDownloadsPlanAsync(
			DownloadsCleanupOptions options,
			IProgress<FilesProScanProgress>? progress,
			CancellationToken cancellationToken);

		Task<CleanupMovePlan> CreateDryRunMovePlanAsync(
			IReadOnlyList<CleanupSuggestion> suggestions,
			string destinationRoot,
			CancellationToken cancellationToken);

		Task<CleanupExecutionResult> ExecuteMovePlanAsync(
			CleanupMovePlan plan,
			CleanupExecutionOptions options,
			IProgress<FilesProScanProgress>? progress,
			CancellationToken cancellationToken);

		Task<CleanupExecutionResult> ExecuteDeletePlanAsync(
			CleanupDeletePlan plan,
			CleanupExecutionOptions options,
			IProgress<FilesProScanProgress>? progress,
			CancellationToken cancellationToken);
	}
}
