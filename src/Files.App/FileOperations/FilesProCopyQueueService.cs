// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Diagnostics;
using Microsoft.Extensions.Logging;
using System.IO;

namespace Files.App.FileOperations
{
	internal sealed class FilesProCopyQueueService : IFilesProCopyQueueService
	{
		private readonly ILogger<FilesProCopyQueueService> logger;
		private readonly SemaphoreSlim queueLock = new(1, 1);

		private int completedCount;
		private int pendingCount;
		private string currentPath = string.Empty;
		private ulong bytesProcessed;
		private ulong totalBytes;

		public FilesProCopyQueueService(ILogger<FilesProCopyQueueService> logger)
		{
			this.logger = logger;
		}

		public async Task<IReadOnlyList<FilesProQueueOperationResult>> EnqueueAsync(
			IReadOnlyList<FilesProQueueOperationRequest> requests,
			IProgress<FilesProScanProgress>? progress,
			CancellationToken cancellationToken)
		{
			await queueLock.WaitAsync(cancellationToken);
			try
			{
				pendingCount = requests.Count;
				completedCount = 0;
				bytesProcessed = 0;
				totalBytes = requests.Aggregate(0UL, (total, request) => total + GetRequestSize(request.SourcePath));
				var results = new List<FilesProQueueOperationResult>(requests.Count);

				foreach (var request in requests)
				{
					cancellationToken.ThrowIfCancellationRequested();
					currentPath = request.SourcePath;
					ReportProgress(progress);

					var result = await ExecuteAsync(request, progress, cancellationToken);
					results.Add(result);
					completedCount++;
					pendingCount--;
					ReportProgress(progress);
				}

				return results;
			}
			finally
			{
				currentPath = string.Empty;
				pendingCount = 0;
				totalBytes = 0;
				queueLock.Release();
			}
		}

		public FilesProCopyQueueSnapshot GetSnapshot()
			=> new()
			{
				IsRunning = queueLock.CurrentCount == 0,
				PendingCount = pendingCount,
				CompletedCount = completedCount,
				CurrentPath = currentPath,
				BytesProcessed = bytesProcessed,
				TotalBytes = totalBytes
			};

		private async Task<FilesProQueueOperationResult> ExecuteAsync(
			FilesProQueueOperationRequest request,
			IProgress<FilesProScanProgress>? progress,
			CancellationToken cancellationToken)
		{
			try
			{
				var destinationPath = request.GenerateUniqueName
					? GetUniqueDestinationPath(request.DestinationPath)
					: request.DestinationPath;

				if (Directory.Exists(request.SourcePath))
				{
					if (IsSameOrSubPath(request.SourcePath, destinationPath))
						return Failure(request, "Destination cannot be inside the source folder.");

					var directoryBytes = await CopyDirectoryAsync(request.SourcePath, destinationPath, request.Overwrite, bytes => AddProcessedBytes(bytes, progress), cancellationToken);
					if (request.Kind is FilesProQueuedOperationKind.Move)
						Directory.Delete(request.SourcePath, recursive: true);

					return new()
					{
						Kind = request.Kind,
						SourcePath = request.SourcePath,
						DestinationPath = destinationPath,
						Succeeded = true,
						Message = request.Kind is FilesProQueuedOperationKind.Move ? "Moved folder." : "Copied folder.",
						BytesProcessed = directoryBytes
					};
				}

				if (!File.Exists(request.SourcePath))
					return Failure(request, "Source file does not exist.");

				var destinationDirectory = Path.GetDirectoryName(destinationPath);
				if (string.IsNullOrWhiteSpace(destinationDirectory))
					return Failure(request, "Destination path is invalid.");

				Directory.CreateDirectory(destinationDirectory);
				if (File.Exists(destinationPath) && !request.Overwrite)
					return Failure(request, "Destination exists and overwrite is false.");

				var bytes = await CopyFileAsync(request.SourcePath, destinationPath, request.Overwrite, bytes => AddProcessedBytes(bytes, progress), cancellationToken);
				if (request.Kind is FilesProQueuedOperationKind.Move)
					File.Delete(request.SourcePath);

				return new()
				{
					Kind = request.Kind,
					SourcePath = request.SourcePath,
					DestinationPath = destinationPath,
					Succeeded = true,
					Message = request.Kind is FilesProQueuedOperationKind.Move ? "Moved." : "Copied.",
					BytesProcessed = bytes
				};
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex)
			{
				logger.LogWarning(ex, "Files Pro queued file operation failed from {Source} to {Destination}", request.SourcePath, request.DestinationPath);
				return Failure(request, ex.Message);
			}
		}

