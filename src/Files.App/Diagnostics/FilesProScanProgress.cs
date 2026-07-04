// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Diagnostics
{
	public sealed record FilesProScanProgress(
		string Phase,
		string CurrentPath,
		int DirectoriesVisited,
		int FilesVisited,
		ulong BytesProcessed = 0,
		ulong TotalBytes = 0)
	{
		public string DisplayText
			=> string.IsNullOrWhiteSpace(CurrentPath)
				? BuildText($"{Phase}: {DirectoriesVisited:#,##0} folders, {FilesVisited:#,##0} files")
				: BuildText($"{Phase}: {DirectoriesVisited:#,##0} folders, {FilesVisited:#,##0} files - {CurrentPath}");

		private string BuildText(string text)
			=> TotalBytes > 0
				? $"{text} ({BytesProcessed.ToSizeString()} / {TotalBytes.ToSizeString()})"
				: text;
	}
}
