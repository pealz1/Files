// Copyright (c) Files Community. Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Files.App.Utils.SaveDialog
{
	/// <summary>
	/// Pure (UI-free) helpers for the Save dialog: filter parsing, target-path resolution,
	/// and filename validation. Kept dependency-free so it can be unit tested.
	/// </summary>
	public static class SaveDialogPathHelper
	{
		private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
		{
			"CON", "PRN", "AUX", "NUL",
			"COM1","COM2","COM3","COM4","COM5","COM6","COM7","COM8","COM9",
			"LPT1","LPT2","LPT3","LPT4","LPT5","LPT6","LPT7","LPT8","LPT9",
		};

		/// <summary>
		/// Parse the pipe-delimited "Display|Pattern|Display|Pattern" string forwarded by the
		/// native dialog. Empty input yields a single "All files (*.*)" fallback.
		/// </summary>
		public static IReadOnlyList<FileTypeChoice> ParseFileTypes(string? serialized)
		{
			if (string.IsNullOrWhiteSpace(serialized))
				return new[] { new FileTypeChoice("All files", "*.*", "") };

			var parts = serialized.Split('|');
			var list = new List<FileTypeChoice>();

			for (int i = 0; i + 1 < parts.Length; i += 2)
			{
				var display = parts[i];
				var pattern = parts[i + 1];
				list.Add(new FileTypeChoice(display, pattern, PrimaryExtensionOf(pattern)));
			}

			return list.Count > 0
				? list
				: new List<FileTypeChoice> { new("All files", "*.*", "") };
		}

		/// <summary>First concrete extension of a pattern (".png"), or "" for wildcard "*.*".</summary>
		private static string PrimaryExtensionOf(string pattern)
		{
			var first = pattern.Split(';').FirstOrDefault()?.Trim() ?? "";

			// "*.png" -> ".png"; "*.*" / "*" -> ""
			var star = first.TrimStart('*');
			if (star is "" or "." or ".*")
				return "";

			return star.StartsWith('.') ? star : "." + star;
		}

		/// <summary>
		/// Compose the full target path from the current folder, the typed name, and the selected
		/// file type. A typed absolute/rooted path overrides the working directory. Extension is
		/// appended from the selected type only when the typed name has none.
		/// </summary>
		public static string ResolveTargetPath(string workingDirectory, string typedName, FileTypeChoice selectedType)
		{
			var name = typedName.Trim();

			string directory;
			string fileName;

			if (Path.IsPathRooted(name))
			{
				directory = Path.GetDirectoryName(name) ?? workingDirectory;
				fileName = Path.GetFileName(name);
			}
			else
			{
				directory = workingDirectory;
				fileName = name;
			}

			if (!string.IsNullOrEmpty(selectedType.PrimaryExtension) &&
				string.IsNullOrEmpty(Path.GetExtension(fileName)))
			{
				fileName += selectedType.PrimaryExtension;
			}

			return Path.Combine(directory, fileName);
		}

		/// <summary>
		/// Validate the typed name. For rooted input only the filename portion is checked.
		/// Rejects empty/whitespace, invalid path chars, and reserved device names.
		/// </summary>
		public static bool IsValidFileName(string typedName)
		{
			if (string.IsNullOrWhiteSpace(typedName))
				return false;

			var name = typedName.Trim();
			var fileName = Path.IsPathRooted(name) ? Path.GetFileName(name) : name;

			if (string.IsNullOrWhiteSpace(fileName))
				return false;

			if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
				return false;

			var stem = Path.GetFileNameWithoutExtension(fileName);
			if (ReservedNames.Contains(stem))
				return false;

			if (fileName.EndsWith('.') || fileName.EndsWith(' '))
				return false;

			return true;
		}
	}
}
