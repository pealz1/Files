// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Data.Enums
{
	/// <summary>
	/// Defines constants that specify parsed command line item type on Windows.
	/// </summary>
	public enum ParsedCommandType
	{
		/// <summary>
		/// Unknown command type.
		/// </summary>
		Unknown,

		/// <summary>
		/// Open directory command type
		/// </summary>
		OpenDirectory,

		/// <summary>
		/// Open path command type
		/// </summary>
		OpenPath,

		/// <summary>
		/// Explorer shell command type
		/// </summary>
		ExplorerShellCommand,

		/// <summary>
		/// Output path command type
		/// </summary>
		OutputPath,

		/// <summary>
		/// Select path command type
		/// </summary>
		SelectItem,

		/// <summary>
		/// Tag files command type
		/// </summary>
		TagFiles,

		/// <summary>
		/// Enter Save dialog mode.
		/// </summary>
		SaveDialog,

		/// <summary>
		/// Suggested file name for the Save dialog.
		/// </summary>
		SaveAs,

		/// <summary>
		/// Pipe-delimited Save dialog file-type filters.
		/// </summary>
		FileTypes,

		/// <summary>
		/// 1-based default file-type index for the Save dialog.
		/// </summary>
		FileTypeIndex,

		/// <summary>
		/// Enter Open dialog mode (file picker for upload/open).
		/// </summary>
		OpenDialog,

		/// <summary>
		/// Open dialog should pick a folder instead of files (FOS_PICKFOLDERS).
		/// </summary>
		PickFolders,

		/// <summary>
		/// Name of the per-dialog completion event the app signals when the dialog finishes.
		/// </summary>
		DoneEvent
	}
}
