// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Diagnostics;
using Microsoft.Win32.SafeHandles;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Files.App.StorageAnalysis
{
	internal static class NtfsUsnScanner
	{
		private const uint GenericRead = 0x80000000;
		private const uint FileShareReadWriteDelete = 0x00000001 | 0x00000002 | 0x00000004;
		private const uint OpenExisting = 3;
		private const uint FsctlEnumUsnData = 0x000900b3;
		private const uint FsctlGetNtfsFileRecord = 0x00090068;
		private const int OutputBufferLength = 1024 * 1024;
		private const int FileRecordOutputBufferLength = 128 * 1024;

		public static bool CanOpenVolume(string rootPath, out string reason)
		{
			reason = string.Empty;

			var volumeRoot = Path.GetPathRoot(Path.GetFullPath(rootPath));
			if (string.IsNullOrWhiteSpace(volumeRoot))
			{
				reason = "Root path has no volume root.";
				return false;
			}

			try
			{
				using var handle = OpenVolume(volumeRoot);
				if (handle.IsInvalid)
				{
					reason = $"Could not open {volumeRoot} for USN enumeration: Win32 {Marshal.GetLastWin32Error()}.";
					return false;
				}

				reason = "NTFS volume can be opened for USN enumeration.";
				return true;
			}
			catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or SystemException)
			{
				reason = ex.Message;
				return false;
			}
		}

		public static bool TryScanLargeFiles(
			LargeFileScanOptions options,
			IProgress<FilesProScanProgress>? progress,
			CancellationToken cancellationToken,
			out IReadOnlyList<LargeFileInfo> files,
			out string reason)
		{
			files = [];
			reason = string.Empty;

			try
			{
				var rootPath = Path.GetFullPath(options.RootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
				var volumeRoot = Path.GetPathRoot(rootPath);
				if (string.IsNullOrWhiteSpace(volumeRoot))
				{
					reason = "Root path has no volume root.";
					return false;
				}

				using var handle = OpenVolume(volumeRoot);
				if (handle.IsInvalid)
				{
					reason = $"Could not open {volumeRoot} for USN enumeration: Win32 {Marshal.GetLastWin32Error()}.";
					return false;
				}

				var entries = EnumerateEntries(handle, cancellationToken, progress);
				if (entries.Count == 0)
				{
					reason = "USN enumeration returned no records.";
					return false;
				}

				var pathCache = new Dictionary<ulong, string>(capacity: Math.Min(entries.Count, 65536));
				var results = new List<LargeFileInfo>();
				var inspectedFiles = 0;

				foreach (var entry in entries.Values.Where(x => !x.IsDirectory))
				{
					cancellationToken.ThrowIfCancellationRequested();
					inspectedFiles++;

					var path = BuildPath(entry.FileReferenceNumber, entries, pathCache, volumeRoot);
					if (string.IsNullOrWhiteSpace(path) ||
						!IsSameOrSubPath(rootPath, path) ||
						!IsWithinDepth(rootPath, path, options.MaxDepth))
					{
						continue;
					}

					FileInfo file;
					try
					{
						file = new(path);
						if (!file.Exists)
							continue;
					}
					catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or SystemException)
					{
						continue;
					}

					var size = (ulong)Math.Max(0L, file.Length);
					if (size >= options.MinimumSizeBytes)
					{
						results.Add(new()
						{
							Name = file.Name,
							Path = file.FullName,
							SizeBytes = size,
							LastModified = new DateTimeOffset(file.LastWriteTime)
						});
					}

					if (inspectedFiles % 512 == 0)
						progress?.Report(new("USN large-file scan", path, entries.Count, inspectedFiles));
				}

				files = results
					.OrderByDescending(x => x.SizeBytes)
					.Take(options.MaxResults)
					.ToArray();
				reason = $"USN enumeration scanned {entries.Count:#,##0} MFT records.";
				return true;
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SystemException)
			{
				reason = ex.Message;
				return false;
			}
		}

		public static bool TryBuildStorageSnapshot(
			StorageAnalysisOptions options,
			IProgress<FilesProScanProgress>? progress,
			CancellationToken cancellationToken,
			out StorageAnalysisSnapshot snapshot,
			out string reason)
		{
			snapshot = new() { RootPath = options.RootPath };
			reason = string.Empty;

			try
			{
				var rootPath = Path.GetFullPath(options.RootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
				var volumeRoot = Path.GetPathRoot(rootPath);
				if (string.IsNullOrWhiteSpace(volumeRoot))
				{
					reason = "Root path has no volume root.";
					return false;
				}

				using var handle = OpenVolume(volumeRoot);
				if (handle.IsInvalid)
				{
					reason = $"Could not open {volumeRoot} for USN/MFT enumeration: Win32 {Marshal.GetLastWin32Error()}.";
					return false;
				}

				var entries = EnumerateEntries(handle, cancellationToken, progress);
				if (entries.Count == 0)
				{
					reason = "USN enumeration returned no records.";
					return false;
				}

				var pathCache = new Dictionary<ulong, string>(capacity: Math.Min(entries.Count, 65536));
				var largeFiles = new List<LargeFileInfo>();
				var folderStats = new Dictionary<string, FolderAccumulator>(StringComparer.OrdinalIgnoreCase);
				var extensionStats = new Dictionary<string, ExtensionAccumulator>(StringComparer.OrdinalIgnoreCase);
				var totalBytes = 0UL;
				var inspectedFiles = 0;
				var directoryCount = 0;
				var wasTruncated = false;

				foreach (var directory in entries.Values.Where(x => x.IsDirectory))
				{
					cancellationToken.ThrowIfCancellationRequested();
					var path = BuildPath(directory.FileReferenceNumber, entries, pathCache, volumeRoot);
					if (!string.IsNullOrWhiteSpace(path) && IsSameOrSubPath(rootPath, path) && IsWithinDepth(rootPath, path, options.MaxDepth))
					{
						directoryCount++;
						AddDirectoryUsage(rootPath, path, folderStats);
					}
				}

				foreach (var entry in entries.Values.Where(x => !x.IsDirectory))
				{
					cancellationToken.ThrowIfCancellationRequested();
					if (inspectedFiles >= options.MaxFilesToInspect)
					{
						wasTruncated = true;
						break;
					}

					var path = BuildPath(entry.FileReferenceNumber, entries, pathCache, volumeRoot);
					if (string.IsNullOrWhiteSpace(path) ||
						!IsSameOrSubPath(rootPath, path) ||
						!IsWithinDepth(rootPath, path, options.MaxDepth))
					{
						continue;
					}

					inspectedFiles++;
					NtfsFileMetadata? metadata = TryReadNtfsFileMetadata(handle, entry.FileReferenceNumber, out var ntfsMetadata)
						? ntfsMetadata
						: TryReadFileInfoMetadata(path);

					if (metadata is not { } fileMetadata)
						continue;

					var size = fileMetadata.SizeBytes;
					totalBytes += size;
					AddFolderUsage(rootPath, path, size, folderStats);
					AddExtensionUsage(path, size, extensionStats);

					if (size >= options.MinimumLargeFileSizeBytes)
					{
						largeFiles.Add(new()
						{
							Name = Path.GetFileName(path),
							Path = path,
							SizeBytes = size,
							LastModified = fileMetadata.LastModified
						});

						TrimLargeFiles(largeFiles, options.MaxLargeFiles * 4);
					}

					if (inspectedFiles % 512 == 0)
						progress?.Report(new("USN/MFT storage accounting", path, entries.Count, inspectedFiles));
				}

				var largestFolders = folderStats
					.Select(pair => new FolderSizeInfo
					{
						Name = Path.GetFileName(pair.Key.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
						Path = pair.Key,
						SizeBytes = pair.Value.SizeBytes,
						FileCount = pair.Value.FileCount,
						DirectoryCount = pair.Value.DirectoryCount
					})
					.OrderByDescending(x => x.SizeBytes)
					.Take(options.MaxFolders)
					.ToArray();

				var extensions = extensionStats
					.Select(pair => new ExtensionSizeInfo
					{
						Extension = pair.Key,
						SizeBytes = pair.Value.SizeBytes,
						FileCount = pair.Value.FileCount
					})
					.OrderByDescending(x => x.SizeBytes)
					.Take(options.MaxExtensions)
					.ToArray();

				var selectedTreemapEntries = largestFolders
					.Where(x => x.SizeBytes > 0)
					.Take(options.MaxTreemapTiles)
					.Select(x => new TreemapEntry(
						string.IsNullOrWhiteSpace(x.Name) ? x.Path : x.Name,
						x.Path,
						"Folder",
						x.SizeBytes,
						GetRelativeDepth(rootPath, x.Path)))
					.ToArray();

				snapshot = new()
				{
					RootPath = rootPath,
					ScanMode = "Native NTFS USN/MFT scanner",
					Reason = $"USN enumerated {entries.Count:#,##0} records; MFT metadata inspected {inspectedFiles:#,##0} files.",
					TotalBytes = totalBytes,
					FileCount = inspectedFiles,
					DirectoryCount = directoryCount,
					WasTruncated = wasTruncated,
					LargeFiles = largeFiles
						.OrderByDescending(x => x.SizeBytes)
						.Take(options.MaxLargeFiles)
						.ToArray(),
					LargestFolders = largestFolders,
					Extensions = extensions,
					TreemapTiles = LayoutTreemap(selectedTreemapEntries, 1000d, 420d)
				};

				reason = snapshot.Reason;
				return true;
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SystemException)
			{
				reason = ex.Message;
				return false;
			}
		}

		private static Dictionary<ulong, UsnEntry> EnumerateEntries(
			SafeFileHandle handle,
			CancellationToken cancellationToken,
			IProgress<FilesProScanProgress>? progress)
		{
			var entries = new Dictionary<ulong, UsnEntry>();
			var output = new byte[OutputBufferLength];
			var input = new MftEnumData
			{
				StartFileReferenceNumber = 0,
				LowUsn = 0,
				HighUsn = long.MaxValue
			};

			while (true)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (!DeviceIoControl(handle, FsctlEnumUsnData, ref input, Marshal.SizeOf<MftEnumData>(), output, output.Length, out var bytesReturned, IntPtr.Zero))
				{
					var error = Marshal.GetLastWin32Error();
					if (entries.Count > 0)
						break;

					throw new IOException($"FSCTL_ENUM_USN_DATA failed: Win32 {error}.");
				}

				if (bytesReturned <= sizeof(ulong))
					break;

				var nextReferenceNumber = BitConverter.ToUInt64(output, 0);
				var offset = sizeof(ulong);
				while (offset + 60 <= bytesReturned)
				{
					var recordLength = BitConverter.ToUInt32(output, offset);
					if (recordLength == 0 || offset + recordLength > bytesReturned)
						break;

					if (TryReadUsnEntry(output, offset, bytesReturned, out var entry))
					{
						entries[entry.FileReferenceNumber] = entry;
					}

					offset += (int)recordLength;
				}

				progress?.Report(new("USN enumeration", string.Empty, entries.Count, 0));
				if (nextReferenceNumber <= input.StartFileReferenceNumber)
					break;

				input.StartFileReferenceNumber = nextReferenceNumber;
			}

			return entries;
		}

		private static bool TryReadUsnEntry(
			byte[] buffer,
			int offset,
			int bytesReturned,
			out UsnEntry entry)
		{
			entry = default!;

			if (offset + 8 > bytesReturned)
				return false;

			var majorVersion = BitConverter.ToUInt16(buffer, offset + 4);
			ulong fileReference;
			ulong parentReference;
			FileAttributes attributes;
			ushort nameLength;
			ushort nameOffset;

			switch (majorVersion)
			{
				case 2:
					if (offset + 60 > bytesReturned)
						return false;

					fileReference = BitConverter.ToUInt64(buffer, offset + 8);
					parentReference = BitConverter.ToUInt64(buffer, offset + 16);
					attributes = (FileAttributes)BitConverter.ToUInt32(buffer, offset + 52);
					nameLength = BitConverter.ToUInt16(buffer, offset + 56);
					nameOffset = BitConverter.ToUInt16(buffer, offset + 58);
					break;

				case 3:
					if (offset + 76 > bytesReturned)
						return false;

					// NTFS file-record APIs still accept the low 64 bits. ReFS/native
					// 128-bit ids fall back cleanly when record metadata cannot be read.
					fileReference = BitConverter.ToUInt64(buffer, offset + 8);
					parentReference = BitConverter.ToUInt64(buffer, offset + 24);
					attributes = (FileAttributes)BitConverter.ToUInt32(buffer, offset + 68);
					nameLength = BitConverter.ToUInt16(buffer, offset + 72);
					nameOffset = BitConverter.ToUInt16(buffer, offset + 74);
					break;

				default:
					return false;
			}

			if (nameLength == 0 || offset + nameOffset + nameLength > bytesReturned)
				return false;

			var name = Encoding.Unicode.GetString(buffer, offset + nameOffset, nameLength);
			if (string.IsNullOrWhiteSpace(name))
				return false;

			entry = new(
				fileReference,
				parentReference,
				name,
				attributes.HasFlag(FileAttributes.Directory));
			return true;
		}

		private static bool TryReadNtfsFileMetadata(
			SafeFileHandle handle,
			ulong fileReferenceNumber,
			out NtfsFileMetadata metadata)
		{
			metadata = default;
			var input = BitConverter.GetBytes(fileReferenceNumber);
			var output = new byte[FileRecordOutputBufferLength];

			if (!DeviceIoControl(handle, FsctlGetNtfsFileRecord, input, input.Length, output, output.Length, out var bytesReturned, IntPtr.Zero) ||
				bytesReturned < 16)
			{
				return false;
			}

			var recordLength = BitConverter.ToUInt32(output, 8);
			var recordOffset = 12;
			if (recordLength == 0 || recordOffset + recordLength > bytesReturned || recordOffset + 64 > output.Length)
				return false;

			if (output[recordOffset] != (byte)'F' || output[recordOffset + 1] != (byte)'I' || output[recordOffset + 2] != (byte)'L' || output[recordOffset + 3] != (byte)'E')
				return false;

			var firstAttributeOffset = BitConverter.ToUInt16(output, recordOffset + 20);
			var offset = recordOffset + firstAttributeOffset;
			var end = recordOffset + (int)recordLength;
			ulong? sizeBytes = null;
			DateTimeOffset? lastModified = null;

			while (offset + 16 <= end)
			{
				var attributeType = BitConverter.ToUInt32(output, offset);
				if (attributeType == 0xffffffff)
					break;

				var attributeLength = BitConverter.ToUInt32(output, offset + 4);
				if (attributeLength == 0 || offset + attributeLength > end)
					break;

				var nonResident = output[offset + 8] != 0;
				var nameLength = output[offset + 9];

				if (attributeType == 0x10 && !nonResident)
				{
					var valueLength = BitConverter.ToUInt32(output, offset + 16);
					var valueOffset = BitConverter.ToUInt16(output, offset + 20);
					var valueStart = offset + valueOffset;
					if (valueLength >= 16 && valueStart + 16 <= end)
					{
						var writeTime = BitConverter.ToInt64(output, valueStart + 8);
						if (writeTime > 0)
							lastModified = DateTimeOffset.FromFileTime(writeTime);
					}
				}
				else if (attributeType == 0x80 && nameLength == 0)
				{
					if (nonResident)
					{
						if (offset + 56 <= end)
							sizeBytes = (ulong)Math.Max(0L, BitConverter.ToInt64(output, offset + 48));
					}
					else
					{
						if (offset + 24 <= end)
							sizeBytes = BitConverter.ToUInt32(output, offset + 16);
					}
				}

				offset += (int)attributeLength;
			}

			if (sizeBytes is null)
				return false;

			metadata = new(sizeBytes.Value, lastModified ?? DateTimeOffset.MinValue);
			return true;
		}

		private static string BuildPath(
			ulong fileReference,
			IReadOnlyDictionary<ulong, UsnEntry> entries,
			IDictionary<ulong, string> pathCache,
			string volumeRoot)
		{
			if (pathCache.TryGetValue(fileReference, out var cached))
				return cached;

			if (!entries.TryGetValue(fileReference, out var entry))
				return string.Empty;

			var names = new Stack<string>();
			var visited = new HashSet<ulong>();
			var current = entry;

			while (visited.Add(current.FileReferenceNumber))
			{
				names.Push(current.Name);
				if (current.ParentFileReferenceNumber == current.FileReferenceNumber ||
					!entries.TryGetValue(current.ParentFileReferenceNumber, out current!))
				{
					break;
				}
			}

			var path = volumeRoot;
			foreach (var name in names)
			{
				path = Path.Combine(path, name);
			}

			pathCache[fileReference] = path;
			return path;
		}

		private static bool IsSameOrSubPath(string rootPath, string candidatePath)
		{
			var root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
			var candidate = Path.GetFullPath(candidatePath);
			return candidate.Equals(rootPath, StringComparison.OrdinalIgnoreCase) ||
				candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase);
		}

		private static bool IsWithinDepth(string rootPath, string candidatePath, int maxDepth)
		{
			if (maxDepth <= 0)
				return true;

			var relativePath = Path.GetRelativePath(rootPath, candidatePath);
			if (relativePath.StartsWith("..", StringComparison.Ordinal))
				return false;

			var depth = relativePath
				.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
				.Length;
			return depth <= maxDepth;
		}

		private static int GetRelativeDepth(string rootPath, string candidatePath)
		{
			var relativePath = Path.GetRelativePath(rootPath, candidatePath);
			if (string.IsNullOrWhiteSpace(relativePath) || relativePath == ".")
				return 0;

			return relativePath
				.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
				.Length;
		}

		private static NtfsFileMetadata? TryReadFileInfoMetadata(string path)
		{
			try
			{
				var file = new FileInfo(path);
				if (!file.Exists)
					return null;

				return new((ulong)Math.Max(0L, file.Length), new DateTimeOffset(file.LastWriteTime));
			}
			catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or SystemException)
			{
				return null;
			}
		}

		private static void AddFolderUsage(
			string rootPath,
			string filePath,
			ulong sizeBytes,
			IDictionary<string, FolderAccumulator> folderStats)
		{
			var directory = Path.GetDirectoryName(filePath);
			while (!string.IsNullOrWhiteSpace(directory) && IsSameOrSubPath(rootPath, directory))
			{
				if (!folderStats.TryGetValue(directory, out var accumulator))
				{
					accumulator = new();
					folderStats[directory] = accumulator;
				}

				accumulator.SizeBytes += sizeBytes;
				accumulator.FileCount++;
				directory = Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
			}
		}

		private static void AddDirectoryUsage(
			string rootPath,
			string directoryPath,
			IDictionary<string, FolderAccumulator> folderStats)
		{
			var parent = Path.GetDirectoryName(directoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
			while (!string.IsNullOrWhiteSpace(parent) && IsSameOrSubPath(rootPath, parent))
			{
				if (!folderStats.TryGetValue(parent, out var accumulator))
				{
					accumulator = new();
					folderStats[parent] = accumulator;
				}

				accumulator.DirectoryCount++;
				parent = Path.GetDirectoryName(parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
			}
		}

		private static void AddExtensionUsage(
			string path,
			ulong sizeBytes,
			IDictionary<string, ExtensionAccumulator> extensionStats)
		{
			var extension = Path.GetExtension(path);
			if (string.IsNullOrWhiteSpace(extension))
				extension = "[none]";

			if (!extensionStats.TryGetValue(extension, out var accumulator))
			{
				accumulator = new();
				extensionStats[extension] = accumulator;
			}

			accumulator.SizeBytes += sizeBytes;
			accumulator.FileCount++;
		}

		private static void TrimLargeFiles(List<LargeFileInfo> files, int maxRetained)
		{
			if (files.Count <= maxRetained)
				return;

			var retained = files
				.OrderByDescending(x => x.SizeBytes)
				.Take(maxRetained)
				.ToArray();
			files.Clear();
			files.AddRange(retained);
		}

		private static IReadOnlyList<TreemapTileInfo> LayoutTreemap(
			IReadOnlyList<TreemapEntry> entries,
			double width,
			double height)
		{
			if (entries.Count == 0)
				return [];

			var total = entries.Aggregate(0UL, (current, entry) => current + entry.SizeBytes);
			if (total == 0)
				return [];

			var tiles = new List<TreemapTileInfo>(entries.Count);
			LayoutSlice(entries, 0, entries.Count, 0d, 0d, width, height, total, tiles);
			return tiles;
		}

		private static void LayoutSlice(
			IReadOnlyList<TreemapEntry> entries,
			int start,
			int end,
			double x,
			double y,
			double width,
			double height,
			ulong totalSize,
			List<TreemapTileInfo> tiles)
		{
			if (start >= end || width <= 1d || height <= 1d)
				return;

			if (end - start == 1)
			{
				var entry = entries[start];
				tiles.Add(new()
				{
					Name = entry.Name,
					Path = entry.Path,
					Kind = entry.Kind,
					SizeBytes = entry.SizeBytes,
					X = x,
					Y = y,
					Width = Math.Max(1d, width),
					Height = Math.Max(1d, height),
					Depth = entry.Depth
				});
				return;
			}

			var half = totalSize / 2UL;
			var running = 0UL;
			var split = start;
			for (; split < end - 1; split++)
			{
				var next = running + entries[split].SizeBytes;
				if (running > 0 && next > half)
					break;

				running = next;
			}

			if (running == 0)
				running = entries[start].SizeBytes;

			var firstSize = running;
			var secondSize = totalSize > firstSize ? totalSize - firstSize : 0UL;
			var ratio = totalSize == 0 ? 0.5d : Math.Clamp((double)firstSize / totalSize, 0.05d, 0.95d);

			if (width >= height)
			{
				var firstWidth = width * ratio;
				LayoutSlice(entries, start, split + 1, x, y, firstWidth, height, firstSize, tiles);
				LayoutSlice(entries, split + 1, end, x + firstWidth, y, width - firstWidth, height, secondSize, tiles);
			}
			else
			{
				var firstHeight = height * ratio;
				LayoutSlice(entries, start, split + 1, x, y, width, firstHeight, firstSize, tiles);
				LayoutSlice(entries, split + 1, end, x, y + firstHeight, width, height - firstHeight, secondSize, tiles);
			}
		}

		private static SafeFileHandle OpenVolume(string volumeRoot)
		{
			var driveName = volumeRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			return CreateFile(
				@"\\.\" + driveName,
				GenericRead,
				FileShareReadWriteDelete,
				IntPtr.Zero,
				OpenExisting,
				0,
				IntPtr.Zero);
		}

		[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
		private static extern SafeFileHandle CreateFile(
			string lpFileName,
			uint dwDesiredAccess,
			uint dwShareMode,
			IntPtr lpSecurityAttributes,
			uint dwCreationDisposition,
			uint dwFlagsAndAttributes,
			IntPtr hTemplateFile);

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern bool DeviceIoControl(
			SafeFileHandle hDevice,
			uint dwIoControlCode,
			ref MftEnumData lpInBuffer,
			int nInBufferSize,
			byte[] lpOutBuffer,
			int nOutBufferSize,
			out int lpBytesReturned,
			IntPtr lpOverlapped);

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern bool DeviceIoControl(
			SafeFileHandle hDevice,
			uint dwIoControlCode,
			byte[] lpInBuffer,
			int nInBufferSize,
			byte[] lpOutBuffer,
			int nOutBufferSize,
			out int lpBytesReturned,
			IntPtr lpOverlapped);

		[StructLayout(LayoutKind.Sequential)]
		private struct MftEnumData
		{
			public ulong StartFileReferenceNumber;

			public long LowUsn;

			public long HighUsn;
		}

		private sealed record UsnEntry(
			ulong FileReferenceNumber,
			ulong ParentFileReferenceNumber,
			string Name,
			bool IsDirectory);

		private readonly record struct NtfsFileMetadata(
			ulong SizeBytes,
			DateTimeOffset LastModified);

		private sealed class FolderAccumulator
		{
			public ulong SizeBytes { get; set; }

			public int FileCount { get; set; }

			public int DirectoryCount { get; set; }
		}

		private sealed class ExtensionAccumulator
		{
			public ulong SizeBytes { get; set; }

			public int FileCount { get; set; }
		}

		private sealed record TreemapEntry(
			string Name,
			string Path,
			string Kind,
			ulong SizeBytes,
			int Depth);
	}
}
