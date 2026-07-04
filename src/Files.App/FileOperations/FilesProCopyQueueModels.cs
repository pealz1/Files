// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.FileOperations
{
	public enum FilesProQueuedOperationKind
	{
		Copy,
		Move
	}

	public sealed record FilesProQueueOperationRequest(
		FilesProQueuedOperationKind Kind,
		string SourcePath,
		string DestinationPath,
		bool Overwrite = false,
		bool GenerateUniqueName = false);

	public sealed class FilesProQueueOperationResult
	{
		public FilesProQueuedOperationKind Kind { get; init; }

		public string SourcePath { get; init; } = string.Empty;

		public string DestinationPath { get; init; } = string.Empty;

		public bool Succeeded { get; init; }

		public string Message { get; init; } = string.Empty;

		public ulong BytesProcessed { get; init; }
	}

	public sealed class FilesProCopyQueueSnapshot
	{
		public bool IsRunning { get; init; }

		public int PendingCount { get; init; }

		public int CompletedCount { get; init; }

		public string CurrentPath { get; init; } = string.Empty;

		public ulong BytesProcessed { get; init; }

		public ulong TotalBytes { get; init; }

		public double PercentComplete => TotalBytes == 0 ? 0 : Math.Clamp((double)BytesProcessed / TotalBytes * 100d, 0d, 100d);
	}
}
