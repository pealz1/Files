// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Diagnostics;

namespace Files.App.FileOperations
{
	public interface IFilesProCopyQueueService
	{
		Task<IReadOnlyList<FilesProQueueOperationResult>> EnqueueAsync(
			IReadOnlyList<FilesProQueueOperationRequest> requests,
			IProgress<FilesProScanProgress>? progress,
			CancellationToken cancellationToken);

		FilesProCopyQueueSnapshot GetSnapshot();
	}
}