		private static async Task<ulong> CopyFileAsync(
			string sourcePath,
			string destinationPath,
			bool overwrite,
			Action<ulong> onBytesProcessed,
			CancellationToken cancellationToken)
		{
			await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
			await using var destination = new FileStream(destinationPath, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
			var buffer = new byte[1024 * 1024];
			var total = 0UL;

			while (true)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
				if (read <= 0)
					break;

				await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
				total += (ulong)read;
				onBytesProcessed((ulong)read);
			}

			return total;
		}

		private static string GetUniqueDestinationPath(string destinationPath)
		{
			if (!File.Exists(destinationPath) && !Directory.Exists(destinationPath))
				return destinationPath;

			var directory = Path.GetDirectoryName(destinationPath) ?? string.Empty;
			var fileName = Path.GetFileNameWithoutExtension(destinationPath);
			var extension = Path.GetExtension(destinationPath);

			for (var index = 1; index < 10000; index++)
			{
				var candidate = Path.Combine(directory, $"{fileName} ({index}){extension}");
				if (!File.Exists(candidate) && !Directory.Exists(candidate))
					return candidate;
			}

			throw new IOException("Could not create a unique destination path.");
		}

		private static async Task<ulong> CopyDirectoryAsync(
			string sourcePath,
			string destinationPath,
			bool overwrite,
			Action<ulong> onBytesProcessed,
			CancellationToken cancellationToken)
		{
			var sourceDirectory = new DirectoryInfo(sourcePath);
			if (sourceDirectory.Attributes.HasFlag(FileAttributes.ReparsePoint))
				throw new IOException("Reparse-point folders are not copied by the Files Pro queue.");

			if (File.Exists(destinationPath))
				throw new IOException("Destination is an existing file.");

			if (Directory.Exists(destinationPath) && !overwrite)
				throw new IOException("Destination folder exists and overwrite is false.");

			Directory.CreateDirectory(destinationPath);
			var total = 0UL;

			foreach (var file in sourceDirectory.EnumerateFiles())
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (file.Attributes.HasFlag(FileAttributes.ReparsePoint))
					throw new IOException("Reparse-point files are not copied by the Files Pro queue.");

				total += await CopyFileAsync(file.FullName, Path.Combine(destinationPath, file.Name), overwrite, onBytesProcessed, cancellationToken);
			}

			foreach (var directory in sourceDirectory.EnumerateDirectories())
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
					throw new IOException("Reparse-point folders are not copied by the Files Pro queue.");

				total += await CopyDirectoryAsync(directory.FullName, Path.Combine(destinationPath, directory.Name), overwrite, onBytesProcessed, cancellationToken);
			}

			return total;
		}

		private void AddProcessedBytes(ulong byteCount, IProgress<FilesProScanProgress>? progress)
		{
			bytesProcessed += byteCount;
			ReportProgress(progress);
		}

		private void ReportProgress(IProgress<FilesProScanProgress>? progress)
			=> progress?.Report(new("Copy queue", currentPath, completedCount, pendingCount, bytesProcessed, totalBytes));

		private static ulong GetRequestSize(string sourcePath)
		{
			try
			{
				if (File.Exists(sourcePath))
					return (ulong)Math.Max(0L, new FileInfo(sourcePath).Length);
				if (Directory.Exists(sourcePath))
					return GetDirectorySize(new DirectoryInfo(sourcePath));
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SystemException)
			{
				return 0;
			}

			return 0;
		}

		private static ulong GetDirectorySize(DirectoryInfo directory)
		{
			if (directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
				return 0;

			var total = 0UL;
			foreach (var file in directory.EnumerateFiles())
			{
				if (!file.Attributes.HasFlag(FileAttributes.ReparsePoint))
					total += (ulong)Math.Max(0L, file.Length);
			}

			foreach (var child in directory.EnumerateDirectories())
				total += GetDirectorySize(child);

			return total;
		}

		private static bool IsSameOrSubPath(string parentPath, string candidatePath)
		{
			var parent = Path.GetFullPath(parentPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
			var candidate = Path.GetFullPath(candidatePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

			return candidate.StartsWith(parent, StringComparison.OrdinalIgnoreCase);
		}

		private static FilesProQueueOperationResult Failure(FilesProQueueOperationRequest request, string message)
			=> new()
			{
				Kind = request.Kind,
				SourcePath = request.SourcePath,
				DestinationPath = request.DestinationPath,
				Succeeded = false,
				Message = message
			};
	}
}
