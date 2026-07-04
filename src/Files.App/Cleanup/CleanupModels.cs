// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Cleanup
{
	public sealed record DownloadsCleanupOptions(
		string DownloadsPath,
		int RecentDays = 7,
		int MaxItems = 500);

	public sealed class CleanupSuggestion
	{
		public string Name { get; init; } = string.Empty;

		public string Path { get; init; } = string.Empty;

		public string Category { get; init; } = "Unknown";

		public string SuggestedAction { get; init; } = "Review manually";

		public string Reason { get; init; } = string.Empty;

		public string Risk { get; init; } = "Normal";

		public DateTimeOffset LastModified { get; init; }

		public string LastModifiedText => LastModified.LocalDateTime.ToString("g");
	}

	public sealed class CleanupMovePlan
	{
		public IReadOnlyList<CleanupMovePlanItem> Items { get; init; } = [];

		public int MoveCount => Items.Count;

		public string Summary => $"{MoveCount:#,##0} dry-run move targets";
	}

	public sealed class CleanupMovePlanItem
	{
		public string SourcePath { get; init; } = string.Empty;

		public string DestinationPath { get; init; } = string.Empty;

		public string Action { get; init; } = "Move";

		public string Reason { get; init; } = string.Empty;

		public string Risk { get; init; } = "Normal";
	}

	public sealed class CleanupDeletePlan
	{
		public IReadOnlyList<CleanupDeletePlanItem> Items { get; init; } = [];

		public int DeleteCount => Items.Count;

		public string Summary => $"{DeleteCount:#,##0} recycle-bin targets";
	}

	public sealed class CleanupDeletePlanItem
	{
		public string SourcePath { get; init; } = string.Empty;

		public string Reason { get; init; } = string.Empty;

		public string Risk { get; init; } = "Risky";
	}

	public sealed record CleanupExecutionOptions(
		bool AllowMoves,
		bool AllowDeletes = false,
		bool UseRecycleBin = true,
		string ConfirmationText = "");

	public sealed class CleanupExecutionResult
	{
		public IReadOnlyList<CleanupExecutionItemResult> Items { get; init; } = [];

		public int SucceededCount => Items.Count(x => x.Succeeded);

		public int FailedCount => Items.Count(x => !x.Succeeded);

		public string LogPath { get; init; } = string.Empty;

		public string Summary => $"{SucceededCount:#,##0} applied, {FailedCount:#,##0} skipped or failed";
	}

	public sealed class CleanupExecutionItemResult
	{
		public string SourcePath { get; init; } = string.Empty;

		public string DestinationPath { get; init; } = string.Empty;

		public bool Succeeded { get; init; }

		public string Message { get; init; } = string.Empty;
	}
}
